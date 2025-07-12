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
        private const int MaxLogLines = 10000;
        private ConsoleRedirector _consoleRedirector;
        
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
                
                using (var leaderDbContext = new LeaderDbContext(
                    new DbContextOptionsBuilder<LeaderDbContext>()
                        .UseMySql(ServerVersion.AutoDetect(_config.LeaderConnectionString))
                        .Options,
                    _config.LeaderConnectionString,
                    _tableConfigs))
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
                            PendingCount = await GetPendingCountFromReplicationLogs(tableConfig, hasProgress ? progress.LastSyncedId : 0),
                            Status = _isRunning ? "运行中" : "已停止"
                        };
                        
                        // 根据复制方向添加到相应的集合
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
            
            // 状态刷新定时器
            _statusTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5)
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
                
                LeaderConnectionTextBox.Text = _config.LeaderConnectionString;
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
                using (var leaderContext = new LeaderDbContext(
                    new DbContextOptionsBuilder<LeaderDbContext>()
                        .UseMySql(ServerVersion.AutoDetect(_config.LeaderConnectionString))
                        .Options,
                    _config.LeaderConnectionString,
                    _tableConfigs))
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
        
        private async Task RefreshStatusSilently()
        {
            try
            {
                // 检查是否正在运行，如果不是则快速退出
                if (!_isRunning || _config == null)
                {
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
                
                // 更新现有项目而不是清空重建
                UpdateStatusCollections(LeaderToFollowerStatus, newLeaderToFollowerStatus);
                UpdateStatusCollections(FollowerToLeaderStatus, newFollowerToLeaderStatus);
            }
            catch (Exception ex)
            {
                // 静默刷新失败时不记录日志，避免日志过多
            }
        }
        
        private async Task<int> GetPendingCountFromReplicationLogs(TableConfig tableConfig, int lastSyncedId)
        {
            try
            {
                // 检查运行状态，如果程序正在关闭则快速返回
                if (!_isRunning)
                {
                    return 0;
                }
                
                using (var leaderContext = new LeaderDbContext(
                    new DbContextOptionsBuilder<LeaderDbContext>()
                        .UseMySql(ServerVersion.AutoDetect(_config.LeaderConnectionString))
                        .Options,
                    _config.LeaderConnectionString,
                    _tableConfigs))
                {
                    // 再次检查运行状态
                    if (!_isRunning)
                    {
                        return 0;
                    }
                    
                    using (var connection = leaderContext.Database.GetDbConnection())
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
        

        
        private void LoadTimeSyncConfiguration()
        {
            try
            {
                var configuration = new ConfigurationBuilder()
                    .SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                    .Build();
                
                var timeSyncSection = configuration.GetSection("TimeSynchronization");
                if (timeSyncSection.Exists())
                {
                    TimeSyncEnabledCheckBox.IsChecked = timeSyncSection.GetValue<bool>("Enabled");
                    TimeSyncIntervalTextBox.Text = timeSyncSection.GetValue<int>("SyncIntervalMinutes").ToString();
                    RequireElevatedPrivilegesCheckBox.IsChecked = timeSyncSection.GetValue<bool>("RequireElevatedPrivileges");
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
            
            Dispatcher.Invoke(() =>
            {
                // 更新主界面底部的日志显示框
                MainLogTextBox.AppendText(logEntry + Environment.NewLine);
                MainLogTextBox.ScrollToEnd();
                
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
                StatusBarText.Text = message;
            });
        }
        
        private void ClearMainLogButton_Click(object sender, RoutedEventArgs e)
        {
            MainLogTextBox.Clear();
            AddLog("主界面日志已清空");
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
        
        protected override void OnClosed(EventArgs e)
        {
            try
            {
                // 首先设置运行状态为false，防止定时器继续执行数据库操作
                _isRunning = false;
                
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

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}