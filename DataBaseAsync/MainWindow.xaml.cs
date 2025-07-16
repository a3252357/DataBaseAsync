using Microsoft.Extensions.Configuration;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DatabaseReplication.Follower;
using DatabaseReplication;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using DatabaseReplication.Leader;

namespace DataBaseAsync
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private DispatcherTimer _timeTimer;
        private DispatcherTimer _statusTimer;
        private System.Threading.Timer _timeSyncTimer;
        private bool _isRunning = false;
        private readonly StringBuilder _logBuilder = new StringBuilder();
        private DatabaseBasedFollowerReplicator _replicator;
        private DatabaseReplicationConfig _config;
        private List<TableConfig> _tableConfigs;
        private readonly List<string> _logLines = new List<string>();
        private const int MaxLogLines = 1000; // 减少到1000行，避免界面卡死
        private ConsoleRedirector _consoleRedirector;
        
        // UI日志队列优化相关字段
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> _uiLogQueue = new System.Collections.Concurrent.ConcurrentQueue<string>();
        private readonly System.Threading.AutoResetEvent _uiLogEvent = new System.Threading.AutoResetEvent(false);
        private readonly System.Threading.CancellationTokenSource _uiLogCancellationTokenSource = new System.Threading.CancellationTokenSource();
        private System.Threading.Tasks.Task _uiLogProcessorTask;
        private const int MaxUILogQueueSize = 200; // 减少队列大小
        private volatile bool _isUILogProcessorRunning = false;
        private volatile bool _enableRealTimeLog = true; // 默认启用实时日志显示
        
        // 缓存相关字段 - 优化内存使用
        private DateTime _lastStatusRefreshTime = DateTime.MinValue;
        private readonly TimeSpan _statusCacheTimeout = TimeSpan.FromSeconds(15); // 缓存15秒
        private List<SyncStatusItem> _cachedLeaderToFollowerStatus;
        private List<SyncStatusItem> _cachedFollowerToLeaderStatus;
        
        // 内存监控相关字段
        private long _lastMemoryUsage = 0;
        private DateTime _lastMemoryCheckTime = DateTime.MinValue;
        
        public ObservableCollection<SyncStatusItem> LeaderToFollowerStatus { get; set; }
        public ObservableCollection<SyncStatusItem> FollowerToLeaderStatus { get; set; }
        public ObservableCollection<SyncStatusItem> LeaderToFollowerItems { get; set; } = new ObservableCollection<SyncStatusItem>();
        public ObservableCollection<SyncStatusItem> FollowerToLeaderItems { get; set; } = new ObservableCollection<SyncStatusItem>();
        public ObservableCollection<SyncStatusItem> BidirectionalItems { get; set; } = new ObservableCollection<SyncStatusItem>();
        public ObservableCollection<TableConfigViewModel> TableConfigs { get; set; } = new ObservableCollection<TableConfigViewModel>();
        
        public event PropertyChangedEventHandler PropertyChanged;
        
        public MainWindow()
        {
            InitializeComponent();
            InitializeData();
            InitializeTimers();
            LoadConfiguration();
            LoadTableConfigurations(); // 在配置加载后重新加载表配置
            SetupConsoleRedirection();
            
            // 设置窗口标题显示从库编码
            UpdateWindowTitle();
            
            // 启动UI日志处理器
            StartUILogProcessor();
            
            // 绑定表配置数据源
            TableConfigGrid.ItemsSource = TableConfigs;
            
            // 异步自动启动同步服务
            _ = Task.Run(async () =>
            {
                await Task.Delay(2000); // 等待界面完全加载
                await Dispatcher.InvokeAsync(async () =>
                {
                    await AutoStartSyncService();
                });
            });
        }
        
        private async Task LoadRealSyncStatusToCollections(List<SyncStatusItem> leaderToFollowerList, List<SyncStatusItem> followerToLeaderList)
        {
            try
            {
                // 检查运行状态，如果程序正在关闭则快速退出
                if (!_isRunning)
                {
                    return;
                }
                
                // 使用只读连接进行查询操作
                var readOnlyConnectionString = !string.IsNullOrEmpty(_config.LeaderReadOnlyConnectionString) 
                    ? _config.LeaderReadOnlyConnectionString 
                    : _config.LeaderConnectionString;
                    
                using (var leaderDbContext = LeaderDbContext.CreateReadOnlyContext(readOnlyConnectionString, _tableConfigs))
                {
                    // 再次检查运行状态
                    if (!_isRunning)
                    {
                        return;
                    }
                    
                    // 使用Entity Framework查询同步进度
                    var syncProgresses = await leaderDbContext.SyncProgresses
                        .Where(sp => sp.FollowerServerId == _config.FollowerServerId)
                        .ToListAsync();
                    
                    // 检查运行状态
                    if (!_isRunning)
                    {
                        return;
                    }
                    
                    var syncProgressDict = syncProgresses.ToDictionary(
                        sp => sp.TableName,
                        sp => (sp.LastSyncedId, (DateTime?)sp.LastSyncTime)
                    );
                    
                    // 为每个配置的表创建状态项
                    foreach (var tableConfig in _tableConfigs.Where(t => t.Enabled))
                    {
                        // 在每次循环中检查运行状态
                        if (!_isRunning)
                        {
                            return;
                        }
                        
                        var hasProgress = syncProgressDict.TryGetValue(tableConfig.TableName, out var progress);
                        
                        var statusItem = new SyncStatusItem
                        {
                            TableName = tableConfig.TableName,
                            LastSyncedId = hasProgress ? progress.LastSyncedId : 0,
                            LastSyncTime = hasProgress ? (progress.Item2 ?? DateTime.MinValue) : DateTime.MinValue,
                            PendingCount = await GetPendingCountFromReplicationLogs(tableConfig, hasProgress ? progress.LastSyncedId : 0, leaderDbContext),
                            Status = _isRunning ? "运行中" : "已停止"
                        };
                        
                        // 根据复制方向添加到相应的集合
                        if (tableConfig.ReplicationDirection == ReplicationDirection.LeaderToFollower ||
                            tableConfig.ReplicationDirection == ReplicationDirection.Bidirectional)
                        {
                            leaderToFollowerList.Add(statusItem);
                        }
                    }
                }

                // 初始化从库上下文
                using (var followerContext = new FollowerDbContext(
                    new DbContextOptionsBuilder<FollowerDbContext>()
                        .UseMySql(ServerVersion.AutoDetect(_config.FollowerConnectionString))
                        .Options,
                    _config.FollowerConnectionString,
                    _config.FollowerServerId,
                    _tableConfigs))
                {
                    // 再次检查运行状态
                    if (!_isRunning)
                    {
                        return;
                    }

                    // 使用Entity Framework查询同步进度
                    var syncProgresses = await followerContext.SyncProgresses
                        .Where(sp => sp.FollowerServerId == _config.FollowerServerId)
                        .ToListAsync();

                    // 检查运行状态
                    if (!_isRunning)
                    {
                        return;
                    }

                    var syncProgressDict = syncProgresses.ToDictionary(
                        sp => sp.TableName,
                        sp => (sp.LastSyncedId, (DateTime?)sp.LastSyncTime)
                    );

                    // 为每个配置的表创建状态项
                    foreach (var tableConfig in _tableConfigs.Where(t => t.Enabled))
                    {
                        // 在每次循环中检查运行状态
                        if (!_isRunning)
                        {
                            return;
                        }

                        var hasProgress = syncProgressDict.TryGetValue(tableConfig.TableName, out var progress);

                        var statusItem = new SyncStatusItem
                        {
                            TableName = tableConfig.TableName,
                            LastSyncedId = hasProgress ? progress.LastSyncedId : 0,
                            LastSyncTime = hasProgress ? (progress.Item2 ?? DateTime.MinValue) : DateTime.MinValue,
                            PendingCount = await GetPendingCountFromReplicationLogs(tableConfig, hasProgress ? progress.LastSyncedId : 0, followerContext),
                            Status = _isRunning ? "运行中" : "已停止"
                        };

                        if (tableConfig.ReplicationDirection == ReplicationDirection.FollowerToLeader ||
                            tableConfig.ReplicationDirection == ReplicationDirection.Bidirectional)
                        {
                            followerToLeaderList.Add(statusItem);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // 如果获取真实状态失败，显示基本信息
                if (_isRunning) // 只有在程序仍在运行时才添加基本信息
                {
                    foreach (var tableConfig in _tableConfigs.Where(t => t.Enabled))
                    {
                        var statusItem = new SyncStatusItem
                        {
                            TableName = tableConfig.TableName,
                            LastSyncedId = 0,
                            LastSyncTime = DateTime.MinValue,
                            PendingCount = 0,
                            Status = _isRunning ? "运行中" : "已停止"
                        };
                        
                        if (tableConfig.ReplicationDirection == ReplicationDirection.LeaderToFollower ||
                            tableConfig.ReplicationDirection == ReplicationDirection.Bidirectional)
                        {
                            leaderToFollowerList.Add(statusItem);
                        }
                        
                        if (tableConfig.ReplicationDirection == ReplicationDirection.FollowerToLeader ||
                            tableConfig.ReplicationDirection == ReplicationDirection.Bidirectional)
                        {
                            followerToLeaderList.Add(statusItem);
                        }
                    }
                }
            }
        }
        
        private void UpdateStatusCollections(ObservableCollection<SyncStatusItem> existingCollection, List<SyncStatusItem> newItems)
        {
            // 更新现有项目或添加新项目
            foreach (var newItem in newItems)
            {
                var existingItem = existingCollection.FirstOrDefault(x => x.TableName == newItem.TableName);
                if (existingItem != null)
                {
                    // 更新现有项目
                    existingItem.LastSyncedId = newItem.LastSyncedId;
                    existingItem.LastSyncTime = newItem.LastSyncTime;
                    existingItem.PendingCount = newItem.PendingCount;
                    existingItem.Status = newItem.Status;
                }
                else
                {
                    // 添加新项目
                    existingCollection.Add(newItem);
                }
            }
            
            // 移除不再存在的项目
            var itemsToRemove = existingCollection.Where(x => !newItems.Any(n => n.TableName == x.TableName)).ToList();
            foreach (var item in itemsToRemove)
             {
                 existingCollection.Remove(item);
             }
        }
        
        private void InitializeData()
        {
            LeaderToFollowerStatus = new ObservableCollection<SyncStatusItem>();
            FollowerToLeaderStatus = new ObservableCollection<SyncStatusItem>();
            
            LeaderToFollowerGrid.ItemsSource = LeaderToFollowerStatus;
            FollowerToLeaderGrid.ItemsSource = FollowerToLeaderStatus;
            
            LoadTableConfigurations();
        }
        
        private void InitializeTimers()
        {
            // 时间显示定时器
            _timeTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _timeTimer.Tick += (s, e) => TimeText.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            _timeTimer.Start();
            
            // 状态刷新定时器 - 优化：减少刷新频率以降低内存使用
            _statusTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(30)
            };
            _statusTimer.Tick += StatusTimer_Tick;
            _statusTimer.Start(); // 启动状态刷新定时器
        }
        
        private void LoadConfiguration()
        {
            try
            {
                var configuration = new ConfigurationBuilder()
                    .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                    .Build();
                
                _config = configuration.GetSection("DatabaseReplication").Get<DatabaseReplicationConfig>();
                if (_config == null)
                {
                    AddLog("无法读取配置文件，请检查 appsettings.json");
                    return;
                }
                
                // 从配置文件读取实时日志设置
                _enableRealTimeLog = _config.UI?.EnableRealTimeLog ?? true;
                
                LeaderConnectionTextBox.Text = _config.LeaderConnectionString;
                LeaderReadOnlyConnectionTextBox.Text = _config.LeaderReadOnlyConnectionString ?? "";
                FollowerConnectionTextBox.Text = _config.FollowerConnectionString;
                IntervalTextBox.Text = "30"; // 显示默认间隔
                BatchSizeTextBox.Text = _config.BatchSize.ToString();
                RetentionDaysTextBox.Text = _config.DataRetentionDays.ToString();
                CleanupIntervalTextBox.Text = _config.CleanupIntervalHours.ToString();
                InitializeExistingDataCheckBox.IsChecked = _config.InitializeExistingData;
                
                // 加载时间同步配置
                LoadTimeSyncConfiguration();
                
                // 初始化表配置
                InitializeTableConfigs();
                
                AddLog("配置加载完成");
            }
            catch (Exception ex)
            {
                AddLog($"加载配置失败: {ex.Message}");
            }
        }
        
        private void UpdateWindowTitle()
        {
            try
            {
                if (_config != null && !string.IsNullOrEmpty(_config.FollowerServerId))
                {
                    this.Title = $"数据库同步服务 - 从库: {_config.FollowerServerId}";
                }
                else
                {
                    this.Title = "数据库同步服务";
                }
            }
            catch (Exception ex)
            {
                AddLog($"更新窗口标题失败: {ex.Message}");
            }
        }
        
        private void InitializeTableConfigs()
        {
            _tableConfigs = new List<TableConfig>();
            foreach (var tableConfigData in _config.Tables)
            {
                // 根据实体类型名称获取类型
                Type entityType = GetEntityType(tableConfigData.EntityTypeName);
                if (entityType == null)
                {
                    AddLog($"找不到实体类型: {tableConfigData.EntityTypeName}");
                    continue;
                }
                
                // 解析复制方向和冲突策略
                if (!Enum.TryParse<ReplicationDirection>(tableConfigData.ReplicationDirection, out var direction))
                {
                    AddLog($"无效的复制方向: {tableConfigData.ReplicationDirection}");
                    continue;
                }
                
                if (!Enum.TryParse<ConflictResolutionStrategy>(tableConfigData.ConflictStrategy, out var conflictStrategy))
                {
                    conflictStrategy = ConflictResolutionStrategy.PreferLeader;
                }
                
                _tableConfigs.Add(new TableConfig
                {
                    EntityType = entityType,
                    TableName = tableConfigData.TableName,
                    PrimaryKey = tableConfigData.PrimaryKey,
                    ReplicationIntervalSeconds = tableConfigData.ReplicationIntervalSeconds,
                    Enabled = tableConfigData.Enabled,
                    InitializeExistingData = tableConfigData.InitializeExistingData,
                    ReplicationDirection = direction,
                    ConflictStrategy = conflictStrategy,
                    ConflictResolutionPriorityFields = tableConfigData.ConflictResolutionPriorityFields
                });
            }
        }
        
        private Type GetEntityType(string entityTypeName)
        {
            // 在当前程序集中查找实体类型
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            var type = assembly.GetTypes().FirstOrDefault(t => t.Name == entityTypeName);
            
            if (type == null)
            {
                // 在引用的程序集中查找
                foreach (var referencedAssembly in assembly.GetReferencedAssemblies())
                {
                    try
                    {
                        var loadedAssembly = System.Reflection.Assembly.Load(referencedAssembly);
                        type = loadedAssembly.GetTypes().FirstOrDefault(t => t.Name == entityTypeName);
                        if (type != null) break;
                    }
                    catch
                    {
                        // 忽略加载失败的程序集
                    }
                }
            }
            
            return type;
        }
        
        private async void StartButton_Click(object sender, RoutedEventArgs e)
        {
            await StartSyncService();
        }
        
        private async Task AutoStartSyncService()
        {
            try
            {
                AddLog("正在自动启动同步服务...");
                await StartSyncService();
            }
            catch (Exception ex)
            {
                AddLog($"自动启动失败: {ex.Message}");
            }
        }
        
        private async Task StartSyncService()
        {
            if (_isRunning)
            {
                AddLog("同步服务已在运行中");
                return;
            }
            
            try
            {
                // 禁用启动按钮，防止重复点击
                StartButton.IsEnabled = false;
                StatusText.Text = "正在启动...";
                StatusText.Foreground = System.Windows.Media.Brushes.Orange;
                
                // 加载配置
                if (_config == null)
                {
                    LoadConfiguration();
                }
                
                if (_config == null || _tableConfigs == null || !_tableConfigs.Any())
                {
                    AddLog("配置未加载或表配置为空，无法启动同步服务");
                    StatusText.Text = "配置错误";
                    StatusText.Foreground = System.Windows.Media.Brushes.Red;
                    StartButton.IsEnabled = true;
                    return;
                }
                
                AddLog("正在初始化数据库连接...");
                
                // 异步初始化数据库连接
                await Task.Run(async () =>
                {
                    await InitializeDatabaseContexts();
                });
                
                AddLog("正在创建复制器...");
                
                // 创建复制器
                _replicator = new DatabaseBasedFollowerReplicator(
                    _config.FollowerServerId,
                    _config.FollowerConnectionString,
                    _config.LeaderConnectionString,
                     _config.LeaderReadOnlyConnectionString,
                    _tableConfigs,
                    _config.BatchSize,
                    _config.DataRetentionDays,
                    _config.CleanupIntervalHours);
                
                // 初始化现有数据（如果配置要求）
                if (_config.InitializeExistingData)
                {
                    AddLog("正在初始化现有数据...");
                    await Task.Run(async () =>
                    {
                        await _replicator.InitializeExistingData(true);
                    });
                }
                
                AddLog("正在启动复制服务...");
                
                // 启动复制服务
                await Task.Run(() =>
                {
                    _replicator.StartReplication();
                });
                
                _isRunning = true;
                StatusText.Text = "运行中";
                StatusText.Foreground = System.Windows.Media.Brushes.LimeGreen;
                StopButton.IsEnabled = true;
                StartButton.IsEnabled = false;
                
                // 启动状态刷新定时器
                _statusTimer.Start();
                
                // 启动时间同步功能
                await StartTimeSynchronization();
                
                AddLog("数据库同步服务启动成功");
                
                // 立即刷新一次状态
                await RefreshStatusSilently();
            }
            catch (Exception ex)
            {
                AddLog($"启动失败: {ex.Message}");
                StatusText.Text = "启动失败";
                StatusText.Foreground = System.Windows.Media.Brushes.Red;
                _isRunning = false;
                StartButton.IsEnabled = true;
                StopButton.IsEnabled = false;
            }
        }
        
        private async Task StartTimeSynchronization()
        {
            try
            {
                if (_config?.TimeSynchronization?.Enabled != true)
                {
                    AddLog("时间同步功能未启用");
                    return;
                }
                
                AddLog("正在检查系统时间权限...");
                if (!DirectTimeSynchronizationService.HasSystemTimePrivilege())
                {
                    AddLog("警告：程序需要管理员权限才能修改系统时间");
                    AddLog("正在请求管理员权限...");
                    
                    // 尝试请求管理员权限
                    if (!DirectTimeSynchronizationService.RequestAdministratorPrivileges())
                    {
                        AddLog("错误：无法获取管理员权限，时间同步功能将被禁用");
                        return;
                    }
                    // 如果成功请求权限，程序会自动重启，这里不会执行到
                }
                
                AddLog("检测到管理员权限，启用直接时间同步功能");
                
                // 启动时同步
                if (_config.TimeSynchronization.SyncOnStartup)
                {
                    AddLog("正在执行启动时间同步...");
                    bool syncResult = DirectTimeSynchronizationService.SynchronizeToLeaderTime(
                        _config.LeaderConnectionString, 
                        _config.TimeSynchronization.MaxAllowedDifferenceSeconds);
                    AddLog(syncResult ? "启动时间同步完成" : "启动时间同步跳过（时间差异在阈值内或同步失败）");
                }
                
                // 设置定期同步
                if (_config.TimeSynchronization.TimeSyncIntervalMinutes > 0)
                {
                    _timeSyncTimer = new System.Threading.Timer(_ =>
                    {
                        try
                        {
                            Dispatcher.Invoke(() => AddLog("执行定期时间同步检查..."));
                            bool syncResult = DirectTimeSynchronizationService.SynchronizeToLeaderTime(
                                _config.LeaderConnectionString, 
                                _config.TimeSynchronization.MaxAllowedDifferenceSeconds);
                            Dispatcher.Invoke(() => AddLog(syncResult ? "定期时间同步完成" : "定期时间同步跳过（时间差异在阈值内或同步失败）"));
                        }
                        catch (Exception ex)
                        {
                            Dispatcher.Invoke(() => AddLog($"定期时间同步出错: {ex.Message}"));
                        }
                    }, null, TimeSpan.FromMinutes(_config.TimeSynchronization.TimeSyncIntervalMinutes), TimeSpan.FromMinutes(_config.TimeSynchronization.TimeSyncIntervalMinutes));
                    
                    AddLog($"时间同步定时器已启动，间隔: {_config.TimeSynchronization.TimeSyncIntervalMinutes}分钟");
                }
            }
            catch (Exception ex)
            {
                AddLog($"启动时间同步功能失败: {ex.Message}");
            }
        }
        
        private async Task InitializeDatabaseContexts()
        {
            try
            {
                // 初始化主库上下文
                // 初始化操作需要写权限，使用主连接
                using (var leaderContext = LeaderDbContext.CreateWriteContext(_config.LeaderConnectionString, _tableConfigs))
                {
                    leaderContext.Initialize();
                    AddLog("主库初始化完成");
                }
                
                // 初始化从库上下文
                using (var followerContext = new FollowerDbContext(
                    new DbContextOptionsBuilder<FollowerDbContext>()
                        .UseMySql(ServerVersion.AutoDetect(_config.FollowerConnectionString))
                        .Options,
                    _config.FollowerConnectionString,
                    _config.FollowerServerId,
                    _tableConfigs))
                {
                    followerContext.Initialize();
                    AddLog("从库初始化完成");
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"数据库初始化失败: {ex.Message}", ex);
            }
        }
        
        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isRunning)
            {
                AddLog("同步服务未在运行");
                return;
            }
            
            try
            {
                // 停止状态刷新定时器
                _statusTimer?.Stop();
                
                // 停止时间同步定时器
                if (_timeSyncTimer != null)
                {
                    _timeSyncTimer.Dispose();
                    _timeSyncTimer = null;
                    AddLog("时间同步定时器已停止");
                }
                
                // 停止复制器
                if (_replicator != null)
                {
                    _replicator.StopReplication();
                    _replicator.Dispose();
                    _replicator = null;
                }
                
                _isRunning = false;
                StatusText.Text = "已停止";
                StatusText.Foreground = System.Windows.Media.Brushes.Orange;
                StartButton.IsEnabled = true;
                StopButton.IsEnabled = false;
                
                // 清空状态显示
                LeaderToFollowerStatus.Clear();
                FollowerToLeaderStatus.Clear();
                
                AddLog("数据库同步服务已停止");
            }
            catch (Exception ex)
            {
                AddLog($"停止失败: {ex.Message}");
            }
        }
        
        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await RefreshStatusSilently();
                AddLog("状态刷新完成");
            }
            catch (Exception ex)
            {
                AddLog($"刷新状态失败: {ex.Message}");
            }
        }
        
        private async void ManualRetryButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_replicator == null)
                {
                    AddLog("同步服务未初始化，无法执行手动重试");
                    MessageBox.Show("同步服务未初始化，请先启动同步服务", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                
                // 显示确认对话框
                var result = MessageBox.Show(
                    "手动重试将重置同步进度到最早的错误数据之前，并清除错误日志。\n\n确定要继续吗？",
                    "确认手动重试",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                
                if (result != MessageBoxResult.Yes)
                {
                    return;
                }
                
                AddLog("开始执行手动重试...");
                
                // 获取失败数据统计
                var statistics = await _replicator.GetFailedDataStatistics();
                if (statistics.Count == 0)
                {
                    AddLog("没有找到失败的数据，无需重试");
                    MessageBox.Show("没有找到失败的数据，无需重试", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                
                AddLog($"找到 {statistics.Count} 个表的失败数据:");
                foreach (var stat in statistics)
                {
                    AddLog($"  表 {stat.Key}: {stat.Value} 条失败记录");
                }
                
                // 执行手动重试
                var retryResult = await _replicator.ManualRetryFailedData();
                
                if (retryResult.Success)
                {
                    var tableDetails = string.Join(", ", retryResult.ProcessedTables.Select(kvp => $"{kvp.Key}({kvp.Value}条)"));
                    AddLog($"手动重试完成: 处理了 {retryResult.ProcessedCount} 条失败数据，涉及表: {tableDetails}");
                    MessageBox.Show(
                        $"手动重试完成！\n\n{retryResult.Message}\n\n处理详情:\n{tableDetails}",
                        "重试完成",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    
                    // 刷新状态显示
                    await RefreshStatusSilently();
                }
                else
                {
                    AddLog($"手动重试失败: {retryResult.Message}");
                    MessageBox.Show($"手动重试失败: {retryResult.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                AddLog($"手动重试异常: {ex.Message}");
                MessageBox.Show($"手动重试异常: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private async Task RefreshStatusSilently()
        {
            try
            {
                // 检查是否正在运行，如果不是则快速退出
                if (!_isRunning || _config == null)
                {
                    return;
                }
                
                // 缓存机制：检查是否需要刷新数据
                var now = DateTime.Now;
                if (_cachedLeaderToFollowerStatus != null && _cachedFollowerToLeaderStatus != null &&
                    now - _lastStatusRefreshTime < _statusCacheTimeout)
                {
                    // 使用缓存数据更新UI
                    UpdateStatusCollections(LeaderToFollowerStatus, _cachedLeaderToFollowerStatus);
                    UpdateStatusCollections(FollowerToLeaderStatus, _cachedFollowerToLeaderStatus);
                    return;
                }
                
                // 获取新的状态数据但不清空现有显示
                var newLeaderToFollowerStatus = new List<SyncStatusItem>();
                var newFollowerToLeaderStatus = new List<SyncStatusItem>();
                
                // 再次检查运行状态，防止在数据库操作期间程序关闭
                if (!_isRunning)
                {
                    return;
                }
                
                await LoadRealSyncStatusToCollections(newLeaderToFollowerStatus, newFollowerToLeaderStatus);
                
                // 在更新UI前再次检查运行状态
                if (!_isRunning)
                {
                    return;
                }
                
                // 更新缓存
                _cachedLeaderToFollowerStatus = newLeaderToFollowerStatus;
                _cachedFollowerToLeaderStatus = newFollowerToLeaderStatus;
                _lastStatusRefreshTime = now;
                
                // 更新现有项目而不是清空重建
                UpdateStatusCollections(LeaderToFollowerStatus, newLeaderToFollowerStatus);
                UpdateStatusCollections(FollowerToLeaderStatus, newFollowerToLeaderStatus);
            }
            catch (Exception ex)
            {
                // 静默刷新失败时不记录日志，避免日志过多
            }
        }
        
        private async Task<int> GetPendingCountFromReplicationLogs(TableConfig tableConfig, int lastSyncedId, DbContext dbContext)
        {
            try
            {
                // 检查运行状态，如果程序正在关闭则快速返回
                if (!_isRunning)
                {
                    return 0;
                }
                
                // 使用只读连接进行查询操作
                var readOnlyConnectionString = !string.IsNullOrEmpty(_config.LeaderReadOnlyConnectionString) 
                    ? _config.LeaderReadOnlyConnectionString 
                    : _config.LeaderConnectionString;
                    
                    // 再次检查运行状态
                    if (!_isRunning)
                    {
                        return 0;
                    }
                    
                    using (var connection = dbContext.Database.GetDbConnection())
                    {
                        await connection.OpenAsync();
                        
                        // 检查运行状态
                        if (!_isRunning)
                        {
                            return 0;
                        }
                        
                        using (var command = connection.CreateCommand())
                        {
                            // 从replication_logs表获取待同步的记录数量
                            // processed = 0 表示未处理的记录
                            command.CommandText = @"
                                SELECT COUNT(*) 
                                FROM replication_logs 
                                WHERE table_name = @tableName 
                                  AND processed = 0 
                                  AND id > @lastSyncedId";
                            
                            var tableNameParam = command.CreateParameter();
                            tableNameParam.ParameterName = "@tableName";
                            tableNameParam.Value = tableConfig.TableName;
                            command.Parameters.Add(tableNameParam);
                            
                            var lastIdParam = command.CreateParameter();
                            lastIdParam.ParameterName = "@lastSyncedId";
                            lastIdParam.Value = lastSyncedId;
                            command.Parameters.Add(lastIdParam);
                            
                            var result = await command.ExecuteScalarAsync();
                            return Convert.ToInt32(result);
                        }
                    }
            }
            catch
            {
                return 0; // 如果查询失败，返回0
            }
        }
        private async Task LoadBasicStatus()
        {
            // 显示基本的表配置信息
            foreach (var tableConfig in _tableConfigs.Where(t => t.Enabled))
            {
                var statusItem = new SyncStatusItem
                {
                    TableName = tableConfig.TableName,
                    LastSyncedId = 0,
                    LastSyncTime = DateTime.MinValue,
                    PendingCount = 0,
                    Status = _isRunning ? "运行中" : "已停止"
                };
                
                if (tableConfig.ReplicationDirection == ReplicationDirection.LeaderToFollower ||
                    tableConfig.ReplicationDirection == ReplicationDirection.Bidirectional)
                {
                    LeaderToFollowerStatus.Add(statusItem);
                }
                
                if (tableConfig.ReplicationDirection == ReplicationDirection.FollowerToLeader ||
                    tableConfig.ReplicationDirection == ReplicationDirection.Bidirectional)
                {
                    FollowerToLeaderStatus.Add(statusItem);
                }
            }
        }
        
        private void LoadTableConfigurations()
        {
            try
            {
                TableConfigs.Clear();
                
                if (_tableConfigs != null)
                {
                    // 从appsettings.json读取现有配置
                    var configuration = new ConfigurationBuilder()
                        .SetBasePath(Directory.GetCurrentDirectory())
                        .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                        .Build();
                    
                    var config = configuration.GetSection("DatabaseReplication").Get<DatabaseReplicationConfig>();
                    var existingTableNames = config?.Tables?.Select(t => t.TableName).ToHashSet() ?? new HashSet<string>();
                    
                    foreach (var tableConfig in _tableConfigs)
                    {
                        var conflictFields = tableConfig.ConflictResolutionPriorityFields != null ? 
                            string.Join(", ", tableConfig.ConflictResolutionPriorityFields) : "";
                        
                        TableConfigs.Add(new TableConfigViewModel
                        {
                            TableName = tableConfig.TableName,
                            EntityTypeName = tableConfig.EntityType?.Name ?? "Unknown",
                            PrimaryKey = tableConfig.PrimaryKey,
                            ReplicationDirection = tableConfig.ReplicationDirection.ToString(),
                            ConflictStrategy = tableConfig.ConflictStrategy.ToString(),
                            ReplicationIntervalSeconds = tableConfig.ReplicationIntervalSeconds,
                            Enabled = tableConfig.Enabled,
                            InitializeExistingData = tableConfig.InitializeExistingData,
                            ConflictResolutionPriorityFieldsText = conflictFields,
                            IsExistingConfig = existingTableNames.Contains(tableConfig.TableName)
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                AddLog($"加载表配置失败: {ex.Message}");
            }
        }
        
        private void RefreshConfigButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                LoadConfiguration();
                InitializeTableConfigs();
                LoadTableConfigurations();
                AddLog("配置已刷新");
                MessageBox.Show("配置已刷新", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                AddLog($"刷新配置失败: {ex.Message}");
                MessageBox.Show($"刷新配置失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private async void ManualSyncButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var tableName = button?.Tag as string;
            
            if (string.IsNullOrEmpty(tableName))
            {
                AddLog("无法获取表名");
                return;
            }
            
            // 查找对应的表配置
            var tableConfigViewModel = TableConfigs.FirstOrDefault(t => t.TableName == tableName);
            if (tableConfigViewModel == null)
            {
                AddLog($"找不到表 {tableName} 的配置");
                return;
            }
            
            var tableConfig = _tableConfigs?.FirstOrDefault(t => t.TableName == tableName);
            if (tableConfig == null)
            {
                AddLog($"找不到表 {tableName} 的内部配置");
                return;
            }
            
            if (!tableConfig.Enabled)
            {
                AddLog($"表 {tableName} 未启用，无法执行手动同步");
                return;
            }
            
            try
            {
                // 设置同步状态
                tableConfigViewModel.IsManualSyncing = true;
                AddLog($"开始手动同步表 {tableName}...");
                
                // 暂停该表的自动同步
                if (_replicator != null)
                {
                    await _replicator.PauseTableReplication(tableName);
                    AddLog($"已暂停表 {tableName} 的自动同步");
                }
                
                // 执行手动同步
                await PerformManualTableSync(tableConfig);
                
                AddLog($"表 {tableName} 手动同步完成");
            }
            catch (Exception ex)
            {
                AddLog($"表 {tableName} 手动同步失败: {ex.Message}");
                MessageBox.Show($"手动同步失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                // 恢复该表的自动同步
                if (_replicator != null)
                {
                    try
                    {
                        await _replicator.ResumeTableReplication(tableName);
                        AddLog($"已恢复表 {tableName} 的自动同步");
                    }
                    catch (Exception ex)
                    {
                        AddLog($"恢复表 {tableName} 自动同步失败: {ex.Message}");
                    }
                }
                
                // 重置同步状态
                tableConfigViewModel.IsManualSyncing = false;
                
                // 刷新状态显示
                await RefreshStatusSilently();
            }
        }
        
        private async Task PerformManualTableSync(TableConfig tableConfig)
        {
            try
            {
                if (_replicator == null)
                {
                    throw new InvalidOperationException("复制器未初始化");
                }
                
                AddLog($"正在执行表 {tableConfig.TableName} 的完整数据同步...");
                
                // 执行该表的完整数据同步
                await _replicator.SyncTableFromLeaderToFollower(tableConfig);
                
                AddLog($"表 {tableConfig.TableName} 完整数据同步完成");
            }
            catch (Exception ex)
            {
                AddLog($"表 {tableConfig.TableName} 手动同步过程中出错: {ex.Message}");
                throw;
            }
        }
        

        
        private void LoadTimeSyncConfiguration()
        {
            try
            {
                if (_config.TimeSynchronization!=null)
                {
                    TimeSyncEnabledCheckBox.IsChecked = _config.TimeSynchronization.Enabled;
                    TimeSyncIntervalTextBox.Text = _config.TimeSynchronization.TimeSyncIntervalMinutes.ToString();
                    RequireElevatedPrivilegesCheckBox.IsChecked = true;
                }
                else
                {
                    // 设置默认值
                    TimeSyncEnabledCheckBox.IsChecked = false;
                    TimeSyncIntervalTextBox.Text = "60";
                    RequireElevatedPrivilegesCheckBox.IsChecked = true;
                }
            }
            catch (Exception ex)
            {
                AddLog($"加载时间同步配置失败: {ex.Message}");
                // 设置默认值
                TimeSyncEnabledCheckBox.IsChecked = false;
                TimeSyncIntervalTextBox.Text = "60";
                RequireElevatedPrivilegesCheckBox.IsChecked = true;
            }
        }
        
        private void SaveTimeSyncConfigButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!int.TryParse(TimeSyncIntervalTextBox.Text, out int syncInterval) || syncInterval <= 0)
                {
                    MessageBox.Show("请输入有效的同步间隔（大于0的整数）", "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                
                // 读取当前配置文件
                var configPath = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");
                var jsonContent = File.ReadAllText(configPath);
                var jsonObj = JObject.Parse(jsonContent);
                
                // 确保TimeSynchronization节点存在
                if (jsonObj["TimeSynchronization"] == null)
                {
                    jsonObj["TimeSynchronization"] = new JObject();
                }
                
                // 更新TimeSynchronization配置
                jsonObj["TimeSynchronization"]["Enabled"] = TimeSyncEnabledCheckBox.IsChecked ?? false;
                jsonObj["TimeSynchronization"]["SyncIntervalMinutes"] = syncInterval;
                jsonObj["TimeSynchronization"]["RequireElevatedPrivileges"] = RequireElevatedPrivilegesCheckBox.IsChecked ?? true;
                
                // 保存文件
                var updatedJson = jsonObj.ToString(Formatting.Indented);
                File.WriteAllText(configPath, updatedJson);
                
                AddLog("时间同步配置已保存");
                MessageBox.Show("时间同步配置已保存", "保存成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                AddLog($"保存时间同步配置失败: {ex.Message}");
                MessageBox.Show($"保存时间同步配置失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        

        
        private async void StatusTimer_Tick(object sender, EventArgs e)
        {
            if (_isRunning)
            {
                await RefreshStatusSilently();
                
                // 内存监控 - 每分钟检查一次内存使用情况
                var now = DateTime.Now;
                if (now - _lastMemoryCheckTime >= TimeSpan.FromMinutes(1))
                {
                    CheckMemoryUsage();
                    _lastMemoryCheckTime = now;
                }
            }
        }
        
        private void CheckMemoryUsage()
        {
            try
            {
                var currentProcess = System.Diagnostics.Process.GetCurrentProcess();
                var currentMemoryMB = currentProcess.WorkingSet64 / (1024 * 1024);
                
                // 如果内存使用超过1GB，记录警告
                if (currentMemoryMB > 1024)
                {
                    var memoryIncrease = currentMemoryMB - _lastMemoryUsage;
                    if (_lastMemoryUsage > 0 && memoryIncrease > 100) // 内存增长超过100MB
                    {
                        AddLog($"内存使用警告: 当前 {currentMemoryMB}MB (+{memoryIncrease}MB), 日志队列: {_uiLogQueue.Count}, 缓存状态: {(_cachedLeaderToFollowerStatus?.Count ?? 0)}/{(_cachedFollowerToLeaderStatus?.Count ?? 0)}");
                    }
                    else if (currentMemoryMB > 1536) // 超过1.5GB时强制记录
                    {
                        AddLog($"高内存使用: {currentMemoryMB}MB, 建议重启程序");
                        
                        // 强制垃圾回收
                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                        GC.Collect();
                    }
                }
                
                _lastMemoryUsage = currentMemoryMB;
            }
            catch (Exception ex)
            {
                // 内存检查失败时不记录日志，避免递归
            }
        }
        
        private void SaveConfigButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 验证连接字符串不为空
                if (string.IsNullOrWhiteSpace(LeaderConnectionTextBox.Text))
                {
                    MessageBox.Show("主数据库连接字符串不能为空", "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                
                if (string.IsNullOrWhiteSpace(FollowerConnectionTextBox.Text))
                {
                    MessageBox.Show("从数据库连接字符串不能为空", "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                
                // 读取当前配置文件
                var configPath = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");
                var jsonContent = File.ReadAllText(configPath);
                var jsonObj = JObject.Parse(jsonContent);
                
                // 确保DatabaseReplication节点存在
                if (jsonObj["DatabaseReplication"] == null)
                {
                    jsonObj["DatabaseReplication"] = new JObject();
                }
                
                // 更新连接字符串
                jsonObj["DatabaseReplication"]["LeaderConnectionString"] = LeaderConnectionTextBox.Text;
                jsonObj["DatabaseReplication"]["LeaderReadOnlyConnectionString"] = string.IsNullOrWhiteSpace(LeaderReadOnlyConnectionTextBox.Text) ? null : LeaderReadOnlyConnectionTextBox.Text;
                jsonObj["DatabaseReplication"]["FollowerConnectionString"] = FollowerConnectionTextBox.Text;
                
                // 保存文件
                var updatedJson = jsonObj.ToString(Formatting.Indented);
                File.WriteAllText(configPath, updatedJson);
                
                AddLog("数据库连接配置已保存到appsettings.json");
                MessageBox.Show("数据库连接配置已保存", "保存成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                AddLog($"保存配置失败: {ex.Message}");
                MessageBox.Show($"保存配置失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void ApplySettingsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 验证输入
                if (!int.TryParse(IntervalTextBox.Text, out int interval) || interval <= 0)
                {
                    MessageBox.Show("同步间隔必须是正整数", "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                
                if (!int.TryParse(BatchSizeTextBox.Text, out int batchSize) || batchSize <= 0)
                {
                    MessageBox.Show("批处理大小必须是正整数", "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                
                if (!int.TryParse(RetentionDaysTextBox.Text, out int retentionDays) || retentionDays <= 0)
                {
                    MessageBox.Show("数据保留天数必须是正整数", "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                
                if (!int.TryParse(CleanupIntervalTextBox.Text, out int cleanupInterval) || cleanupInterval <= 0)
                {
                    MessageBox.Show("清理间隔必须是正整数", "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                
                // 保存配置到appsettings.json
                var configPath = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");
                var jsonContent = File.ReadAllText(configPath);
                var jsonObj = JObject.Parse(jsonContent);
                
                // 确保DatabaseReplication节点存在
                if (jsonObj["DatabaseReplication"] == null)
                {
                    jsonObj["DatabaseReplication"] = new JObject();
                }
                
                // 更新同步参数
                jsonObj["DatabaseReplication"]["BatchSize"] = batchSize;
                jsonObj["DatabaseReplication"]["DataRetentionDays"] = retentionDays;
                jsonObj["DatabaseReplication"]["CleanupIntervalHours"] = cleanupInterval;
                jsonObj["DatabaseReplication"]["InitializeExistingData"] = InitializeExistingDataCheckBox.IsChecked ?? false;
                
                // 保存文件
                var updatedJson = jsonObj.ToString(Formatting.Indented);
                File.WriteAllText(configPath, updatedJson);
                
                AddLog($"同步参数已保存: 批大小={batchSize}, 保留={retentionDays}天, 清理间隔={cleanupInterval}小时, 初始化数据={InitializeExistingDataCheckBox.IsChecked}");
                MessageBox.Show("同步参数设置已保存", "保存成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                AddLog($"应用设置失败: {ex.Message}");
                MessageBox.Show($"应用设置失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void ClearLogButton_Click(object sender, RoutedEventArgs e)
        {
            lock (_logLines)
            {
                _logLines.Clear();
            }
            _logBuilder.Clear();
            LogTextBox.Text = "";
            AddLog("日志已清空");
        }
        
        private void ExportLogButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var saveFileDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*",
                    FileName = $"SyncLog_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
                };
                
                if (saveFileDialog.ShowDialog() == true)
                {
                    string logContent;
                    lock (_logLines)
                    {
                        logContent = string.Join(Environment.NewLine, _logLines);
                    }
                    System.IO.File.WriteAllText(saveFileDialog.FileName, logContent);
                    AddLog($"日志已导出到: {saveFileDialog.FileName}");
                    MessageBox.Show("日志导出成功", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                AddLog($"导出日志失败: {ex.Message}");
                MessageBox.Show($"导出日志失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void AddLog(string message)
        {
            var logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
            
            // 添加到内存日志列表
            lock (_logLines)
            {
                _logLines.Add(logEntry);
                
                // 如果超过最大行数，删除最上面的行
                if (_logLines.Count > MaxLogLines)
                {
                    int linesToRemove = _logLines.Count - MaxLogLines;
                    _logLines.RemoveRange(0, linesToRemove);
                }
            }
            
            // 只有启用实时日志时才更新UI，避免界面卡死
            if (_enableRealTimeLog && _isUILogProcessorRunning)
            {
                // 检查队列大小，防止内存溢出
                if (_uiLogQueue.Count < MaxUILogQueueSize)
                {
                    _uiLogQueue.Enqueue(logEntry);
                    _uiLogEvent.Set(); // 通知后台线程处理
                }
                else
                {
                    // 队列满时，丢弃最旧的日志条目
                    _uiLogQueue.TryDequeue(out _);
                    _uiLogQueue.Enqueue(logEntry);
                    _uiLogEvent.Set();
                }
            }
            // 移除启动阶段的同步模式，完全禁用实时日志显示
        }
        

        
        private void ShowDetailLogButton_Click(object sender, RoutedEventArgs e)
        {
            // 切换到详细日志选项卡
            if (MainTabControl != null)
            {
                // 查找同步日志选项卡并切换到它
                for (int i = 0; i < MainTabControl.Items.Count; i++)
                {
                    var tabItem = MainTabControl.Items[i] as TabItem;
                    if (tabItem != null && tabItem.Header.ToString() == "同步日志")
                    {
                        MainTabControl.SelectedIndex = i;
                        break;
                    }
                }
            }
        }
        

        
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        
        private void SetupConsoleRedirection()
        {
            _consoleRedirector = new ConsoleRedirector();
            _consoleRedirector.ConsoleOutput += (sender, output) =>
            {
                AddLog($"[控制台] {output}");
            };
            _consoleRedirector.Start();
        }
        
        private void StartUILogProcessor()
        {
            if (_isUILogProcessorRunning)
                return;
                
            _isUILogProcessorRunning = true;
            _uiLogProcessorTask = Task.Run(async () =>
            {
                await ProcessUILogQueue(_uiLogCancellationTokenSource.Token);
            });
        }
        
        private async Task ProcessUILogQueue(System.Threading.CancellationToken cancellationToken)
        {
            var logBatch = new List<string>();
            
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // 等待日志事件或取消信号
                    var waitHandles = new System.Threading.WaitHandle[] { _uiLogEvent, cancellationToken.WaitHandle };
                    var signalIndex = System.Threading.WaitHandle.WaitAny(waitHandles, 100); // 100ms超时
                    
                    if (cancellationToken.IsCancellationRequested)
                        break;
                    
                    // 批量收集日志条目
                    logBatch.Clear();
                    while (_uiLogQueue.TryDequeue(out string logEntry) && logBatch.Count < 50)
                    {
                        logBatch.Add(logEntry);
                    }
                    
                    // 如果有日志条目，批量更新UI
                    if (logBatch.Count > 0)
                    {
                        await Dispatcher.InvokeAsync(() =>
                        {
                            UpdateUIWithLogBatch(logBatch);
                        }, System.Windows.Threading.DispatcherPriority.Background);
                    }
                }
                catch (Exception ex)
                {
                    // 记录错误但继续处理
                    System.Diagnostics.Debug.WriteLine($"UI日志处理器错误: {ex.Message}");
                }
            }
        }
        
        private void UpdateUIWithLogBatch(List<string> logBatch)
        {
            try
            {
                if (logBatch == null || logBatch.Count == 0)
                    return;
                
                // 批量构建日志文本
                var batchText = string.Join(Environment.NewLine, logBatch) + Environment.NewLine;
                
                // 批量更新详细日志选项卡的日志显示框
                // 如果日志行数超过限制，重新构建整个文本
                if (_logLines.Count >= MaxLogLines - 100) // 提前一点重建，避免频繁操作
                {
                    lock (_logLines)
                    {
                        LogTextBox.Text = string.Join(Environment.NewLine, _logLines) + Environment.NewLine;
                    }
                }
                else
                {
                    LogTextBox.AppendText(batchText);
                }
                
                LogTextBox.ScrollToEnd();
                
                // 更新状态栏为最后一条日志的消息
                var lastLogEntry = logBatch[logBatch.Count - 1];
                var messageStart = lastLogEntry.IndexOf("] ");
                if (messageStart >= 0 && messageStart + 2 < lastLogEntry.Length)
                {
                    StatusBarText.Text = lastLogEntry.Substring(messageStart + 2);
                }
                else
                {
                    StatusBarText.Text = lastLogEntry;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"批量更新UI日志时出错: {ex.Message}");
            }
        }
        
        private void UpdateUIWithLogEntry(string logEntry)
        {
            try
            {
                // 更新详细日志选项卡的日志显示框
                // 如果日志行数超过限制，重新构建整个文本
                if (_logLines.Count >= MaxLogLines - 100) // 提前一点重建，避免频繁操作
                {
                    lock (_logLines)
                    {
                        LogTextBox.Text = string.Join(Environment.NewLine, _logLines) + Environment.NewLine;
                    }
                }
                else
                {
                    LogTextBox.AppendText(logEntry + Environment.NewLine);
                }
                
                LogTextBox.ScrollToEnd();
                
                // 从日志条目中提取消息部分更新状态栏
                var messageStart = logEntry.IndexOf("] ");
                if (messageStart >= 0 && messageStart + 2 < logEntry.Length)
                {
                    StatusBarText.Text = logEntry.Substring(messageStart + 2);
                }
                else
                {
                    StatusBarText.Text = logEntry;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"更新UI日志时出错: {ex.Message}");
            }
        }
        
        protected override void OnClosed(EventArgs e)
        {
            try
            {
                // 首先设置运行状态为false，防止定时器继续执行数据库操作
                _isRunning = false;
                
                // 停止UI日志处理器
                StopUILogProcessor();
                
                // 停止所有定时器
                _timeTimer?.Stop();
                _statusTimer?.Stop();
                
                // 停止并释放时间同步定时器
                if (_timeSyncTimer != null)
                {
                    _timeSyncTimer.Dispose();
                    _timeSyncTimer = null;
                }
                
                // 停止控制台重定向
                _consoleRedirector?.Stop();
                
                // 清理缓存以释放内存
                _cachedLeaderToFollowerStatus?.Clear();
                _cachedFollowerToLeaderStatus?.Clear();
                _cachedLeaderToFollowerStatus = null;
                _cachedFollowerToLeaderStatus = null;
                
                // 停止复制器
                if (_replicator != null)
                {
                    try
                    {
                        // 使用Task.Run避免在UI线程上执行可能阻塞的操作
                        var stopTask = Task.Run(() => 
                        {
                            _replicator.StopReplication();
                            _replicator.Dispose();
                        });
                        
                        // 等待最多2秒，避免无限等待
                        if (!stopTask.Wait(2000))
                        {
                            System.Diagnostics.Debug.WriteLine("停止复制器超时，强制关闭");
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"停止复制器时出错: {ex.Message}");
                    }
                    finally
                    {
                        _replicator = null;
                    }
                }
            }
            catch (Exception ex)
            {
                // 记录错误但不阻止关闭
                System.Diagnostics.Debug.WriteLine($"关闭时清理资源失败: {ex.Message}");
            }
            finally
            {
                base.OnClosed(e);
            }
        }
        
        private void StopUILogProcessor()
        {
            try
            {
                if (_isUILogProcessorRunning)
                {
                    _isUILogProcessorRunning = false;
                    _uiLogCancellationTokenSource?.Cancel();
                    _uiLogEvent?.Set(); // 唤醒处理线程以便退出
                    
                    // 等待处理器任务完成
                    if (_uiLogProcessorTask != null && !_uiLogProcessorTask.IsCompleted)
                    {
                        if (!_uiLogProcessorTask.Wait(1000)) // 等待最多1秒
                        {
                            System.Diagnostics.Debug.WriteLine("UI日志处理器停止超时");
                        }
                    }
                }
                
                // 释放资源
                _uiLogEvent?.Dispose();
                _uiLogCancellationTokenSource?.Dispose();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"停止UI日志处理器时出错: {ex.Message}");
            }
        }
    }
    
    public class SyncStatusItem : INotifyPropertyChanged
    {
        private string _tableName;
        private int _lastSyncedId;
        private DateTime _lastSyncTime;
        private int _pendingCount;
        private string _status;
        
        public string TableName
        {
            get => _tableName;
            set { _tableName = value; OnPropertyChanged(nameof(TableName)); }
        }
        
        public int LastSyncedId
        {
            get => _lastSyncedId;
            set { _lastSyncedId = value; OnPropertyChanged(nameof(LastSyncedId)); }
        }
        
        public DateTime LastSyncTime
        {
            get => _lastSyncTime;
            set { _lastSyncTime = value; OnPropertyChanged(nameof(LastSyncTime)); }
        }
        
        public int PendingCount
        {
            get => _pendingCount;
            set { _pendingCount = value; OnPropertyChanged(nameof(PendingCount)); }
        }
        
        public string Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(nameof(Status)); }
        }
        
        public event PropertyChangedEventHandler PropertyChanged;
        
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
    
    public class TableConfigViewModel : INotifyPropertyChanged
    {
        private string _tableName;
        private string _entityTypeName;
        private string _primaryKey;
        private string _replicationDirection;
        private string _conflictStrategy;
        private int _replicationIntervalSeconds;
        private bool _enabled;
        private bool _initializeExistingData;
        private string _conflictResolutionPriorityFieldsText;
        private bool _isExistingConfig;
        private bool _canManualSync = true;
        private bool _isManualSyncing = false;

        public string TableName
        {
            get => _tableName;
            set { _tableName = value; OnPropertyChanged(); }
        }

        public string EntityTypeName
        {
            get => _entityTypeName;
            set { _entityTypeName = value; OnPropertyChanged(); }
        }

        public string PrimaryKey
        {
            get => _primaryKey;
            set { _primaryKey = value; OnPropertyChanged(); }
        }

        public string ReplicationDirection
        {
            get => _replicationDirection;
            set { _replicationDirection = value; OnPropertyChanged(); }
        }

        public string ConflictStrategy
        {
            get => _conflictStrategy;
            set { _conflictStrategy = value; OnPropertyChanged(); }
        }

        public int ReplicationIntervalSeconds
        {
            get => _replicationIntervalSeconds;
            set { _replicationIntervalSeconds = value; OnPropertyChanged(); }
        }

        public bool Enabled
        {
            get => _enabled;
            set { _enabled = value; OnPropertyChanged(); }
        }

        public bool InitializeExistingData
        {
            get => _initializeExistingData;
            set { _initializeExistingData = value; OnPropertyChanged(); }
        }

        public string ConflictResolutionPriorityFieldsText
        {
            get => _conflictResolutionPriorityFieldsText;
            set { _conflictResolutionPriorityFieldsText = value; OnPropertyChanged(); }
        }

        public bool IsExistingConfig
        {
            get => _isExistingConfig;
            set { _isExistingConfig = value; OnPropertyChanged(); }
        }

        public bool CanManualSync
        {
            get => _canManualSync && _enabled && !_isManualSyncing;
            set { _canManualSync = value; OnPropertyChanged(); }
        }

        public bool IsManualSyncing
        {
            get => _isManualSyncing;
            set { _isManualSyncing = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanManualSync)); }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}