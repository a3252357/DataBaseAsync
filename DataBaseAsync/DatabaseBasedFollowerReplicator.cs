using Coldairarrow.bgmj.Entity;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DataBaseAsync;
using Microsoft.EntityFrameworkCore.Storage;

namespace DatabaseReplication.Follower
{
    public class DatabaseBasedFollowerReplicator : IDisposable
    {
        private readonly string _followerServerId;
        private readonly string _followerConnectionString;
        private readonly string _leaderConnectionString;
        private readonly string _leaderReadOnlyConnectionString;
        private readonly List<TableConfig> _tableConfigs;
        private readonly Dictionary<string, Timer> _timers = new Dictionary<string, Timer>();
        private readonly Dictionary<string, bool> _executionFlags = new Dictionary<string, bool>();
        private readonly object _executionLock = new object();
        private readonly int _batchSize;
        private readonly int _dataRetentionDays;
        private readonly int _cleanupIntervalHours;
        private bool _isRunning = false;
        private bool _isInitializationMode = false;
        private Timer _cleanupTimer;
        private bool _isCleanupExecuting = false;
        private readonly Logger _logger;

        public DatabaseBasedFollowerReplicator(
            string followerServerId,
            string followerConnectionString,
            string leaderConnectionString,
            string leaderReadOnlyConnectionString,
            List<TableConfig> tableConfigs,
            int batchSize = 1000,
            int dataRetentionDays = 30,
            int cleanupIntervalHours = 24)
        {
            _followerServerId = followerServerId;
            _followerConnectionString = followerConnectionString;
            _leaderConnectionString = leaderConnectionString;
            // 如果没有配置只读连接字符串，则使用主库连接字符串
            _leaderReadOnlyConnectionString = string.IsNullOrWhiteSpace(leaderReadOnlyConnectionString) 
                ? leaderConnectionString 
                : leaderReadOnlyConnectionString;
            _tableConfigs = tableConfigs;
            _batchSize = batchSize;
            _dataRetentionDays = dataRetentionDays;
            _cleanupIntervalHours = cleanupIntervalHours;
            _logger = Logger.Instance;
            CreateReplicationStatusTable();
        }

        // 启动复制服务
        public async void StartReplication()
        {
            if (_isRunning) return;

            _isRunning = true;

            // 首先执行表结构同步
            await PerformInitialSchemaSync();

            foreach (var tableConfig in _tableConfigs.Where(t => t.Enabled))
            {
                // 启动主库到从库的同步计时器
                if (tableConfig.ReplicationDirection == ReplicationDirection.Bidirectional ||
                    tableConfig.ReplicationDirection == ReplicationDirection.LeaderToFollower)
                {
                    var timerKey = $"LeaderToFollower_{tableConfig.TableName}";
                    _executionFlags[timerKey] = false;
                    
                    var timer = new Timer(
                        async _ => await ExecuteWithFlag(timerKey, () => PullAndApplyChanges(tableConfig)),
                        null,
                        TimeSpan.Zero,
                        TimeSpan.FromSeconds(tableConfig.ReplicationIntervalSeconds));

                    _timers[timerKey] = timer;
                }

                // 启动从库到主库的同步计时器（双向复制）
                if (tableConfig.ReplicationDirection == ReplicationDirection.Bidirectional ||
                    tableConfig.ReplicationDirection == ReplicationDirection.FollowerToLeader)
                {
                    var timerKey = $"FollowerToLeader_{tableConfig.TableName}";
                    _executionFlags[timerKey] = false;
                    
                    var timer = new Timer(
                        async _ => await ExecuteWithFlag(timerKey, () => PushFollowerChangesToLeader(tableConfig)),
                        null,
                        TimeSpan.Zero,
                        TimeSpan.FromSeconds(tableConfig.ReplicationIntervalSeconds));

                    _timers[timerKey] = timer;
                }

                // 启动表结构定期同步计时器
                if (tableConfig.SchemaSync == SchemaSyncStrategy.Periodic || 
                    tableConfig.SchemaSync == SchemaSyncStrategy.OnStartupAndPeriodic)
                {
                    var schemaTimerKey = $"SchemaSync_{tableConfig.TableName}";
                    _executionFlags[schemaTimerKey] = false;
                    
                    var schemaTimer = new Timer(
                        async _ => await ExecuteWithFlag(schemaTimerKey, () => SyncTableSchema(tableConfig)),
                        null,
                        TimeSpan.FromMinutes(tableConfig.SchemaSyncIntervalMinutes),
                        TimeSpan.FromMinutes(tableConfig.SchemaSyncIntervalMinutes));

                    _timers[schemaTimerKey] = schemaTimer;
                }
            }

            // 启动数据清理定时器
            _cleanupTimer = new Timer(
                async _ => await ExecuteCleanupWithFlag(),
                null,
                TimeSpan.Zero, // 立即执行一次
                TimeSpan.FromHours(_cleanupIntervalHours)); // 定期执行

            _logger.Info($"从库 {_followerServerId} 复制服务已启动");
            _logger.Info($"数据清理任务已启动，保留 {_dataRetentionDays} 天数据，每 {_cleanupIntervalHours} 小时执行一次清理");
        }

        // 停止复制服务
        public void StopReplication()
        {
            if (!_isRunning) return;

            _isRunning = false;

            foreach (var timer in _timers.Values)
            {
                timer.Dispose();
            }

            _timers.Clear();
            
            // 清理执行状态标志
            lock (_executionLock)
            {
                _executionFlags.Clear();
            }
            
            // 停止清理定时器
            _cleanupTimer?.Dispose();
            _cleanupTimer = null;
            _isCleanupExecuting = false;
            
            _logger.Info($"从库 {_followerServerId} 复制服务已停止");
        }

        // 初始化旧数据同步
        public async Task InitializeExistingData(bool parallel = true, int maxConcurrency = 3)
        {
            _logger.Info("开始初始化旧数据同步...");
            var startTime = DateTime.Now;

            var enabledTables = _tableConfigs.Where(t => t.Enabled && t.InitializeExistingData).ToList();
            
            if (!enabledTables.Any())
            {
                _logger.Info("没有需要初始化的表");
                return;
            }

            _logger.Info($"共有 {enabledTables.Count} 个表需要初始化，并发度: {(parallel ? maxConcurrency : 1)}");

            if (parallel)
            {
                // 使用 SemaphoreSlim 控制并发数量
                using var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
                var tasks = new List<Task>();
                var completedTables = 0;
                var totalTables = enabledTables.Count;
                var lockObject = new object();

                foreach (var tableConfig in enabledTables)
                {
                    tasks.Add(Task.Run(async () =>
                    {
                        await semaphore.WaitAsync();
                        try
                        {
                            await InitializeTableDataWithRetry(tableConfig);
                            
                            lock (lockObject)
                            {
                                completedTables++;
                                var progress = (double)completedTables / totalTables * 100;
                                _logger.Info($"进度: {completedTables}/{totalTables} ({progress:F1}%) - 表 {tableConfig.TableName} 初始化完成");
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.Error($"表 {tableConfig.TableName} 初始化失败: {ex.Message}");
                            throw;
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }));
                }

                // 等待所有任务完成
                try
                {
                    await Task.WhenAll(tasks);
                    _logger.Info("所有表初始化成功完成");
                }
                catch (Exception ex)
                {
                    _logger.Error($"部分表初始化失败: {ex.Message}");
                    // 检查哪些任务失败了
                    for (int i = 0; i < tasks.Count; i++)
                    {
                        if (tasks[i].IsFaulted)
                        {
                            _logger.Error($"表 {enabledTables[i].TableName} 初始化失败: {tasks[i].Exception?.GetBaseException().Message}");
                        }
                    }
                    throw;
                }
            }
            else
            {
                // 串行执行
                for (int i = 0; i < enabledTables.Count; i++)
                {
                    var tableConfig = enabledTables[i];
                    try
                    {
                        await InitializeTableDataWithRetry(tableConfig);
                        var progress = (double)(i + 1) / enabledTables.Count * 100;
                        _logger.Info($"进度: {i + 1}/{enabledTables.Count} ({progress:F1}%) - 表 {tableConfig.TableName} 初始化完成");
                    }
                    catch (Exception ex)
                    {
                        _logger.Error($"表 {tableConfig.TableName} 初始化失败: {ex.Message}");
                        throw;
                    }
                }
            }

            var duration = DateTime.Now - startTime;
            _logger.Info($"旧数据初始化同步完成，总耗时: {duration.TotalMinutes:F2} 分钟");
        }

        // 带重试机制的表数据初始化
        private async Task InitializeTableDataWithRetry(TableConfig tableConfig, int maxRetries = 3)
        {
            var retryCount = 0;
            var baseDelay = TimeSpan.FromSeconds(5);

            while (retryCount <= maxRetries)
            {
                try
                {
                    await InitializeTableData(tableConfig);
                    return; // 成功则退出
                }
                catch (Exception ex) when (retryCount < maxRetries)
                {
                    retryCount++;
                    var delay = TimeSpan.FromMilliseconds(baseDelay.TotalMilliseconds * Math.Pow(2, retryCount - 1));
                    _logger.Warning($"表 {tableConfig.TableName} 初始化失败 (第 {retryCount} 次重试)，{delay.TotalSeconds} 秒后重试: {ex.Message}");
                    await Task.Delay(delay);
                }
                catch (Exception ex)
                {
                    _logger.Error($"表 {tableConfig.TableName} 初始化最终失败，已重试 {maxRetries} 次: {ex.Message}");
                    throw;
                }
            }
        }

        // 初始化单个表的旧数据
        private async Task InitializeTableData(TableConfig tableConfig)
        {
            _logger.Info($"开始初始化表 {tableConfig.TableName} 的旧数据...");

            await ClearTableData(tableConfig);
            await CopyTableData(tableConfig);

            _logger.Info($"表 {tableConfig.TableName} 的旧数据初始化完成");
        }
        // 创建复制状态表
        private void CreateReplicationStatusTable()
        {
            using (var le = CreateLeaderDbContext())
            {
                string tableName = $"replication_status_{_followerServerId}";

                string createTableSql = $@"
                CREATE TABLE IF NOT EXISTS `{tableName}` (
                  `log_entry_id` int NOT NULL,
                  `is_synced` tinyint(1) NOT NULL DEFAULT '0',
                  `sync_time` datetime DEFAULT NULL,
                  `error_message` varchar(500) DEFAULT NULL,
                  PRIMARY KEY (`log_entry_id`),
                  FOREIGN KEY (`log_entry_id`) REFERENCES `replication_logs` (`id`)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;";

                le.Database.ExecuteSqlRaw(createTableSql);
            }
        }

        // 清空从库表数据
        private async Task ClearTableData(TableConfig tableConfig)
        {
            await ExecuteWithRetry(async () =>
            {
                _logger.Info($"清空从库 {_followerConnectionString} 中表 {tableConfig.TableName} 的数据...");

                using (var connection = new MySqlConnection(_followerConnectionString))
                {
                    await connection.OpenAsync();

                    // 禁用外键约束以允许清空表
                    await using (var disableConstraintsCmd = new MySqlCommand("SET FOREIGN_KEY_CHECKS = 0", connection))
                    {
                        await disableConstraintsCmd.ExecuteNonQueryAsync();
                    }

                    // 清空表数据
                    await using (var truncateCmd = new MySqlCommand($"TRUNCATE TABLE {tableConfig.TableName}", connection))
                    {
                        await truncateCmd.ExecuteNonQueryAsync();
                    }

                    // 重新启用外键约束
                    await using (var enableConstraintsCmd = new MySqlCommand("SET FOREIGN_KEY_CHECKS = 1", connection))
                    {
                        await enableConstraintsCmd.ExecuteNonQueryAsync();
                    }
                }
            },3,1000, $"清空表数据 - 表 {tableConfig.TableName}");
        }

        // 复制表数据
        private async Task CopyTableData(TableConfig tableConfig, int maxConcurrency = 4)
        {
            _logger.Info($"从 {_leaderReadOnlyConnectionString} 复制表 {tableConfig.TableName} 数据到 {_followerConnectionString}...");

            using (var sourceConnection = new MySqlConnection(_leaderReadOnlyConnectionString))
            {
                await sourceConnection.OpenAsync();

                // 获取表结构信息
                var columns = new List<string>();
                await using (var schemaCmd = new MySqlCommand(
                    $"SELECT COLUMN_NAME FROM information_schema.COLUMNS WHERE TABLE_NAME = @TableName AND TABLE_SCHEMA = DATABASE()",
                    sourceConnection))
                {
                    schemaCmd.Parameters.AddWithValue("@TableName", tableConfig.TableName);

                    await using (var reader = await schemaCmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            columns.Add(reader.GetString(0));
                        }
                    }
                }

                var columnList = "`" + string.Join("`,`", columns ) + "`";

                // 获取总行数
                int totalRows = 0;
                await using (var countCmd = new MySqlCommand($"SELECT COUNT(*) FROM {tableConfig.TableName}", sourceConnection))
                {
                    totalRows = Convert.ToInt32(await countCmd.ExecuteScalarAsync());
                }

                _logger.Info($"表 {tableConfig.TableName} 共有 {totalRows} 条记录需要复制");

                int batchSize = 5000;
                var batches = new List<(int offset, int size, int batchNumber)>();
                
                // 计算所有批次
                for (int offset = 0; offset < totalRows; offset += batchSize)
                {
                    int currentBatchSize = Math.Min(batchSize, totalRows - offset);
                    batches.Add((offset, currentBatchSize, batches.Count + 1));
                }

                _logger.Info($"表 {tableConfig.TableName} 将分为 {batches.Count} 个批次进行并行复制，并发度: {maxConcurrency}");

                // 使用 SemaphoreSlim 控制并发数量
                using var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
                var tasks = new List<Task>();
                var completedBatches = 0;
                var lockObject = new object();
                var totalCopiedRows = 0;

                foreach (var (offset, size, batchNumber) in batches)
                {
                    tasks.Add(Task.Run(async () =>
                    {
                        await semaphore.WaitAsync();
                        try
                        {
                            var copiedRows = await CopyBatchData(tableConfig, columnList, columns, offset, size, batchNumber);
                            
                            lock (lockObject)
                            {
                                completedBatches++;
                                totalCopiedRows += copiedRows;
                                var progress = (double)completedBatches / batches.Count * 100;
                                _logger.Info($"批次进度: {completedBatches}/{batches.Count} ({progress:F1}%) - 批次 {batchNumber} 完成，复制了 {copiedRows} 条记录");
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.Error($"批次 {batchNumber} 复制失败: {ex.Message}");
                            throw;
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }));
                }

                // 等待所有批次完成
                try
                {
                    await Task.WhenAll(tasks);
                    _logger.Info($"表 {tableConfig.TableName} 所有批次复制完成，共复制 {totalCopiedRows} 条记录");
                }
                catch (Exception ex)
                {
                    _logger.Error($"表 {tableConfig.TableName} 部分批次复制失败: {ex.Message}");
                    throw;
                }
            }
        }

        // 通用重试方法
        private async Task<T> ExecuteWithRetry<T>(Func<Task<T>> operation, int maxRetries = 3, int delayMs = 1000, string operationName = "操作")
        {
            Exception lastException = null;
            
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    return await operation();
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    _logger.Warning($"{operationName} 第 {attempt} 次尝试失败: {ex.Message}");
                    
                    if (attempt == maxRetries)
                    {
                        _logger.Error($"{operationName} 达到最大重试次数 ({maxRetries})，操作失败");
                        throw;
                    }
                    
                    // 指数退避策略
                    int delay = delayMs * (int)Math.Pow(2, attempt - 1);
                    _logger.Info($"等待 {delay}ms 后进行第 {attempt + 1} 次重试...");
                    await Task.Delay(delay);
                }
            }
            
            throw lastException;
        }
        // 处理单个变更的重试逻辑（事务外移）
        private async Task<bool> ProcessSingleChangeWithRetry(
            FollowerDbContext followerContext,
            TableConfig tableConfig,
            ReplicationLogEntry change,
            int maxRetries = 3)
        {
            Exception lastException = null;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    if (tableConfig.SyncMode == TableSyncMode.Entity)
                    {
                        // Entity模式：使用EF Core实体处理
                        var entity = await GetEntityFromLeader(tableConfig, change.Data);

                        if (entity != null)
                        {
                            // 应用变更到从库
                            await ApplyEntityChangeToFollower(followerContext, tableConfig, change, entity);
                        }
                        else if (change.OperationType == ReplicationOperation.Delete)
                        {
                            // 对于删除操作，如果实体不存在，直接执行删除
                            await DeleteEntityInFollower(followerContext, tableConfig, change.Data);
                        }
                        else
                        {
                            throw new Exception("主库数据不存在");
                        }
                    }
                    else
                    {
                         // 从主库获取原始数据
                        var rawData = await GetRawDataFromLeader(tableConfig, change.Data);
                        // NoEntity模式：使用原始SQL处理
                        if (rawData != null && rawData.Count > 0)
                        {
                                // 应用变更到从库
                                await ApplyRawDataChangeToFollower(followerContext, tableConfig, change, rawData);
                        }
                        else  if (change.OperationType == ReplicationOperation.Delete)
                        {
                            // 对于删除操作，直接执行删除
                            await DeleteRawDataInFollower(followerContext, tableConfig, change.Data);
                        }
                        else
                        {
                            
                            throw new Exception("主库数据不存在");
                        }
                    }

                    // 每条数据调用一次SaveChanges
                    await followerContext.SaveChangesAsync();

                    _logger.Info($"表 {tableConfig.TableName} 变更 ID {change.Id} 处理成功");
                    return true; // 成功处理
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    _logger.Warning($"表 {tableConfig.TableName} 变更 ID {change.Id} 第 {attempt} 次尝试失败: {ex.Message}");

                    if (attempt < maxRetries)
                    {
                        // 指数退避策略
                        int delay = 1000 * (int)Math.Pow(2, attempt - 1);
                        _logger.Info($"等待 {delay}ms 后进行第 {attempt + 1} 次重试...");
                        await Task.Delay(delay);
                    }
                }
            }

            // 重试3次后仍然失败，记录失败数据
            await LogFailedChange(change, tableConfig.TableName, lastException?.Message);
            _logger.Error($"表 {tableConfig.TableName} 变更 ID {change.Id} 重试 {maxRetries} 次后仍然失败: {lastException?.Message}");
            return false; // 处理失败
        }

        // 记录失败的变更到失败日志表
        private async Task LogFailedChanges(List<ReplicationLogEntry> failedChanges, string tableName, string additionalError = null)
        {
            try
            {
                using (var leaderContext = CreateLeaderDbContext())
                {
                    foreach (var change in failedChanges)
                    {
                        var failureLog = new ReplicationFailureLog
                        {
                            Id = change.Id,
                            TableName = tableName,
                            OperationType = change.OperationType.ToString(),
                            RecordId = change.Data,
                            ErrorMessage = additionalError ?? "重试3次后仍然失败",
                            FailureTime = DateTime.Now,
                            RetryCount = 3,
                            FollowerServerId = _followerServerId
                        };

                        leaderContext.ReplicationFailureLogs.Add(failureLog);
                    }

                    await leaderContext.SaveChangesAsync();
                    _logger.Info($"已记录 {failedChanges.Count} 条失败变更到失败日志表");
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"记录失败日志时出错: {ex.Message}");
            }
        }

        // 记录单个失败的变更到失败日志表
        private async Task LogFailedChange(ReplicationLogEntry change, string tableName, string errorMessage = null)
        {
            try
            {
                using (var leaderContext = CreateLeaderDbContext())
                {
                    var failureLog = new ReplicationFailureLog
                    {
                        Id = change.Id,
                        TableName = tableName,
                        OperationType = change.OperationType.ToString(),
                        RecordId = change.Data,
                        ErrorMessage = errorMessage ?? "重试3次后仍然失败",
                        FailureTime = DateTime.Now,
                        RetryCount = 3,
                        FollowerServerId = _followerServerId
                    };

                    leaderContext.ReplicationFailureLogs.Add(failureLog);
                    await leaderContext.SaveChangesAsync();
                    _logger.Info($"已记录失败变更到失败日志表: 表 {tableName} 变更 ID {change.Id}");
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"记录失败日志时出错: {ex.Message}");
            }
        }

        // 通用重试方法
        private async Task ExecuteWithRetry(Func<Task> operation, int maxRetries = 3, int delayMs = 1000, string operationName = "操作")
        {
            Exception lastException = null;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    await operation();
                    break;
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    _logger.Warning($"{operationName} 第 {attempt} 次尝试失败: {ex.Message}");

                    if (attempt == maxRetries)
                    {
                        _logger.Error($"{operationName} 达到最大重试次数 ({maxRetries})，操作失败");
                        throw;
                    }

                    // 指数退避策略
                    int delay = delayMs * (int)Math.Pow(2, attempt - 1);
                    _logger.Info($"等待 {delay}ms 后进行第 {attempt + 1} 次重试...");
                    await Task.Delay(delay);
                }
            }
            if (lastException != null)
            {
                throw lastException;
            }
        }
        // 复制单个批次的数据（带重试机制）
        private async Task<int> CopyBatchData(TableConfig tableConfig, string columnList, List<string> columns, int offset, int batchSize, int batchNumber)
        {
            return await ExecuteWithRetry(async () =>
            {
                using (var sourceConnection = new MySqlConnection(_leaderConnectionString))
                {
                    await sourceConnection.OpenAsync();

                    await using (var selectCmd = new MySqlCommand(
                        $"SELECT {columnList} FROM {tableConfig.TableName} ORDER BY {tableConfig.PrimaryKey} LIMIT {batchSize} OFFSET {offset}",
                        sourceConnection))
                    {
                        await using (var reader = await selectCmd.ExecuteReaderAsync())
                        {
                            // 检查是否有数据
                            if (reader.HasRows)
                            {
                                using (var destinationConnection = new MySqlConnection(_followerConnectionString))
                                {
                                    await destinationConnection.OpenAsync();

                                    // 设置会话变量防止触发器在批量加载时触发
                                    await using (var setCmd = new MySqlCommand("SET @is_replicating = 1", destinationConnection))
                                    {
                                        await setCmd.ExecuteNonQueryAsync();
                                    }

                                    try
                                    {
                                        /// <summary>
                                        /// 使用 MySqlBulkLoader 进行批量插入
                                        /// 配置正确的NULL值处理，避免DateTime?类型null转换为0000-00-00 00:00:00
                                        /// </summary>
                                        var bulkLoader = new MySqlBulkLoader(destinationConnection)
                                        {
                                            TableName = tableConfig.TableName,
                                            CharacterSet = "utf8mb4",
                                            NumberOfLinesToSkip = 0,
                                            Timeout = 300, // 5分钟超时
                                            FieldTerminator = ",",
                                            LineTerminator = Environment.NewLine,
                                            FieldQuotationCharacter = '"',
                                            FieldQuotationOptional = true, // 修改为true，只有包含特殊字符的字段才使用引号
                                            // 配置NULL值处理 - 使用\N作为NULL标识符
                                            EscapeCharacter = '\\',
                                            Local = true
                                        };
                                        
                                        // MySQL LOAD DATA默认将\N识别为NULL值，无需额外配置

                                        // 添加列映射
                                        foreach (var column in columns)
                                        {
                                            bulkLoader.Columns.Add("`"+column+"`");
                                        }
                                        // 将 DataReader 转换为 CSV 格式并加载
                                        bulkLoader.SourceStream = new DataReaderStream(reader);
                                        var rowsLoaded = await bulkLoader.LoadAsync();
                                        return (int)rowsLoaded;
                                    }
                                    finally
                                    {
                                        // 重置会话变量
                                        await using (var resetCmd = new MySqlCommand("SET @is_replicating = 0", destinationConnection))
                                        {
                                            await resetCmd.ExecuteNonQueryAsync();
                                        }
                                    }
                                }
                            }
                            return 0;
                        }
                    }
                }
            }, maxRetries: 3, delayMs: 1000, operationName: $"批次 {batchNumber} 数据复制");
        }

        // 辅助类：将 DataReader 转换为流供 MySqlBulkLoader 使用
        private class DataReaderStream : Stream
        {
            private readonly MySqlDataReader _reader;
            private readonly List<string> _allData = new List<string>();
            private byte[] _allDataBytes;
            private int _position = 0;
            private bool _isInitialized = false;

            public DataReaderStream(MySqlDataReader reader)
            {
                _reader = reader;
            }

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

            public override void Flush() => throw new NotSupportedException();

            public override int Read(byte[] buffer, int offset, int count)
            {
                if (!_isInitialized)
                {
                    InitializeData();
                    _isInitialized = true;
                }

                if (_position >= _allDataBytes.Length)
                    return 0;

                int bytesToRead = Math.Min(count, _allDataBytes.Length - _position);
                Array.Copy(_allDataBytes, _position, buffer, offset, bytesToRead);
                _position += bytesToRead;

                return bytesToRead;
            }

            /// <summary>
            /// 初始化数据，将DataReader转换为CSV格式
            /// 特别处理DateTime?类型的null值，避免转换为0000-00-00 00:00:00
            /// </summary>
            private void InitializeData()
            {
                var csvLines = new List<string>();
                
                // 读取所有数据行
                while (_reader.Read())
                {
                    var row = new List<string>();
                    for (int i = 0; i < _reader.FieldCount; i++)
                    {
                        object value = _reader[i];
                        string valueStr;
                        
                        if (value == DBNull.Value || value == null)
                        {
                            // 对于null值，使用\N表示MySQL的NULL值
                            valueStr = "\\N";
                        }
                        else if (value is bool boolValue)
                        {
                            // 对于bool类型，转换为0或1
                            valueStr = boolValue ? "1" : "0";
                        }
                        else if (value is ulong ulongValue)
                        {
                            // 对于BIT(1)类型（MySQL读取为ulong），转换为0或1
                            valueStr = ulongValue == 0 ? "\x00" : ulongValue == 1? "\x01": EscapeCsvValue(value.ToString());
                        }
                        else
                        {
                            valueStr = EscapeCsvValue(value.ToString());
                        }
                        
                        row.Add(valueStr);
                    }
                    csvLines.Add(string.Join(",", row));
                }

                // 将所有行合并为一个字符串
                string allCsvData = string.Join(Environment.NewLine, csvLines);
                if (csvLines.Count > 0)
                {
                    allCsvData += Environment.NewLine; // 确保最后一行也有换行符
                }
                
                _allDataBytes = System.Text.Encoding.UTF8.GetBytes(allCsvData);
            }

            private string EscapeCsvValue(string value)
            {
                if(value==null){
                    return "\\N";
                }
                if (string.IsNullOrEmpty(value))
                    return "";

                // 如果值包含逗号、引号或换行符，用引号括起来并转义引号
                if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
                {
                    return "\"" + value.Replace("\"", "\"\"") + "\"";
                }

                return value;
            }

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }

        // 从主库拉取变更并应用到从库（带重试机制）
        private async Task PullAndApplyChanges(TableConfig tableConfig)
        {
            await ExecuteWithRetry(async () =>
            {
                using (var leaderReadContext = CreateLeaderDbContextForRead())
                using (var followerContext = CreateFollowerDbContext())
                {
                    // 获取主库待同步的变更
                    var pendingChanges = await GetPendingChangesFromLeader(leaderReadContext, tableConfig);

                    if (pendingChanges.Any())
                    {
                        _logger.Info($"从主库拉取了 {pendingChanges.Count} 条表 {tableConfig.TableName} 的变更");

                        // 应用变更到从库
                        await ApplyChangesToFollower(followerContext, tableConfig, pendingChanges);

                        // 标记变更为已处理（需要写操作，使用写上下文）
                        using (var leaderWriteContext = CreateLeaderDbContext())
                        {
                            await MarkChangesAsProcessed(leaderWriteContext, pendingChanges);
                        }

                        _logger.Info($"成功将 {pendingChanges.Count} 条表 {tableConfig.TableName} 的变更应用到从库");
                    }
                }
                return Task.CompletedTask;
            }, maxRetries: 3, delayMs: 2000, operationName: $"表 {tableConfig.TableName} 变更同步");
        }

        // 获取主库待同步的变更（支持分批处理）
        private async Task<List<ReplicationLogEntry>> GetPendingChangesFromLeader(
            LeaderDbContext leaderContext,
            TableConfig tableConfig)
        {
            // 获取当前表的同步进度
            var lastSyncedId = await GetLastSyncedId(leaderContext, tableConfig.TableName);
            
            string statusTableName = $"replication_status_{_followerServerId}";

            var sql = $@"
                SELECT l.* 
                FROM replication_logs l
                LEFT JOIN {statusTableName} s ON l.id = s.log_entry_id
                WHERE l.table_name = @TableName
                  AND l.direction = 0
                  AND l.id > @LastSyncedId
                  AND (s.is_synced IS NULL OR s.is_synced = 0)
                ORDER BY l.id ASC
                LIMIT @BatchSize";

            var parameters = new List<MySqlParameter>
            {
                new MySqlParameter("@TableName", tableConfig.TableName),
                new MySqlParameter("@LastSyncedId", lastSyncedId),
                new MySqlParameter("@BatchSize", _batchSize)
            };

            var changes = await leaderContext.ReplicationLogs
                .FromSqlRaw(sql, parameters.ToArray())
                .ToListAsync();

            return changes;
        }

        // 获取最后同步的ID
        private async Task<int> GetLastSyncedId(LeaderDbContext leaderContext, string tableName)
        {
            try
            {
                var syncProgress = await leaderContext.SyncProgresses
                    .FirstOrDefaultAsync(sp => sp.TableName == tableName && sp.FollowerServerId == _followerServerId);
                
                return syncProgress?.LastSyncedId ?? 0;
            }
            catch (Exception ex)
            {
                _logger.Warning($"获取同步进度失败，使用默认值0: {ex.Message}");
                return 0;
            }
        }

        // 更新同步进度（主库到从库）
        private async Task UpdateSyncProgress(LeaderDbContext leaderContext, string tableName, int lastSyncedId)
        {
            try
            {
                var syncProgress = await leaderContext.SyncProgresses
                    .FirstOrDefaultAsync(sp => sp.TableName == tableName && sp.FollowerServerId == _followerServerId);

                if (syncProgress == null)
                {
                    // 创建新的同步进度记录
                    syncProgress = new SyncProgress
                    {
                        TableName = tableName,
                        FollowerServerId = _followerServerId,
                        LastSyncedId = lastSyncedId,
                        LastSyncTime = DateTime.Now
                    };
                    leaderContext.SyncProgresses.Add(syncProgress);
                }
                else
                {
                    // 更新现有的同步进度记录
                    syncProgress.LastSyncedId = lastSyncedId;
                    syncProgress.LastSyncTime = DateTime.Now;
                }

                await leaderContext.SaveChangesAsync();
                _logger.Info($"已更新表 {tableName} 的主库到从库同步进度到 ID: {lastSyncedId}");
            }
            catch (Exception ex)
            {
                _logger.Error($"更新主库到从库同步进度失败: {ex.Message}");
            }
        }

        // 获取从库到主库的同步进度
        private async Task<int> GetLastSyncedIdToLeader(FollowerDbContext followerContext, string tableName)
        {
            try
            {
                var syncProgress = await followerContext.SyncProgresses
                    .FirstOrDefaultAsync(sp => sp.TableName == tableName && sp.FollowerServerId == _followerServerId);
                
                return syncProgress?.LastSyncedId ?? 0;
            }
            catch (Exception ex)
            {
                _logger.Warning($"获取从库到主库同步进度失败，使用默认值0: {ex.Message}");
                return 0;
            }
        }

        // 更新从库到主库的同步进度
        private async Task UpdateSyncProgressToLeader(FollowerDbContext followerContext, string tableName, int lastSyncedId)
        {
            try
            {
                var syncProgress = await followerContext.SyncProgresses
                    .FirstOrDefaultAsync(sp => sp.TableName == tableName && sp.FollowerServerId == _followerServerId);

                if (syncProgress == null)
                {
                    // 创建新的同步进度记录
                    syncProgress = new SyncProgress
                    {
                        TableName = tableName,
                        FollowerServerId = _followerServerId,
                        LastSyncedId = lastSyncedId,
                        LastSyncTime = DateTime.Now
                    };
                    followerContext.SyncProgresses.Add(syncProgress);
                }
                else
                {
                    // 更新现有的同步进度记录
                    syncProgress.LastSyncedId = lastSyncedId;
                    syncProgress.LastSyncTime = DateTime.Now;
                }

                await followerContext.SaveChangesAsync();
                _logger.Info($"已更新表 {tableName} 的从库到主库同步进度到 ID: {lastSyncedId}");
            }
            catch (Exception ex)
            {
                _logger.Error($"更新从库到主库同步进度失败: {ex.Message}");
            }
        }

        // 应用变更到从库（带冲突检测和失败重试）
        private async Task ApplyChangesToFollower(
            FollowerDbContext followerContext,
            TableConfig tableConfig,
            List<ReplicationLogEntry> changes)
        {
            var failedChanges = new List<ReplicationLogEntry>();
            var successfulChanges = new List<ReplicationLogEntry>();

            try
            {
                // 只有双向同步才需要检测冲突
                if (tableConfig.ReplicationDirection == ReplicationDirection.Bidirectional)
                {
                    var conflicts = await DetectConflicts(tableConfig, changes);

                    if (conflicts.Any())
                    {
                        _logger.Warning($"检测到 {conflicts.Count} 个冲突，开始解决冲突...");

                        // 解决冲突
                        var resolvedChanges = new List<ReplicationLogEntry>();
                        foreach (var conflict in conflicts)
                        {
                            var resolvedEntry = await ResolveConflict(conflict, tableConfig);
                            if (resolvedEntry != null)
                            {
                                resolvedChanges.Add(resolvedEntry);
                            }

                            // 记录冲突日志
                            await LogConflict(conflict);
                        }

                        // 移除已解决的冲突变更，添加解决后的变更
                        var conflictIds = conflicts.Select(c => c.SourceEntry.Id).ToHashSet();
                        changes = changes.Where(c => !conflictIds.Contains(c.Id)).ToList();
                        changes.AddRange(resolvedChanges);
                    }
                }

                // 对变更列表进行去重处理，确保对同一个主键的多次操作只处理最后一次
                changes = DeduplicateChanges(changes, tableConfig.TableName);

                // 在外层创建事务并设置会话变量
                await using var transaction = await followerContext.Database.BeginTransactionAsync();
                
                try
                {
                    // 设置会话变量防止从库触发器递归
                    await followerContext.Database.ExecuteSqlRawAsync("SET @is_replicating = 1");

                    // 逐个处理变更
                    foreach (var change in changes)
                    {
                        bool success = await ProcessSingleChangeWithRetry(followerContext, tableConfig, change);
                        
                        if (success)
                        {
                            successfulChanges.Add(change);
                        }
                        else
                        {
                            failedChanges.Add(change);
                        }
                    }

                    // 重置会话变量
                    await followerContext.Database.ExecuteSqlRawAsync("SET @is_replicating = 0");
                    
                    // 提交事务
                    await transaction.CommitAsync();
                    
                    _logger.Info($"表 {tableConfig.TableName} 变更处理完成: 成功 {successfulChanges.Count} 条，失败 {failedChanges.Count} 条");
                }
                catch (Exception ex)
                {
                    // 回滚事务
                    await transaction.RollbackAsync();
                    _logger.Error($"表 {tableConfig.TableName} 事务处理失败，已回滚: {ex.Message}");
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"应用变更到从库时出错: {ex.Message}");
                
                // 如果整个过程失败，将所有变更记录为失败
                await LogFailedChanges(changes, tableConfig.TableName, ex.Message);
                throw;
            }
        }



        // 从主库获取实体
        private async Task<object> GetEntityFromLeader(TableConfig tableConfig, string primaryKeyValue)
        {
            using (var leaderContext = CreateLeaderDbContextForRead())
            {
                var primaryKeyProperty = tableConfig.EntityType.GetProperty(tableConfig.PrimaryKey);

                if (primaryKeyProperty == null)
                    throw new InvalidOperationException($"找不到实体 {tableConfig.EntityType.Name} 的主键属性: {tableConfig.PrimaryKey}");

                var convertedValue = Convert.ChangeType(primaryKeyValue, primaryKeyProperty.PropertyType);
                
                // 使用EF Core的FindAsync方法直接查找实体
                return await leaderContext.FindAsync(tableConfig.EntityType, convertedValue);
            }
        }

        // 从主库获取实体（使用现有的DbContext）
        private async Task<object> GetEntityFromLeaderWithContext(LeaderDbContext leaderContext, TableConfig tableConfig, string primaryKeyValue)
        {
            var primaryKeyProperty = tableConfig.EntityType.GetProperty(tableConfig.PrimaryKey);

            if (primaryKeyProperty == null)
                throw new InvalidOperationException($"找不到实体 {tableConfig.EntityType.Name} 的主键属性: {tableConfig.PrimaryKey}");

            var convertedValue = Convert.ChangeType(primaryKeyValue, primaryKeyProperty.PropertyType);
            
            // 使用EF Core的FindAsync方法直接查找实体
            return await leaderContext.FindAsync(tableConfig.EntityType, convertedValue);
        }

        // 应用实体变更到从库
        private async Task ApplyEntityChangeToFollower(
            FollowerDbContext followerContext,
            TableConfig tableConfig,
            ReplicationLogEntry log,
            object entity)
        {
            var setMethod = typeof(DbContext).GetMethod("Set", Type.EmptyTypes);
            var genericSetMethod = setMethod.MakeGenericMethod(tableConfig.EntityType);
            dynamic dbSet = genericSetMethod.Invoke(followerContext, null);

            // 安全地转换实体类型
            dynamic typedEntity = ConvertToTypedEntity(entity, tableConfig.EntityType);
            var primaryKeyProperty = tableConfig.EntityType.GetProperty(tableConfig.PrimaryKey);
            var primaryKeyValue = primaryKeyProperty.GetValue(typedEntity);

            switch (log.OperationType)
            {
                case ReplicationOperation.Insert:
                    // 先检查记录是否已存在
                    var existingEntityI = await followerContext.FindAsync(tableConfig.EntityType, primaryKeyValue);
                    if (existingEntityI != null)
                    {
                        // 记录已存在，跳过处理
                        break;
                    }

                    var trackedInsertEntity = followerContext.Entry(typedEntity).Entity;
                    dbSet.Add(typedEntity);
                    _logger.Info($"在从库插入表 {tableConfig.TableName} 记录 {log.Data}");
                    break;

                case ReplicationOperation.Update:
                    // 检查目标记录是否存在
                    var existingEntity = await followerContext.FindAsync(tableConfig.EntityType, primaryKeyValue);
                    
                    if (existingEntity != null)
                    {
                        // 记录存在，更新已跟踪的实体
                        followerContext.Entry(existingEntity).CurrentValues.SetValues(typedEntity);

                        // 特殊处理：如果是自增主键，确保不更新主键值
                        if (IsAutoIncrementPrimaryKey(tableConfig))
                        {
                            followerContext.Entry(existingEntity).Property(tableConfig.PrimaryKey).IsModified = false;
                        }

                        _logger.Info($"在从库更新表 {tableConfig.TableName} 记录 {log.Data}");
                    }
                    else
                    {
                        // 记录不存在，转换为插入操作
                        // 确保实体未被跟踪
                        var trackedEntity = followerContext.Entry(typedEntity).Entity;
                        if (followerContext.Entry(trackedEntity).State != EntityState.Detached)
                        {
                            followerContext.Entry(trackedEntity).State = EntityState.Detached;
                        }
                        dbSet.Add(typedEntity);
                        _logger.Info($"在从库中记录不存在，将更新操作转换为插入操作 - 表 {tableConfig.TableName} 记录 {log.Data}");
                    }
                    break;

                case ReplicationOperation.Delete:
                    // 对于删除操作，先查找已跟踪的实体
                    var entityToDelete = await followerContext.FindAsync(tableConfig.EntityType, primaryKeyValue);
                    if (entityToDelete != null)
                    {
                        // 标记为删除状态
                        followerContext.Entry(entityToDelete).State = EntityState.Deleted;
                        _logger.Info($"在从库删除表 {tableConfig.TableName} 记录 {log.Data}");
                    }
                    break;
            }
        }

        // 在从库删除实体
        private async Task DeleteEntityInFollower(
            FollowerDbContext followerContext,
            TableConfig tableConfig,
            string primaryKeyValue)
        {
            var setMethod = typeof(DbContext).GetMethod("Set", Type.EmptyTypes);
            var genericSetMethod = setMethod.MakeGenericMethod(tableConfig.EntityType);
            dynamic dbSet = genericSetMethod.Invoke(followerContext, null);
            var primaryKeyProperty = tableConfig.EntityType.GetProperty(tableConfig.PrimaryKey);

            if (primaryKeyProperty == null)
                throw new InvalidOperationException($"找不到实体 {tableConfig.EntityType.Name} 的主键属性: {tableConfig.PrimaryKey}");
            var convertedValue = Convert.ChangeType(primaryKeyValue, primaryKeyProperty.PropertyType);
            // 对于删除操作，先查找已跟踪的实体
            var entityToDelete = await followerContext.FindAsync(tableConfig.EntityType, convertedValue);
            if (entityToDelete != null)
            {
                // 标记为删除状态
                followerContext.Entry(entityToDelete).State = EntityState.Deleted;

                _logger.Info($"在从库删除表 {tableConfig.TableName} 记录 {primaryKeyValue}");
            }
        }

        // 安全地转换实体类型
        private object ConvertToTypedEntity(object sourceEntity, Type targetType)
        {
            if (sourceEntity == null)
                return null;

            // 如果已经是正确的类型，直接返回
            if (sourceEntity.GetType() == targetType)
                return sourceEntity;

            try
            {
                // 创建目标类型的实例
                var targetEntity = Activator.CreateInstance(targetType);

                // 获取源实体和目标实体的所有属性
                var sourceProperties = sourceEntity.GetType().GetProperties();
                var targetProperties = targetType.GetProperties();

                // 复制属性值
                foreach (var sourceProperty in sourceProperties)
                {
                    if (!sourceProperty.CanRead)
                        continue;

                    var targetProperty = targetProperties.FirstOrDefault(p => 
                        p.Name == sourceProperty.Name && 
                        p.CanWrite && 
                        (p.PropertyType == sourceProperty.PropertyType || 
                         p.PropertyType.IsAssignableFrom(sourceProperty.PropertyType)));

                    if (targetProperty != null)
                    {
                        var value = sourceProperty.GetValue(sourceEntity);
                        
                        // 处理类型转换
                        if (value != null && targetProperty.PropertyType != sourceProperty.PropertyType)
                        {
                            try
                            {
                                // 尝试类型转换
                                if (targetProperty.PropertyType.IsEnum && value is string stringValue)
                                {
                                    value = Enum.Parse(targetProperty.PropertyType, stringValue);
                                }
                                else if (targetProperty.PropertyType == typeof(Guid) && value is string guidString)
                                {
                                    value = Guid.Parse(guidString);
                                }
                                else
                                {
                                    value = Convert.ChangeType(value, targetProperty.PropertyType);
                                }
                            }
                            catch
                            {
                                // 如果转换失败，跳过这个属性
                                continue;
                            }
                        }
                        
                        targetProperty.SetValue(targetEntity, value);
                    }
                }

                return targetEntity;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"无法将类型 {sourceEntity.GetType().Name} 转换为 {targetType.Name}: {ex.Message}", ex);
            }
        }

        // 检查是否是自增主键
        private bool IsAutoIncrementPrimaryKey(TableConfig tableConfig)
        {
            using (var connection = new MySqlConnection(_followerConnectionString))
            {
                connection.Open();

                string sql = $@"
                    SELECT EXTRA 
                    FROM information_schema.COLUMNS 
                    WHERE TABLE_NAME = @TableName 
                      AND COLUMN_NAME = @PrimaryKey
                      AND TABLE_SCHEMA = DATABASE()";

                using (var command = new MySqlCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@TableName", tableConfig.TableName);
                    command.Parameters.AddWithValue("@PrimaryKey", tableConfig.PrimaryKey);

                    using (var reader = command.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            string extra = reader.GetString("EXTRA");
                            return extra.Contains("auto_increment");
                        }
                    }
                }
            }

            return false;
        }

        // 标记变更为已处理
        private async Task MarkChangesAsProcessed(
            LeaderDbContext leaderContext,
            List<ReplicationLogEntry> changes)
        {
            await ExecuteWithRetry(async () =>
            {
                string statusTableName = $"replication_status_{_followerServerId}";

                using (var connection = (MySqlConnection)leaderContext.Database.GetDbConnection())
                {
                    if (connection.State != ConnectionState.Open)
                    {
                        await connection.OpenAsync();
                    }
                    await using var transaction = await connection.BeginTransactionAsync();

                    try
                    {
                        foreach (var change in changes)
                        {
                            string updateSql = $@"
                                INSERT INTO {statusTableName} (log_entry_id, is_synced, sync_time)
                                VALUES (@LogEntryId, 1, @SyncTime)
                                ON DUPLICATE KEY UPDATE 
                                    is_synced = 1,
                                    sync_time = @SyncTime";

                            using (var command = new MySqlCommand(updateSql, connection, transaction))
                            {
                                command.Parameters.AddWithValue("@LogEntryId", change.Id);
                                command.Parameters.AddWithValue("@SyncTime", DateTime.Now);
                                await command.ExecuteNonQueryAsync();
                            }
                        }

                        await transaction.CommitAsync();
                        
                        // 更新同步进度到最大的变更ID
                        if (changes.Any())
                        {
                            var maxId = changes.Max(c => c.Id);
                            var tableName = changes.First().TableName;
                            await UpdateSyncProgress(leaderContext, tableName, maxId);
                        }
                    }
                    catch (Exception ex)
                    {
                        await transaction.RollbackAsync();
                        _logger.Error($"标记变更为已处理时出错: {ex.Message}");
                        throw;
                    }
                }
            }, 3, 1000, "标记变更为已处理");
        }

        // 带执行状态控制的方法包装器
        private async Task ExecuteWithFlag(string timerKey, Func<Task> action)
        {
            lock (_executionLock)
            {
                if (_executionFlags[timerKey])
                {
                    _logger.Info($"定时器 {timerKey} 正在执行中，跳过本次执行");
                    return;
                }
                _executionFlags[timerKey] = true;
            }

            try
            {
                await action();
            }
            catch (Exception ex)
            {
                _logger.Error($"定时器 {timerKey} 执行出错: {ex.Message}");
            }
            finally
            {
                lock (_executionLock)
                {
                    _executionFlags[timerKey] = false;
                }
            }
        }

        // 带执行状态控制的清理方法
        private async Task ExecuteCleanupWithFlag()
        {
            if (_isCleanupExecuting)
            {
                _logger.Info("数据清理任务正在执行中，跳过本次执行");
                return;
            }

            _isCleanupExecuting = true;
            try
            {
                await CleanupOldReplicationLogs();
            }
            catch (Exception ex)
            {
                _logger.Error($"数据清理任务执行出错: {ex.Message}");
            }
            finally
            {
                _isCleanupExecuting = false;
            }
        }

        // 从库向主库同步变更
        private async Task PushFollowerChangesToLeader(TableConfig tableConfig)
        {
            try
            {
                List<ReplicationLogEntry> pendingChanges;
                
                // 第一步：获取待同步的变更
                using (var followerContext = CreateFollowerDbContext())
                {
                    pendingChanges = await GetPendingChangesFromFollower(followerContext, tableConfig);
                }

                if (pendingChanges.Any())
                {
                    _logger.Info($"从从库拉取了 {pendingChanges.Count} 条表 {tableConfig.TableName} 的变更，准备推送到主库");

                    // 第二步：应用变更到主库
                    using (var leaderContext = CreateLeaderDbContext())
                    {
                        await ApplyChangesToLeader(leaderContext, tableConfig, pendingChanges);
                    }

                    // 第三步：标记变更为已处理并更新同步进度
                    using (var followerContext = CreateFollowerDbContext())
                    {
                        // 标记变更为已处理
                        await MarkFollowerChangesAsProcessed(followerContext, pendingChanges, tableConfig);
                    }

                    _logger.Info($"成功将 {pendingChanges.Count} 条表 {tableConfig.TableName} 的变更推送到主库");
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"推送从库变更到主库时出错: {ex.Message}");
            }
        }

        // 获取从库待同步的变更
        private async Task<List<ReplicationLogEntry>> GetPendingChangesFromFollower(
            FollowerDbContext followerContext,
            TableConfig tableConfig)
        {
            // 获取当前表的从库到主库同步进度
            var lastSyncedId = await GetLastSyncedIdToLeader(followerContext, tableConfig.TableName);
            
            string statusTableName = $"replication_status_{_followerServerId}_to_leader";

            var sql = $@"
                SELECT l.* 
                FROM replication_logs l
                LEFT JOIN {statusTableName} s ON l.id = s.log_entry_id
                WHERE l.table_name = @TableName
                  AND l.direction = 1
                  AND l.id > @LastSyncedId
                  AND (s.is_synced IS NULL OR s.is_synced = 0)
                ORDER BY l.id ASC
                LIMIT @BatchSize";

            return await followerContext.ReplicationLogs
                .FromSqlRaw(sql, 
                    new MySqlParameter("@TableName", tableConfig.TableName),
                    new MySqlParameter("@LastSyncedId", lastSyncedId),
                    new MySqlParameter("@BatchSize", _batchSize))
                .ToListAsync();
        }

        // 应用变更到主库（事务外移）
        private async Task ApplyChangesToLeader(
            LeaderDbContext leaderContext,
            TableConfig tableConfig,
            List<ReplicationLogEntry> changes)
        {
            var successfulChanges = new List<ReplicationLogEntry>();
            var failedChanges = new List<ReplicationLogEntry>();

            try
            {
                // 对变更列表进行去重处理，确保对同一个主键的多次操作只处理最后一次
                changes = DeduplicateChanges(changes, tableConfig.TableName);

                // 在外层创建事务并设置会话变量
                await using var transaction = await leaderContext.Database.BeginTransactionAsync();
                
                try
                {
                    // 设置会话变量防止主库触发器递归
                    await leaderContext.Database.ExecuteSqlRawAsync("SET @is_replicating = 1");

                    // 逐个处理变更
                    foreach (var change in changes)
                    {
                        bool success = await ProcessSingleChangeWithRetryToLeader(leaderContext, tableConfig, change);
                        
                        if (success)
                        {
                            successfulChanges.Add(change);
                        }
                        else
                        {
                            failedChanges.Add(change);
                        }
                    }

                    // 重置会话变量
                    await leaderContext.Database.ExecuteSqlRawAsync("SET @is_replicating = 0");
                    
                    // 提交事务
                    await transaction.CommitAsync();
                    
                    _logger.Info($"表 {tableConfig.TableName} 变更处理完成: 成功 {successfulChanges.Count} 条，失败 {failedChanges.Count} 条");
                }
                catch (Exception ex)
                {
                    // 回滚事务
                    await transaction.RollbackAsync();
                    _logger.Error($"表 {tableConfig.TableName} 事务处理失败，已回滚: {ex.Message}");
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"应用变更到主库时出错: {ex.Message}");
                
                // 如果整个过程失败，将所有变更记录为失败
                await LogFailedChanges(changes, tableConfig.TableName, ex.Message);
                throw;
            }
        }

        // 处理单个变更的重试逻辑（从库到主库，事务外移）
        private async Task<bool> ProcessSingleChangeWithRetryToLeader(
            LeaderDbContext leaderContext,
            TableConfig tableConfig,
            ReplicationLogEntry change,
            int maxRetries = 3)
        {
            Exception lastException = null;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    if (tableConfig.SyncMode == TableSyncMode.Entity)
                    {
                        // Entity模式：使用EF Core实体处理
                        var entity = await GetEntityFromFollower(tableConfig, change.Data);

                        if (entity != null)
                        {
                            // 应用变更到主库
                            await ApplyEntityChangeToLeader(leaderContext, tableConfig, change, entity);
                        }
                        else if (change.OperationType == ReplicationOperation.Delete)
                        {
                            // 对于删除操作，如果实体不存在，直接执行删除
                            //await DeleteEntityInLeader(leaderContext, tableConfig, change.Data);
                        }

                        // 每条数据调用一次SaveChanges
                        await leaderContext.SaveChangesAsync();
                    }
                    else if (tableConfig.SyncMode == TableSyncMode.NoEntity)
                    {
                        // NoEntity模式：使用原始SQL处理
                        // 从从库获取原始数据
                        var rawData = await GetRawDataFromFollower(tableConfig, change.Data);

                        if (rawData != null)
                        {
                            // 应用原始数据变更到主库
                            await ApplyRawDataChangeToLeader(tableConfig, change, rawData);
                        }
                        else if (change.OperationType == ReplicationOperation.Delete)
                        {
                            // 对于删除操作，如果数据不存在，直接执行删除
                            await DeleteRawDataInLeader(tableConfig, change.Data);
                        }
                    }

                    _logger.Info($"表 {tableConfig.TableName} 变更 ID {change.Id} 处理成功（从库到主库）");
                    return true; // 成功处理
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    _logger.Warning($"表 {tableConfig.TableName} 变更 ID {change.Id} 第 {attempt} 次尝试失败（从库到主库）: {ex.Message}");

                    if (attempt < maxRetries)
                    {
                        // 指数退避策略
                        int delay = 1000 * (int)Math.Pow(2, attempt - 1);
                        _logger.Info($"等待 {delay}ms 后进行第 {attempt + 1} 次重试...");
                        await Task.Delay(delay);
                    }
                }
            }

            // 重试3次后仍然失败，记录失败数据
            await LogFailedChange(change, tableConfig.TableName, lastException?.Message);
            _logger.Error($"表 {tableConfig.TableName} 变更 ID {change.Id} 重试 {maxRetries} 次后仍然失败（从库到主库）: {lastException?.Message}");
            return false; // 处理失败
        }

        // 从从库获取实体
        private async Task<object> GetEntityFromFollower(TableConfig tableConfig, string primaryKeyValue)
        {
            using (var followerContext = CreateFollowerDbContext())
            {
                var setMethod = typeof(DbContext).GetMethod("Set", Type.EmptyTypes);
                var genericSetMethod = setMethod.MakeGenericMethod(tableConfig.EntityType);
                dynamic dbSet = genericSetMethod.Invoke(followerContext, null);
                var primaryKeyProperty = tableConfig.EntityType.GetProperty(tableConfig.PrimaryKey);

                if (primaryKeyProperty == null)
                    throw new InvalidOperationException($"找不到实体 {tableConfig.EntityType.Name} 的主键属性: {tableConfig.PrimaryKey}");

                var convertedValue = Convert.ChangeType(primaryKeyValue, primaryKeyProperty.PropertyType);
                return await followerContext.FindAsync(tableConfig.EntityType, convertedValue);
            }
        }

        // 应用实体变更到主库
        private async Task ApplyEntityChangeToLeader(
            LeaderDbContext leaderContext,
            TableConfig tableConfig,
            ReplicationLogEntry log,
            object entity)
        {
            var setMethod = typeof(DbContext).GetMethod("Set", Type.EmptyTypes);
            var genericSetMethod = setMethod.MakeGenericMethod(tableConfig.EntityType);
            dynamic dbSet = genericSetMethod.Invoke(leaderContext, null);

            // 安全地转换实体类型
            dynamic typedEntity = ConvertToTypedEntity(entity, tableConfig.EntityType);
            var primaryKeyProperty = tableConfig.EntityType.GetProperty(tableConfig.PrimaryKey);
            var primaryKeyValue = primaryKeyProperty.GetValue(typedEntity);

            switch (log.OperationType)
            {
                case ReplicationOperation.Insert:

                    // 先检查记录是否已存在
                    var existingEntityI = await leaderContext.FindAsync(tableConfig.EntityType, primaryKeyValue);
                    if (existingEntityI != null)
                    {
                        // 记录已存在，跳过处理
                        break;
                    }

                    var trackedInsertEntity = leaderContext.Entry(typedEntity).Entity;
                    dbSet.Add(typedEntity);
                    _logger.Info($"在从库插入表 {tableConfig.TableName} 记录 {log.Data}");
                    break;

                case ReplicationOperation.Update:
                    // 检查目标记录是否存在
                    var existingEntity = await leaderContext.FindAsync(tableConfig.EntityType, primaryKeyValue);
                    
                    if (existingEntity != null)
                    {
                        // 检查主库记录是否在从库变更之后有新的变更
                        if (CheckLeaderReplicationLogForConflict(existingEntity, log, tableConfig))
                        {
                            _logger.Warning($"检测到冲突：主库表 {tableConfig.TableName} 记录在从库变更之后有新的变更，跳过覆盖。主键: {primaryKeyValue}");
                            break;
                        }

                        // 记录存在，更新已跟踪的实体
                        leaderContext.Entry(existingEntity).CurrentValues.SetValues(typedEntity);

                        // 特殊处理：如果是自增主键，确保不更新主键值
                        if (IsAutoIncrementPrimaryKey(tableConfig))
                        {
                            leaderContext.Entry(existingEntity).Property(tableConfig.PrimaryKey).IsModified = false;
                        }

                        _logger.Info($"在主库更新表 {tableConfig.TableName} 记录 {log.Data}（来自从库 {_followerServerId}）");
                    }
                    else
                    {
                        // 记录不存在，转换为插入操作
                        // 确保实体未被跟踪
                        var trackedEntity = leaderContext.Entry(typedEntity).Entity;
                        if (leaderContext.Entry(trackedEntity).State != EntityState.Detached)
                        {
                            leaderContext.Entry(trackedEntity).State = EntityState.Detached;
                        }
                        dbSet.Add(typedEntity);
                        _logger.Info($"在主库中记录不存在，将更新操作转换为插入操作 - 表 {tableConfig.TableName} 记录 {log.Data}（来自从库 {_followerServerId}）");
                    }
                    break;

                case ReplicationOperation.Delete:
                    //// 对于删除操作，先查找已跟踪的实体
                    //var entityToDelete = await leaderContext.FindAsync(tableConfig.EntityType, primaryKeyValue);
                    //if (entityToDelete != null)
                    //{
                    //    dbSet.Remove(entityToDelete);
                    //    _logger.Info($"在主库删除表 {tableConfig.TableName} 记录 {log.Data}（来自从库 {_followerServerId}）");
                    //}
                    //else
                    //{
                    //    // 如果实体不存在，创建一个只包含主键的实体进行删除
                    //    var deleteEntity = Activator.CreateInstance(tableConfig.EntityType);
                    //    primaryKeyProperty.SetValue(deleteEntity, primaryKeyValue);
                    //    leaderContext.Entry(deleteEntity).State = EntityState.Deleted;
                    //    _logger.Info($"在主库删除表 {tableConfig.TableName} 记录 {log.Data}（实体不存在，创建删除标记）（来自从库 {_followerServerId}）");
                    //}
                    break;
            }
        }

        // 在主库删除实体
        private async Task DeleteEntityInLeader(
            LeaderDbContext leaderContext,
            TableConfig tableConfig,
            string primaryKeyValue)
        {
            var setMethod = typeof(DbContext).GetMethod("Set", Type.EmptyTypes);
            var genericSetMethod = setMethod.MakeGenericMethod(tableConfig.EntityType);
            dynamic dbSet = genericSetMethod.Invoke(leaderContext, null);
            var primaryKeyProperty = tableConfig.EntityType.GetProperty(tableConfig.PrimaryKey);

            if (primaryKeyProperty == null)
                throw new InvalidOperationException($"找不到实体 {tableConfig.EntityType.Name} 的主键属性: {tableConfig.PrimaryKey}");

            var convertedValue = Convert.ChangeType(primaryKeyValue, primaryKeyProperty.PropertyType);

            // 创建一个只包含主键的实体实例
            var entity = Activator.CreateInstance(tableConfig.EntityType);
            primaryKeyProperty.SetValue(entity, convertedValue);

            // 标记为删除状态
            leaderContext.Entry(entity).State = EntityState.Deleted;
        }

        #region NoEntity模式数据变更处理方法

        // 从主库获取原始数据（NoEntity模式）
        private async Task<Dictionary<string, object>> GetRawDataFromLeader(TableConfig tableConfig, string primaryKeyValue)
        {
            try
            {
                using var connection = new MySqlConnection(_leaderReadOnlyConnectionString);
                await connection.OpenAsync();

                // 获取表结构信息
                var tableSchema = await GetTableSchemaFromDatabase(tableConfig.TableName, true);
                var columns = tableSchema.Columns.Select(c => $"`{c.ColumnName}`").ToList();
                var columnList = string.Join(", ", columns);

                // 构建查询SQL
                var sql = $"SELECT {columnList} FROM `{tableConfig.TableName}` WHERE `{tableConfig.PrimaryKey}` = @PrimaryKey";

                using var command = new MySqlCommand(sql, connection);
                command.Parameters.AddWithValue("@PrimaryKey", primaryKeyValue);

                using var reader = await command.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    var result = new Dictionary<string, object>();
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        var columnName = reader.GetName(i);
                        var value = reader.IsDBNull(i) ? null : reader.GetValue(i);
                        result[columnName] = value;
                    }
                    return result;
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.Error($"从主库获取原始数据失败 - 表: {tableConfig.TableName}, 主键: {primaryKeyValue}, 错误: {ex.Message}");
                throw;
            }
        }

        // 应用原始数据变更到从库（NoEntity模式）
        private async Task ApplyRawDataChangeToFollower(
            FollowerDbContext followerContext,
            TableConfig tableConfig,
            ReplicationLogEntry log,
            Dictionary<string, object> data)
        {
            try
            {
                var connection = (MySqlConnection)followerContext.Database.GetDbConnection();
                var transaction = followerContext.Database.CurrentTransaction?.GetDbTransaction() as MySqlTransaction;
                
                if (connection.State != ConnectionState.Open)
                {
                    await connection.OpenAsync();
                }

                var primaryKeyValue = data[tableConfig.PrimaryKey];

                switch (log.OperationType)
                {
                    case ReplicationOperation.Insert:
                        await ExecuteRawInsert(connection, transaction, tableConfig, data);
                        _logger.Info($"在从库插入表 {tableConfig.TableName} 记录 {primaryKeyValue}");
                        break;

                    case ReplicationOperation.Update:
                        // 先尝试更新，如果记录不存在则插入
                        var updateResult = await ExecuteRawUpdate(connection, transaction, tableConfig, data);
                        if (updateResult == 0)
                        {
                            await ExecuteRawInsert(connection, transaction, tableConfig, data);
                            _logger.Info($"在从库中记录不存在，将更新操作转换为插入操作 - 表 {tableConfig.TableName} 记录 {primaryKeyValue}");
                        }
                        else
                        {
                            _logger.Info($"在从库更新表 {tableConfig.TableName} 记录 {primaryKeyValue}");
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"应用原始数据变更到从库失败 - 表: {tableConfig.TableName}, 操作: {log.OperationType}, 错误: {ex.Message}");
                throw;
            }
        }

        // 在从库删除原始数据（NoEntity模式）
        private async Task DeleteRawDataInFollower(
            FollowerDbContext followerContext,
            TableConfig tableConfig,
            string primaryKeyValue)
        {
            try
            {
                var connection = (MySqlConnection)followerContext.Database.GetDbConnection();
                var transaction = followerContext.Database.CurrentTransaction?.GetDbTransaction() as MySqlTransaction;
                
                if (connection.State != ConnectionState.Open)
                {
                    await connection.OpenAsync();
                }

                var sql = $"DELETE FROM `{tableConfig.TableName}` WHERE `{tableConfig.PrimaryKey}` = @PrimaryKey";
                using var command = new MySqlCommand(sql, connection, transaction);
                command.Parameters.AddWithValue("@PrimaryKey", primaryKeyValue);

                var affectedRows = await command.ExecuteNonQueryAsync();
                _logger.Info($"在从库删除表 {tableConfig.TableName} 记录 {primaryKeyValue}，影响行数: {affectedRows}");
            }
            catch (Exception ex)
            {
                _logger.Error($"在从库删除原始数据失败 - 表: {tableConfig.TableName}, 主键: {primaryKeyValue}, 错误: {ex.Message}");
                throw;
            }
        }

        // 执行原始插入操作
        private async Task ExecuteRawInsert(MySqlConnection connection, MySqlTransaction transaction, TableConfig tableConfig, Dictionary<string, object> data)
        {
            var columns = data.Keys.Select(k => $"`{k}`").ToList();
            var parameters = data.Keys.Select(k => $"@{k}").ToList();

            var sql = $"INSERT INTO `{tableConfig.TableName}` ({string.Join(", ", columns)}) VALUES ({string.Join(", ", parameters)})";
            
            using var command = new MySqlCommand(sql, connection, transaction);
            foreach (var kvp in data)
            {
                command.Parameters.AddWithValue($"@{kvp.Key}", kvp.Value ?? DBNull.Value);
            }

            await command.ExecuteNonQueryAsync();
        }

        // 执行原始更新操作
        private async Task<int> ExecuteRawUpdate(MySqlConnection connection, MySqlTransaction transaction, TableConfig tableConfig, Dictionary<string, object> data)
        {
            var setParts = data.Keys.Where(k => k != tableConfig.PrimaryKey)
                                   .Select(k => $"`{k}` = @{k}")
                                   .ToList();

            if (setParts.Count == 0)
            {
                return 0; // 没有需要更新的列
            }

            var sql = $"UPDATE `{tableConfig.TableName}` SET {string.Join(", ", setParts)} WHERE `{tableConfig.PrimaryKey}` = @{tableConfig.PrimaryKey}";
            
            using var command = new MySqlCommand(sql, connection, transaction);
            foreach (var kvp in data)
            {
                command.Parameters.AddWithValue($"@{kvp.Key}", kvp.Value ?? DBNull.Value);
            }

            return await command.ExecuteNonQueryAsync();
        }

        // 从从库获取原始数据（NoEntity模式）
        private async Task<Dictionary<string, object>> GetRawDataFromFollower(TableConfig tableConfig, string primaryKeyValue)
        {
            try
            {
                using var connection = new MySqlConnection(_followerConnectionString);
                await connection.OpenAsync();

                // 获取表结构信息
                var tableSchema = await GetTableSchemaFromDatabase(tableConfig.TableName, false);
                var columns = tableSchema.Columns.Select(c => $"`{c.ColumnName}`").ToList();
                var columnList = string.Join(", ", columns);

                // 构建查询SQL
                var sql = $"SELECT {columnList} FROM `{tableConfig.TableName}` WHERE `{tableConfig.PrimaryKey}` = @PrimaryKey";

                using var command = new MySqlCommand(sql, connection);
                command.Parameters.AddWithValue("@PrimaryKey", primaryKeyValue);

                using var reader = await command.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    var result = new Dictionary<string, object>();
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        var columnName = reader.GetName(i);
                        var value = reader.IsDBNull(i) ? null : reader.GetValue(i);
                        result[columnName] = value;
                    }
                    return result;
                }
                return null;
            }
            catch (Exception ex)
            {
                _logger.Error($"从从库获取原始数据失败 - 表: {tableConfig.TableName}, 主键: {primaryKeyValue}, 错误: {ex.Message}");
                throw;
            }
        }

        // 将原始数据变更应用到主库（NoEntity模式）
        private async Task ApplyRawDataChangeToLeader(TableConfig tableConfig, ReplicationLogEntry change, Dictionary<string, object> data)
        {
            try
            {
                using var connection = new MySqlConnection(_leaderConnectionString);
                await connection.OpenAsync();

                // 设置复制标志
                using var setFlagCommand = new MySqlCommand("SET @is_replicating = 1", connection);
                await setFlagCommand.ExecuteNonQueryAsync();

                try
                {
                    if (change.OperationType == ReplicationOperation.Insert)
                    {
                        await ExecuteRawInsertToLeader(connection, tableConfig, data);
                    }
                    else if (change.OperationType == ReplicationOperation.Update)
                    {
                        var affectedRows = await ExecuteRawUpdateToLeader(connection, tableConfig, data);
                        if (affectedRows == 0)
                        {
                            // 如果更新没有影响任何行，说明记录不存在，转为插入
                            await ExecuteRawInsertToLeader(connection, tableConfig, data);
                            _logger.Info($"主库中记录不存在，将更新操作转换为插入操作 - 表 {tableConfig.TableName}");
                        }
                    }
                }
                finally
                {
                    // 重置复制标志
                    using var resetFlagCommand = new MySqlCommand("SET @is_replicating = 0", connection);
                    await resetFlagCommand.ExecuteNonQueryAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"应用原始数据变更到主库失败 - 表: {tableConfig.TableName}, 错误: {ex.Message}");
                throw;
            }
        }

        // 在主库删除原始数据（NoEntity模式）
        private async Task DeleteRawDataInLeader(TableConfig tableConfig, string primaryKeyValue)
        {
            try
            {
                using var connection = new MySqlConnection(_leaderConnectionString);
                await connection.OpenAsync();

                // 设置复制标志
                using var setFlagCommand = new MySqlCommand("SET @is_replicating = 1", connection);
                await setFlagCommand.ExecuteNonQueryAsync();

                try
                {
                    var sql = $"DELETE FROM `{tableConfig.TableName}` WHERE `{tableConfig.PrimaryKey}` = @PrimaryKey";
                    using var command = new MySqlCommand(sql, connection);
                    command.Parameters.AddWithValue("@PrimaryKey", primaryKeyValue);

                    var affectedRows = await command.ExecuteNonQueryAsync();
                    _logger.Info($"在主库删除表 {tableConfig.TableName} 记录，主键: {primaryKeyValue}, 影响行数: {affectedRows}");
                }
                finally
                {
                    // 重置复制标志
                    using var resetFlagCommand = new MySqlCommand("SET @is_replicating = 0", connection);
                    await resetFlagCommand.ExecuteNonQueryAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"在主库删除原始数据失败 - 表: {tableConfig.TableName}, 主键: {primaryKeyValue}, 错误: {ex.Message}");
                throw;
            }
        }

        // 执行原始插入操作到主库
        private async Task ExecuteRawInsertToLeader(MySqlConnection connection, TableConfig tableConfig, Dictionary<string, object> data)
        {
            var columns = string.Join(", ", data.Keys.Select(k => $"`{k}`"));
            var values = string.Join(", ", data.Keys.Select(k => $"@{k}"));
            var sql = $"INSERT INTO `{tableConfig.TableName}` ({columns}) VALUES ({values})";

            using var command = new MySqlCommand(sql, connection);
            foreach (var kvp in data)
            {
                command.Parameters.AddWithValue($"@{kvp.Key}", kvp.Value ?? DBNull.Value);
            }

            await command.ExecuteNonQueryAsync();
        }

        // 执行原始更新操作到主库
        private async Task<int> ExecuteRawUpdateToLeader(MySqlConnection connection, TableConfig tableConfig, Dictionary<string, object> data)
        {
            var setParts = data.Keys.Where(k => k != tableConfig.PrimaryKey)
                                   .Select(k => $"`{k}` = @{k}")
                                   .ToList();

            if (setParts.Count == 0)
            {
                return 0; // 没有需要更新的列
            }

            var sql = $"UPDATE `{tableConfig.TableName}` SET {string.Join(", ", setParts)} WHERE `{tableConfig.PrimaryKey}` = @{tableConfig.PrimaryKey}";
            
            using var command = new MySqlCommand(sql, connection);
            foreach (var kvp in data)
            {
                command.Parameters.AddWithValue($"@{kvp.Key}", kvp.Value ?? DBNull.Value);
            }

            return await command.ExecuteNonQueryAsync();
        }

        #endregion

        // 标记从库变更为已处理
        private async Task MarkFollowerChangesAsProcessed(
            FollowerDbContext followerContext,
            List<ReplicationLogEntry> changes, TableConfig tableConfig)
        {
            await ExecuteWithRetry(async () =>
            {
                string statusTableName = $"replication_status_{_followerServerId}_to_leader";
                int lastProcessedId = (int)changes.Max(c => c.Id);

                // 第一步：标记复制状态
                using (var connection = (MySqlConnection)followerContext.Database.GetDbConnection())
                {
                    await connection.OpenAsync();
                    await using var transaction = await connection.BeginTransactionAsync();

                    try
                    {
                        foreach (var change in changes)
                        {
                            string updateSql = $@"
                                INSERT INTO {statusTableName} (log_entry_id, is_synced, sync_time)
                                VALUES (@LogEntryId, 1, @SyncTime)
                                ON DUPLICATE KEY UPDATE 
                                    is_synced = 1,
                                    sync_time = @SyncTime";

                            using (var command = new MySqlCommand(updateSql, connection, transaction))
                            {
                                command.Parameters.AddWithValue("@LogEntryId", change.Id);
                                command.Parameters.AddWithValue("@SyncTime", DateTime.Now);
                                await command.ExecuteNonQueryAsync();
                            }
                        }
                        await transaction.CommitAsync();

                        // 第二步：更新同步进度（使用独立的DbContext事务）
                        await UpdateSyncProgressToLeader(followerContext, tableConfig.TableName, lastProcessedId);
                    }
                    catch (Exception ex)
                    {
                        await transaction.RollbackAsync();
                        _logger.Error($"标记从库变更为已处理时出错: {ex.Message}");
                        throw;
                    }
                }
            }, 3, 1000, "标记从库变更为已处理");
        }

        // 创建主库上下文
        private LeaderDbContext CreateLeaderDbContext()
        {
            return new LeaderDbContext(
                new DbContextOptionsBuilder<LeaderDbContext>()
                    .UseMySql(ServerVersion.AutoDetect(_leaderConnectionString))
                    .Options,
                _leaderConnectionString,
                _tableConfigs);
        }

        // 创建主库只读上下文
        private LeaderDbContext CreateLeaderDbContextForRead()
        {
            return new LeaderDbContext(
                new DbContextOptionsBuilder<LeaderDbContext>()
                    .UseMySql(ServerVersion.AutoDetect(_leaderReadOnlyConnectionString))
                    .Options,
                _leaderReadOnlyConnectionString,
                _tableConfigs);
        }

        // 创建从库上下文
        private FollowerDbContext CreateFollowerDbContext()
        {
            return new FollowerDbContext(
                new DbContextOptionsBuilder<FollowerDbContext>()
                    .UseMySql(ServerVersion.AutoDetect(_followerConnectionString))
                    .Options,
                _followerConnectionString,
                _followerServerId,
                _tableConfigs);
        }

        // 清理旧的复制日志数据
        private async Task CleanupOldReplicationLogs()
        {
            try
            {
                _logger.Info($"开始清理 {_dataRetentionDays} 天前的复制日志数据...");
                var cutoffDate = DateTime.Now.AddDays(-_dataRetentionDays);
                
                // 清理主库的复制日志
                await CleanupReplicationLogsInDatabase("主库", () => CreateLeaderDbContext(), cutoffDate);
                
                // 清理从库的复制日志
                await CleanupReplicationLogsInDatabase("从库", () => CreateFollowerDbContext(), cutoffDate);
                
                _logger.Info($"复制日志清理完成，已删除 {cutoffDate:yyyy-MM-dd HH:mm:ss} 之前的数据");
            }
            catch (Exception ex)
            {
                _logger.Error($"清理复制日志时出错: {ex.Message}");
            }
        }

        // 在指定数据库中清理复制日志
        private async Task CleanupReplicationLogsInDatabase<T>(string dbName, Func<T> createContext, DateTime cutoffDate) where T : DbContext, IDisposable
        {
            try
            {
                using var context = createContext();
                using var connection = (MySqlConnection)context.Database.GetDbConnection();
                await connection.OpenAsync();
                
                // 清理 replication_logs 表
                var deleteSql = "DELETE FROM replication_logs WHERE timestamp < @CutoffDate";
                using var command = new MySqlCommand(deleteSql, connection);
                command.Parameters.AddWithValue("@CutoffDate", cutoffDate);
                
                var deletedCount = await command.ExecuteNonQueryAsync();
                _logger.Info($"{dbName} - 已删除 {deletedCount} 条过期的复制日志记录");
                
                // 清理复制状态表（如果存在）
                await CleanupReplicationStatusTables(connection, cutoffDate, dbName);
            }
            catch (Exception ex)
            {
                _logger.Error($"清理 {dbName} 复制日志时出错: {ex.Message}");
            }
        }

        // 清理复制状态表
        private async Task CleanupReplicationStatusTables(MySqlConnection connection, DateTime cutoffDate, string dbName)
        {
            try
            {
                // 获取所有复制状态表
                var statusTables = new List<string>();
                
                // 基础复制状态表
                statusTables.Add("replication_status");
                
                // 主库到从库的状态表
                statusTables.Add($"replication_status_{_followerServerId}");
                
                // 从库到主库的状态表
                statusTables.Add($"replication_status_{_followerServerId}_to_leader");
                
                foreach (var tableName in statusTables)
                {
                    try
                    {
                        // 检查表是否存在
                        var checkTableSql = $"SELECT COUNT(*) FROM information_schema.tables WHERE table_name = '{tableName}' AND table_schema = DATABASE()";
                        using var checkCommand = new MySqlCommand(checkTableSql, connection);
                        var tableExists = Convert.ToInt32(await checkCommand.ExecuteScalarAsync()) > 0;
                        
                        if (tableExists)
                        {
                            // 删除与过期日志相关的状态记录
                            var deleteStatusSql = $@"
                                DELETE s FROM {tableName} s
                                LEFT JOIN replication_logs l ON s.log_entry_id = l.id
                                WHERE l.id IS NULL OR l.timestamp < @CutoffDate";
                            
                            using var deleteCommand = new MySqlCommand(deleteStatusSql, connection);
                            deleteCommand.Parameters.AddWithValue("@CutoffDate", cutoffDate);
                            
                            var deletedStatusCount = await deleteCommand.ExecuteNonQueryAsync();
                            if (deletedStatusCount > 0)
                            {
                                _logger.Info($"{dbName} - 已删除 {deletedStatusCount} 条过期的复制状态记录 (表: {tableName})");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Error($"清理状态表 {tableName} 时出错: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"清理 {dbName} 复制状态表时出错: {ex.Message}");
            }
        }

        #region 冲突处理方法

        // 检测冲突
        private async Task<List<DataConflict>> DetectConflicts(TableConfig tableConfig, List<ReplicationLogEntry> changes)
        {
            var conflicts = new List<DataConflict>();
            
            try
            {
                using var followerContext = CreateFollowerDbContext();
                using var leaderContext = CreateLeaderDbContextForRead();
                
                foreach (var change in changes)
                {
                    var conflictingEntries = await GetConflictingEntries(tableConfig, change, followerContext, leaderContext);
                    
                    foreach (var conflictEntry in conflictingEntries)
                    {
                        var conflict = new DataConflict
                        {
                            TableName = tableConfig.TableName,
                            RecordId = change.RecordId,
                            FollowerServer = _followerServerId,
                            SourceEntry = change,
                            TargetEntry = conflictEntry,
                            Type = DetermineConflictType(change, conflictEntry),
                            ConflictingFields = await GetConflictingFields(tableConfig, change, conflictEntry)
                        };
                        
                        conflicts.Add(conflict);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"检测冲突时出错: {ex.Message}");
            }
            
            return conflicts;
        }

        // 获取冲突的条目
        private async Task<List<ReplicationLogEntry>> GetConflictingEntries(
            TableConfig tableConfig, 
            ReplicationLogEntry change, 
            FollowerDbContext followerContext,
            LeaderDbContext leaderContext)
        {
            var conflicts = new List<ReplicationLogEntry>();
            
            // 检查从库是否有未同步的变更
            var followerChanges = await GetPendingChangesFromFollower(followerContext, tableConfig);
            var conflictingFollowerChanges = followerChanges.Where(fc => 
                fc.RecordId == change.RecordId && 
                fc.Timestamp > change.Timestamp.AddSeconds(-30) && // 30秒内的变更认为可能冲突
                fc.Id != change.Id).ToList();
            
            conflicts.AddRange(conflictingFollowerChanges);
            
            //// 检查主库是否有更新的变更
            //var newerLeaderChanges = await GetNewerChangesFromLeader(leaderContext, tableConfig, change);
            //conflicts.AddRange(newerLeaderChanges);
            
            return conflicts;
        }

        // 获取主库更新的变更
        private async Task<List<ReplicationLogEntry>> GetNewerChangesFromLeader(
            LeaderDbContext leaderContext, 
            TableConfig tableConfig, 
            ReplicationLogEntry change)
        {
            return await leaderContext.ReplicationLogs
                .Where(l => l.TableName == tableConfig.TableName &&
                           l.RecordId == change.RecordId &&
                           l.Timestamp > change.Timestamp &&
                           l.Id != change.Id)
                .OrderByDescending(l => l.Timestamp)
                .ToListAsync();
        }

        // 确定冲突类型
        private ConflictType DetermineConflictType(ReplicationLogEntry source, ReplicationLogEntry target)
        {
            if (source.OperationType == ReplicationOperation.Update && target.OperationType == ReplicationOperation.Update)
                return ConflictType.ConcurrentUpdate;
            
            if (source.OperationType == ReplicationOperation.Delete && target.OperationType == ReplicationOperation.Update)
                return ConflictType.DeleteAfterUpdate;
            
            if (source.OperationType == ReplicationOperation.Update && target.OperationType == ReplicationOperation.Delete)
                return ConflictType.UpdateAfterDelete;
            
            if (source.OperationType == ReplicationOperation.Insert && target.OperationType == ReplicationOperation.Insert)
                return ConflictType.DuplicateInsert;
            
            return ConflictType.VersionMismatch;
        }

        // 获取冲突字段
        private async Task<Dictionary<string, object>> GetConflictingFields(
            TableConfig tableConfig, 
            ReplicationLogEntry source, 
            ReplicationLogEntry target)
        {
            var conflictingFields = new Dictionary<string, object>();
            
            try
            {
                // 这里可以实现具体的字段比较逻辑
                // 简化实现：记录时间戳差异
                conflictingFields["source_timestamp"] = source.Timestamp;
                conflictingFields["target_timestamp"] = target.Timestamp;
                conflictingFields["source_operation"] = source.OperationType.ToString();
                conflictingFields["target_operation"] = target.OperationType.ToString();
            }
            catch (Exception ex)
            {
                _logger.Error($"获取冲突字段时出错: {ex.Message}");
            }
            
            return conflictingFields;
        }

        // 解决冲突
        private async Task<ReplicationLogEntry> ResolveConflict(DataConflict conflict, TableConfig tableConfig)
        {
            try
            {
                ReplicationLogEntry resolvedEntry = null;
                
                switch (tableConfig.ConflictStrategy)
                {
                    case ConflictResolutionStrategy.PreferLeader:
                        resolvedEntry = ResolvePreferLeader(conflict);
                        conflict.ResolutionReason = "优先选择主库变更";
                        break;
                        
                    case ConflictResolutionStrategy.PreferFollower:
                        resolvedEntry = ResolvePreferFollower(conflict);
                        conflict.ResolutionReason = "优先选择从库变更";
                        break;
                        
                    case ConflictResolutionStrategy.LastWriteWins:
                        resolvedEntry = ResolveLastWriteWins(conflict, tableConfig);
                        conflict.ResolutionReason = "选择最后写入的变更";
                        break;
                        
                    case ConflictResolutionStrategy.FieldPriority:
                        resolvedEntry = await ResolveFieldPriority(conflict, tableConfig);
                        conflict.ResolutionReason = "基于字段优先级解决";
                        break;
                        
                    case ConflictResolutionStrategy.Custom:
                        resolvedEntry = await ResolveCustomConflict(conflict, tableConfig);
                        conflict.ResolutionReason = "自定义解决策略";
                        break;
                        
                    case ConflictResolutionStrategy.ManualReview:
                        conflict.Resolution = ConflictResolutionResult.RequiresManualReview;
                        conflict.ResolutionReason = "需要人工审核";
                        _logger.Warning($"冲突需要人工审核: 表 {conflict.TableName}, 记录 {conflict.RecordId}");
                        return null;
                        
                    default:
                        throw new NotSupportedException($"不支持的冲突解决策略: {tableConfig.ConflictStrategy}");
                }
                
                if (resolvedEntry != null)
                {
                    conflict.Resolution = ConflictResolutionResult.ResolvedAutomatically;
                    _logger.Info($"冲突已自动解决: 表 {conflict.TableName}, 记录 {conflict.RecordId}, 策略: {tableConfig.ConflictStrategy}");
                }
                else
                {
                    conflict.Resolution = ConflictResolutionResult.Failed;
                    conflict.ResolutionReason = "解决失败";
                }
                
                return resolvedEntry;
            }
            catch (Exception ex)
            {
                _logger.Error($"解决冲突时出错: {ex.Message}");
                conflict.Resolution = ConflictResolutionResult.Failed;
                conflict.ResolutionReason = $"解决时出错: {ex.Message}";
                return null;
            }
        }

        // 优先主库策略
        private ReplicationLogEntry ResolvePreferLeader(DataConflict conflict)
        {
            // 如果源变更来自主库，选择源变更；否则选择目标变更
            return conflict.SourceEntry.Direction == ReplicationDirection.LeaderToFollower ? 
                   conflict.SourceEntry : conflict.TargetEntry;
        }

        // 优先从库策略
        private ReplicationLogEntry ResolvePreferFollower(DataConflict conflict)
        {
            // 如果源变更来自从库，选择源变更；否则选择目标变更
            return conflict.SourceEntry.Direction == ReplicationDirection.FollowerToLeader ? 
                   conflict.SourceEntry : conflict.TargetEntry;
        }

        // 最后写入获胜策略（支持配置的动态字段比较）
        private ReplicationLogEntry ResolveLastWriteWins(DataConflict conflict, TableConfig tableConfig = null)
        {
            try
            {
                // 首先比较时间戳
                if (conflict.SourceEntry.Timestamp != conflict.TargetEntry.Timestamp)
                {
                    return conflict.SourceEntry.Timestamp > conflict.TargetEntry.Timestamp ? 
                           conflict.SourceEntry : conflict.TargetEntry;
                }
                
                // 如果时间戳相同，比较配置的优先级字段
                var sourceData = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(conflict.SourceEntry.Data);
                var targetData = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(conflict.TargetEntry.Data);
                
                // 使用配置的优先级字段，如果没有配置则使用默认字段
                var priorityFields = tableConfig?.ConflictResolutionPriorityFields ?? 
                    new[] { "version", "Version", "row_version", "RowVersion", "updated_at", "UpdatedAt", "modified_time", "ModifiedTime" }.ToList();
                
                foreach (var field in priorityFields)
                {
                    if (sourceData.ContainsKey(field) && targetData.ContainsKey(field))
                    {
                        var sourceValue = sourceData[field];
                        var targetValue = targetData[field];
                        
                        // 尝试比较数值类型的版本
                        if (TryCompareNumericValues(sourceValue, targetValue, out var numericResult))
                        {
                            return numericResult > 0 ? conflict.SourceEntry : conflict.TargetEntry;
                        }
                        
                        // 尝试比较日期时间类型
                        if (TryCompareDateTimeValues(sourceValue, targetValue, out var dateResult))
                        {
                            return dateResult > 0 ? conflict.SourceEntry : conflict.TargetEntry;
                        }
                        
                        // 尝试字符串比较（用于版本号如 "1.0.1" vs "1.0.2"）
                        if (TryCompareStringValues(sourceValue, targetValue, out var stringResult))
                        {
                            return stringResult > 0 ? conflict.SourceEntry : conflict.TargetEntry;
                        }
                    }
                }
                
                // 如果没有找到合适的比较字段，默认选择源条目
                return conflict.SourceEntry;
            }
            catch (Exception ex)
            {
                _logger.Error($"最后写入获胜策略执行时出错: {ex.Message}");
                // 出错时回退到简单的时间戳比较
                return conflict.SourceEntry.Timestamp > conflict.TargetEntry.Timestamp ? 
                       conflict.SourceEntry : conflict.TargetEntry;
            }
        }
        
        // 尝试比较数值类型的值
        private bool TryCompareNumericValues(object sourceValue, object targetValue, out int result)
        {
            result = 0;
            
            try
            {
                if (sourceValue is JsonElement sourceElement && targetValue is JsonElement targetElement)
                {
                    if (sourceElement.ValueKind == JsonValueKind.Number && targetElement.ValueKind == JsonValueKind.Number)
                    {
                        var sourceNum = sourceElement.GetDecimal();
                        var targetNum = targetElement.GetDecimal();
                        result = sourceNum.CompareTo(targetNum);
                        return true;
                    }
                }
                
                // 尝试直接转换为数值
                if (decimal.TryParse(sourceValue?.ToString(), out var sourceDecimal) && 
                    decimal.TryParse(targetValue?.ToString(), out var targetDecimal))
                {
                    result = sourceDecimal.CompareTo(targetDecimal);
                    return true;
                }
            }
            catch
            {
                // 忽略转换错误
            }
            
            return false;
        }
        
        // 尝试比较日期时间类型的值
         private bool TryCompareDateTimeValues(object sourceValue, object targetValue, out int result)
         {
             result = 0;
             
             try
             {
                 if (sourceValue is JsonElement sourceElement && targetValue is JsonElement targetElement)
                 {
                     if (sourceElement.ValueKind == JsonValueKind.String && targetElement.ValueKind == JsonValueKind.String)
                     {
                         if (DateTime.TryParse(sourceElement.GetString(), out var sourceDate) && 
                             DateTime.TryParse(targetElement.GetString(), out var targetDate))
                         {
                             result = sourceDate.CompareTo(targetDate);
                             return true;
                         }
                     }
                 }
                 
                 // 尝试直接转换为日期时间
                 if (DateTime.TryParse(sourceValue?.ToString(), out var sourceDt) && 
                     DateTime.TryParse(targetValue?.ToString(), out var targetDt))
                 {
                     result = sourceDt.CompareTo(targetDt);
                     return true;
                 }
             }
             catch
             {
                 // 忽略转换错误
             }
             
             return false;
         }
         
         // 尝试比较字符串类型的值（支持版本号比较）
         private bool TryCompareStringValues(object sourceValue, object targetValue, out int result)
         {
             result = 0;
             
             try
             {
                 var sourceStr = sourceValue?.ToString();
                 var targetStr = targetValue?.ToString();
                 
                 if (string.IsNullOrEmpty(sourceStr) || string.IsNullOrEmpty(targetStr))
                     return false;
                 
                 // 尝试版本号比较（如 "1.0.1" vs "1.0.2"）
                 if (TryCompareVersionStrings(sourceStr, targetStr, out var versionResult))
                 {
                     result = versionResult;
                     return true;
                 }
                 
                 // 普通字符串比较
                 result = string.Compare(sourceStr, targetStr, StringComparison.OrdinalIgnoreCase);
                 return true;
             }
             catch
             {
                 // 忽略转换错误
             }
             
             return false;
         }
         
         // 尝试版本号字符串比较
         private bool TryCompareVersionStrings(string sourceVersion, string targetVersion, out int result)
         {
             result = 0;
             
             try
             {
                 // 尝试解析为 Version 对象
                 if (Version.TryParse(sourceVersion, out var sourceVer) && 
                     Version.TryParse(targetVersion, out var targetVer))
                 {
                     result = sourceVer.CompareTo(targetVer);
                     return true;
                 }
                 
                 // 尝试按点分割的数字比较
                 var sourceParts = sourceVersion.Split('.').Select(p => int.TryParse(p, out var num) ? num : 0).ToArray();
                 var targetParts = targetVersion.Split('.').Select(p => int.TryParse(p, out var num) ? num : 0).ToArray();
                 
                 var maxLength = Math.Max(sourceParts.Length, targetParts.Length);
                 
                 for (int i = 0; i < maxLength; i++)
                 {
                     var sourcePart = i < sourceParts.Length ? sourceParts[i] : 0;
                     var targetPart = i < targetParts.Length ? targetParts[i] : 0;
                     
                     if (sourcePart != targetPart)
                     {
                         result = sourcePart.CompareTo(targetPart);
                         return true;
                     }
                 }
                 
                 result = 0; // 版本号相同
                 return true;
             }
             catch
             {
                 // 忽略解析错误
             }
             
             return false;
         }

        // 字段优先级策略
        private async Task<ReplicationLogEntry> ResolveFieldPriority(DataConflict conflict, TableConfig tableConfig)
        {
            // 简化实现：基于配置的优先级字段
            if (tableConfig.ConflictResolutionPriorityFields?.Any() == true)
            {
                // 这里可以实现基于特定字段值的优先级判断
                // 当前简化为时间戳比较
                return ResolveLastWriteWins(conflict, tableConfig);
            }
            
            return ResolvePreferLeader(conflict);
        }

        // 自定义冲突解决策略
        private async Task<ReplicationLogEntry> ResolveCustomConflict(DataConflict conflict, TableConfig tableConfig)
        {
            // 这里可以实现自定义的冲突解决逻辑
            // 当前简化为优先主库策略
            return ResolvePreferLeader(conflict);
        }

        // 记录冲突日志
        private async Task LogConflict(DataConflict conflict)
        {
            try
            {
                using var connection = new MySqlConnection(_followerConnectionString);
                await connection.OpenAsync();
                
                // 确保冲突日志表存在
                await CreateConflictLogTableIfNotExists(connection);
                
                var insertSql = @"
                    INSERT INTO conflict_logs 
                    (table_name, record_id, conflict_type, detected_at, resolution, resolution_strategy, details, resolved_by, resolved_at)
                    VALUES 
                    (@TableName, @RecordId, @ConflictType, @DetectedAt, @Resolution, @ResolutionStrategy, @Details, @ResolvedBy, @ResolvedAt)";
                
                using var command = new MySqlCommand(insertSql, connection);
                command.Parameters.AddWithValue("@TableName", conflict.TableName);
                command.Parameters.AddWithValue("@RecordId", conflict.RecordId);
                command.Parameters.AddWithValue("@ConflictType", conflict.Type.ToString());
                command.Parameters.AddWithValue("@DetectedAt", conflict.DetectedAt);
                command.Parameters.AddWithValue("@Resolution", conflict.Resolution.ToString());
                command.Parameters.AddWithValue("@ResolutionStrategy", conflict.ResolutionReason ?? "");
                command.Parameters.AddWithValue("@Details", System.Text.Json.JsonSerializer.Serialize(conflict));
                command.Parameters.AddWithValue("@ResolvedBy", $"System_{_followerServerId}");
                command.Parameters.AddWithValue("@ResolvedAt", DateTime.Now);
                
                await command.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                _logger.Error($"记录冲突日志时出错: {ex.Message}");
            }
        }

        // 创建冲突日志表
        private async Task CreateConflictLogTableIfNotExists(MySqlConnection connection)
        {
            var createTableSql = @"
                CREATE TABLE IF NOT EXISTS conflict_logs (
                    id INT AUTO_INCREMENT PRIMARY KEY,
                    table_name VARCHAR(255) NOT NULL,
                    record_id VARCHAR(255) NOT NULL,
                    conflict_type VARCHAR(50) NOT NULL,
                    detected_at DATETIME NOT NULL,
                    resolution VARCHAR(50) NOT NULL,
                    resolution_strategy TEXT,
                    details JSON,
                    resolved_by VARCHAR(255),
                    resolved_at DATETIME,
                    INDEX idx_table_record (table_name, record_id),
                    INDEX idx_detected_at (detected_at)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4";
            
            using var command = new MySqlCommand(createTableSql, connection);
            await command.ExecuteNonQueryAsync();
        }

        // 检测同步间隙
        private async Task<SynchronizationGapDetection> DetectSynchronizationGaps()
        {
            var detection = new SynchronizationGapDetection();
            
            try
            {
                using var leaderContext = CreateLeaderDbContextForRead();
                using var followerContext = CreateFollowerDbContext();
                
                foreach (var tableConfig in _tableConfigs.Where(t => t.Enabled))
                {
                    var gap = await DetectTableSyncGap(tableConfig, leaderContext, followerContext);
                    if (gap != null)
                    {
                        detection.Gaps.Add(gap);
                        detection.HasGaps = true;
                        detection.MissedOperationsCount += gap.MissedOperations;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"检测同步间隙时出错: {ex.Message}");
            }
            
            return detection;
        }

        // 检测单表同步间隙
        private async Task<SyncGap> DetectTableSyncGap(
            TableConfig tableConfig, 
            LeaderDbContext leaderContext, 
            FollowerDbContext followerContext)
        {
            try
            {
                // 获取最后同步时间
                var lastSyncTime = await GetLastSyncTime(tableConfig, followerContext);
                
                // 检查主库在此时间之后的变更数量
                var missedCount = await leaderContext.ReplicationLogs
                    .CountAsync(l => l.TableName == tableConfig.TableName && 
                                    l.Timestamp > lastSyncTime &&
                                    l.Direction == ReplicationDirection.LeaderToFollower);
                
                if (missedCount > 0)
                {
                    return new SyncGap
                    {
                        TableName = tableConfig.TableName,
                        StartTime = lastSyncTime,
                        EndTime = DateTime.Now,
                        MissedOperations = missedCount,
                        Reason = "网络中断或同步延迟"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"检测表 {tableConfig.TableName} 同步间隙时出错: {ex.Message}");
            }
            
            return null;
        }

        // 获取最后同步时间
        private async Task<DateTime> GetLastSyncTime(TableConfig tableConfig, FollowerDbContext followerContext)
        {
            try
            {
                string statusTableName = $"replication_status_{_followerServerId}";
                
                var connection = (MySqlConnection)followerContext.Database.GetDbConnection();
                await connection.OpenAsync();
                
                var sql = $@"
                    SELECT MAX(l.timestamp) 
                    FROM replication_logs l
                    INNER JOIN {statusTableName} s ON l.id = s.log_entry_id
                    WHERE l.table_name = @TableName AND s.is_synced = 1";
                
                using var command = new MySqlCommand(sql, connection);
                command.Parameters.AddWithValue("@TableName", tableConfig.TableName);
                
                var result = await command.ExecuteScalarAsync();
                return result != DBNull.Value ? Convert.ToDateTime(result) : DateTime.MinValue;
            }
            catch (Exception ex)
            {
                _logger.Error($"获取最后同步时间时出错: {ex.Message}");
                return DateTime.MinValue;
            }
        }

        // 网络恢复处理
        private async Task HandleNetworkRecovery()
        {
            try
            {
                _logger.Info("检测网络恢复，开始同步间隙检测...");
                
                var gapDetection = await DetectSynchronizationGaps();
                
                if (gapDetection.HasGaps)
                {
                    _logger.Warning($"检测到同步间隙，共 {gapDetection.MissedOperationsCount} 个未同步操作");
                    await RecoverFromSynchronizationGap(gapDetection);
                }
                else
                {
                    _logger.Info("未检测到同步间隙，数据同步正常");
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"网络恢复处理时出错: {ex.Message}");
            }
        }

        // 从同步间隙恢复
        private async Task RecoverFromSynchronizationGap(SynchronizationGapDetection gapDetection)
        {
            try
            {
                foreach (var gap in gapDetection.Gaps)
                {
                    _logger.Info($"开始恢复表 {gap.TableName} 的同步间隙，时间范围: {gap.StartTime:yyyy-MM-dd HH:mm:ss} - {gap.EndTime:yyyy-MM-dd HH:mm:ss}");
                    
                    var tableConfig = _tableConfigs.FirstOrDefault(t => t.TableName == gap.TableName);
                    if (tableConfig != null)
                    {
                        // 强制同步该表的所有未同步变更
                        await ForceSyncTable(tableConfig, gap.StartTime);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"从同步间隙恢复时出错: {ex.Message}");
            }
        }

        // 强制同步表
        private async Task ForceSyncTable(TableConfig tableConfig, DateTime fromTime)
        {
            try
            {
                using var leaderReadContext = CreateLeaderDbContextForRead();
                using var followerContext = CreateFollowerDbContext();
                
                // 获取指定时间之后的所有变更
                var missedChanges = await leaderReadContext.ReplicationLogs
                    .Where(l => l.TableName == tableConfig.TableName &&
                               l.Timestamp > fromTime &&
                               l.Direction == ReplicationDirection.LeaderToFollower)
                    .OrderBy(l => l.Timestamp)
                    .ToListAsync();
                
                if (missedChanges.Any())
                {
                    _logger.Info($"开始强制同步表 {tableConfig.TableName} 的 {missedChanges.Count} 个变更");
                    
                    // 分批处理
                    var batches = missedChanges.Chunk(_batchSize);
                    foreach (var batch in batches)
                    {
                        await ApplyChangesToFollower(followerContext, tableConfig, batch.ToList());
                        
                        // 为写操作创建新的上下文
                        using var leaderWriteContext = CreateLeaderDbContext();
                        await MarkChangesAsProcessed(leaderWriteContext, batch.ToList());
                    }
                    
                    _logger.Info($"表 {tableConfig.TableName} 强制同步完成");
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"强制同步表 {tableConfig.TableName} 时出错: {ex.Message}");
            }
        }

        #endregion

        // 检查主库记录是否在从库变更之后有新的变更
        private bool HasConflictWithLeaderChanges(object leaderEntity, ReplicationLogEntry followerLog, TableConfig tableConfig)
        {
            try
            {
                // 查找实体中的时间戳字段（常见的字段名）
                var timestampProperties = new[] { "ModifyDate" };
                
                foreach (var propName in timestampProperties)
                {
                    var timestampProperty = tableConfig.EntityType.GetProperty(propName);
                    if (timestampProperty != null && 
                        (timestampProperty.PropertyType == typeof(DateTime) || 
                         timestampProperty.PropertyType == typeof(DateTime?)))
                    {
                        var leaderTimestamp = timestampProperty.GetValue(leaderEntity) as DateTime?;
                        
                        if (leaderTimestamp.HasValue && leaderTimestamp.Value > followerLog.Timestamp)
                        {
                            _logger.Info($"检测到冲突：主库记录时间戳 {leaderTimestamp.Value:yyyy-MM-dd HH:mm:ss.fff} 晚于从库变更时间 {followerLog.Timestamp:yyyy-MM-dd HH:mm:ss.fff}");
                            return true;
                        }
                        
                        // 找到时间戳字段就返回，不继续查找其他字段
                        return false;
                    }
                }
                
                // 如果没有找到时间戳字段，检查主库的复制日志
                return CheckLeaderReplicationLogForConflict(leaderEntity, followerLog, tableConfig);
            }
            catch (Exception ex)
            {
                _logger.Warning($"检查冲突时出错: {ex.Message}，默认允许更新");
                return false;
            }
        }

        // 通过主库复制日志检查冲突
        private bool CheckLeaderReplicationLogForConflict(object leaderEntity, ReplicationLogEntry followerLog, TableConfig tableConfig)
        {
            try
            {
                var primaryKeyProperty = tableConfig.EntityType.GetProperty(tableConfig.PrimaryKey);
                var primaryKeyValue = primaryKeyProperty.GetValue(leaderEntity)?.ToString();
                
                using (var leaderContext = CreateLeaderDbContextForRead())
                {
                    // 查找主库中该记录在从库变更时间之后的最新变更
                    var latestLeaderLog = leaderContext.ReplicationLogs
                        .Where(l => l.TableName == tableConfig.TableName && 
                               l.RecordId == primaryKeyValue &&
                               l.Timestamp > followerLog.Timestamp &&
                               l.Direction == ReplicationDirection.LeaderToFollower)
                        .OrderByDescending(l => l.Timestamp)
                        .FirstOrDefault();
                        
                    if (latestLeaderLog != null)
                    {
                        _logger.Info($"检测到冲突：主库在 {latestLeaderLog.Timestamp:yyyy-MM-dd HH:mm:ss.fff} 有新变更，晚于从库变更时间 {followerLog.Timestamp:yyyy-MM-dd HH:mm:ss.fff}");
                        return true;
                    }
                }
                
                return false;
            }
            catch (Exception ex)
            {
                _logger.Warning($"通过复制日志检查冲突时出错: {ex.Message}，默认允许更新");
                return false;
            }
        }

        // 复杂去重处理，考虑操作类型优先级和时间顺序
        private List<ReplicationLogEntry> DeduplicateChanges(List<ReplicationLogEntry> changes, string tableName)
        {
            var originalCount = changes.Count;
            
            // 按主键分组进行去重处理
            var deduplicatedChanges = changes
                .GroupBy(c => c.Data) // 按主键分组
                .Select(group => {
                    var groupChanges = group.OrderBy(c => c.Timestamp).ToList();
                    
                    // 如果组内只有一个变更，直接返回
                    if (groupChanges.Count == 1)
                        return groupChanges.First();
                    
                    // 按时间顺序分析操作序列，处理删除后插入的场景
                    var deleteOperations = groupChanges.Where(c => c.OperationType == ReplicationOperation.Delete).ToList();
                    var insertOperations = groupChanges.Where(c => c.OperationType == ReplicationOperation.Insert).ToList();
                    var updateOperations = groupChanges.Where(c => c.OperationType == ReplicationOperation.Update).ToList();
                    
                    // 如果有删除操作，需要检查删除后是否有插入
                    if (deleteOperations.Any())
                    {
                        var latestDelete = deleteOperations.OrderByDescending(c => c.Timestamp).First();
                        
                        // 检查删除后是否有插入操作
                        var insertsAfterDelete = insertOperations.Where(i => i.Timestamp > latestDelete.Timestamp).ToList();
                        if (insertsAfterDelete.Any())
                        {
                            // 删除后有插入，选择最新的插入操作
                            var latestInsertAfterDelete = insertsAfterDelete.OrderByDescending(i => i.Timestamp).First();
                            
                            // 还需要检查插入后是否有更新
                            var updatesAfterInsert = updateOperations.Where(u => u.Timestamp > latestInsertAfterDelete.Timestamp).ToList();
                            if (updatesAfterInsert.Any())
                            {
                                return updatesAfterInsert.OrderByDescending(u => u.Timestamp).First();
                            }
                            
                            return latestInsertAfterDelete;
                        }
                        
                        // 删除后没有插入，返回最新的删除操作
                        return latestDelete;
                    }
                    
                    // 没有删除操作，处理插入和更新操作
                    if (insertOperations.Any() && updateOperations.Any())
                    {
                        // 比较插入和更新操作的时间戳，选择最新的
                        var latestInsert = insertOperations.OrderByDescending(c => c.Timestamp).First();
                        var latestUpdate = updateOperations.OrderByDescending(c => c.Timestamp).First();
                        
                        return latestInsert.Timestamp > latestUpdate.Timestamp ? latestInsert : latestUpdate;
                    }
                    
                    // 只有插入操作或只有更新操作，选择最新的
                    return groupChanges.OrderByDescending(c => c.Timestamp).First();
                })
                .OrderBy(c => c.Timestamp) // 按时间戳重新排序
                .ToList();
            
            if (originalCount != deduplicatedChanges.Count)
            {
                _logger.Info($"表 {tableName} 去重处理：原始变更数量 {originalCount}，去重后数量 {deduplicatedChanges.Count}");
                
                // 记录详细的去重信息
                var groupedOriginal = changes.GroupBy(c => c.Data).Where(g => g.Count() > 1);
                foreach (var group in groupedOriginal)
                {
                    var operations = group.Select(c => $"{c.OperationType}({c.Timestamp:HH:mm:ss.fff})");
                    _logger.Debug($"主键 {group.Key} 的操作去重：{string.Join(", ", operations)} -> 保留最终操作");
                }
            }
            
            return deduplicatedChanges;
        }

        // 手动重试失败的数据
        public async Task<ManualRetryResult> ManualRetryFailedData(string tableName = null)
        {
            var result = new ManualRetryResult();
            
            try
            {
                _logger.Info("开始手动重试失败的数据...");
                
                using var leaderContext = CreateLeaderDbContext();
                
                // 获取失败的数据，按表名过滤（如果指定）
                var failedLogsQuery = leaderContext.ReplicationFailureLogs
                    .Where(f => f.FollowerServerId == _followerServerId);
                    
                if (!string.IsNullOrEmpty(tableName))
                {
                    failedLogsQuery = failedLogsQuery.Where(f => f.TableName == tableName);
                }
                
                var failedLogs = await failedLogsQuery
                    .OrderBy(f => f.Id) // 按ID排序，确保从最早的错误开始
                    .ToListAsync();
                
                if (!failedLogs.Any())
                {
                    _logger.Info("没有找到需要重试的失败数据");
                    result.Success = true;
                    result.Message = "没有找到需要重试的失败数据";
                    result.ProcessedCount = 0;
                    return result;
                }
                
                // 按表名分组处理
                var failedLogsByTable = failedLogs.GroupBy(f => f.TableName);
                
                foreach (var tableGroup in failedLogsByTable)
                {
                    var currentTableName = tableGroup.Key;
                    var tableFailedLogs = tableGroup.OrderBy(f => f.Id).ToList();
                    
                    _logger.Info($"处理表 {currentTableName} 的 {tableFailedLogs.Count} 条失败数据");
                    
                    // 获取最早的失败数据ID
                    var earliestFailedId = tableFailedLogs.First().Id;
                    
                    _logger.Info($"表 {currentTableName} 最早的失败数据ID: {earliestFailedId}");
                    
                    // 将同步进度设置到最早失败数据之前
                    await ResetSyncProgressBeforeFailedData(leaderContext, currentTableName, earliestFailedId);
                    
                    // 删除失败数据对应的复制日志记录
                    await ClearReplicationLogs(leaderContext, tableFailedLogs);
                    
                    // 删除失败数据的记录
                    await ClearFailedDataLogs(leaderContext, tableFailedLogs);
                    
                    result.ProcessedTables.Add(currentTableName, tableFailedLogs.Count);
                    result.ProcessedCount += tableFailedLogs.Count;
                }
                
                await leaderContext.SaveChangesAsync();
                
                result.Success = true;
                result.Message = $"手动重试设置完成，已处理 {result.ProcessedCount} 条失败数据，系统将自动重新同步这些数据";
                _logger.Info(result.Message);
                
                return result;
            }
            catch (Exception ex)
            {
                _logger.Error($"手动重试失败数据时出错: {ex.Message}");
                result.Success = false;
                result.Message = $"手动重试失败: {ex.Message}";
                return result;
            }
        }
        
        // 将同步进度重置到失败数据之前
        private async Task ResetSyncProgressBeforeFailedData(LeaderDbContext leaderContext, string tableName, int earliestFailedId)
        {
            try
            {
                // 将同步进度设置到最早失败数据之前
                var newSyncId = earliestFailedId - 1;
                
                // 确保不会设置为负数
                if (newSyncId < 0)
                {
                    newSyncId = 0;
                }
                
                // 获取当前的同步进度
                var syncProgress = await leaderContext.SyncProgresses
                    .FirstOrDefaultAsync(sp => sp.TableName == tableName && sp.FollowerServerId == _followerServerId);
                
                if (syncProgress != null)
                {
                    var originalSyncId = syncProgress.LastSyncedId;
                    
                    // 更新现有的同步进度记录
                    syncProgress.LastSyncedId = newSyncId;
                    syncProgress.LastSyncTime = DateTime.Now;
                    _logger.Info($"表 {tableName} 更新同步进度记录，从 {originalSyncId} 重置为 {newSyncId}");
                }
                else
                {
                    // 如果不存在同步进度记录，则创建新的
                    var newSyncProgress = new SyncProgress
                    {
                        TableName = tableName,
                        FollowerServerId = _followerServerId,
                        LastSyncedId = newSyncId,
                        LastSyncTime = DateTime.Now
                    };
                    
                    leaderContext.SyncProgresses.Add(newSyncProgress);
                    _logger.Info($"表 {tableName} 创建新的同步进度记录，设置为 {newSyncId}");
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"重置表 {tableName} 同步进度时出错: {ex.Message}");
                throw;
            }
        }
        
        // 清理失败数据对应的复制日志记录
        private async Task ClearReplicationLogs(LeaderDbContext leaderContext, List<ReplicationFailureLog> failedLogs)
        {
            try
            {
                // 提取失败日志中的复制日志ID
                var replicationLogIds = failedLogs
                    .Select(fl => fl.Id)
                    .Distinct()
                    .ToList();
                
                if (replicationLogIds.Any())
                {
                    // 删除对应的复制日志记录
                    var replicationLogsToDelete = await leaderContext.ReplicationLogs
                        .Where(rl => replicationLogIds.Contains(rl.Id))
                        .ToListAsync();
                    
                    if (replicationLogsToDelete.Any())
                    {
                        leaderContext.ReplicationLogs.RemoveRange(replicationLogsToDelete);
                        _logger.Info($"已清理 {replicationLogsToDelete.Count} 条失败数据对应的复制日志记录");
                        
                        // 记录清理的详细信息
                        foreach (var log in replicationLogsToDelete.Take(5)) // 只记录前5条详细信息
                        {
                            _logger.Debug($"清理复制日志: 表={log.TableName}, ID={log.Id}, 操作={log.OperationType}, 时间={log.Timestamp}");
                        }
                        
                        if (replicationLogsToDelete.Count > 5)
                        {
                            _logger.Debug($"... 还有 {replicationLogsToDelete.Count - 5} 条记录被清理");
                        }
                    }
                    else
                    {
                        _logger.Info($"没有找到对应的复制日志记录需要清理");
                    }
                }
                else
                {
                    _logger.Info($"失败日志中没有关联的复制日志ID，跳过复制日志清理");
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"清理失败数据对应的复制日志时出错: {ex.Message}");
                throw;
            }
        }
        
        // 清理失败数据的日志记录
        private async Task ClearFailedDataLogs(LeaderDbContext leaderContext, List<ReplicationFailureLog> failedLogs)
        {
            try
            {
                // 删除失败日志记录
                leaderContext.ReplicationFailureLogs.RemoveRange(failedLogs);
                
                _logger.Info($"已清理 {failedLogs.Count} 条失败数据日志记录");
                
                // 记录清理的详细信息
                foreach (var log in failedLogs)
                {
                    _logger.Debug($"清理失败日志: 表={log.TableName}, ID={log.Id}, 操作={log.OperationType}, 错误={log.ErrorMessage}");
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"清理失败数据日志时出错: {ex.Message}");
                throw;
            }
        }
        
        // 获取失败数据的统计信息
        public async Task<Dictionary<string, int>> GetFailedDataStatistics()
        {
            try
            {
                using var leaderContext = CreateLeaderDbContext();
                
                var statistics = await leaderContext.ReplicationFailureLogs
                    .Where(f => f.FollowerServerId == _followerServerId)
                    .GroupBy(f => f.TableName)
                    .Select(g => new { TableName = g.Key, Count = g.Count() })
                    .ToDictionaryAsync(x => x.TableName, x => x.Count);
                
                return statistics;
            }
            catch (Exception ex)
            {
                _logger.Error($"获取失败数据统计信息时出错: {ex.Message}");
                return new Dictionary<string, int>();
            }
        }

        // 暂停指定表的复制
        public Task PauseTableReplication(string tableName)
        {
            try
            {
                var timerKey = $"LeaderToFollower_{tableName}";
                if (_timers.ContainsKey(timerKey))
                {
                    _timers[timerKey]?.Dispose();
                    _timers.Remove(timerKey);
                    _logger.Info($"已暂停表 {tableName} 的主库到从库复制");
                }
                
                var followerToLeaderTimerKey = $"FollowerToLeader_{tableName}";
                if (_timers.ContainsKey(followerToLeaderTimerKey))
                {
                    _timers[followerToLeaderTimerKey]?.Dispose();
                    _timers.Remove(followerToLeaderTimerKey);
                    _logger.Info($"已暂停表 {tableName} 的从库到主库复制");
                }
                
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.Error($"暂停表 {tableName} 复制时出错: {ex.Message}");
                throw;
            }
        }

        // 恢复指定表的复制
        public Task ResumeTableReplication(string tableName)
        {
            try
            {
                var tableConfig = _tableConfigs.FirstOrDefault(t => t.TableName == tableName && t.Enabled);
                if (tableConfig == null)
                {
                    _logger.Warning($"找不到表 {tableName} 的配置或该表未启用");
                    return Task.CompletedTask;
                }
                
                // 恢复主库到从库的同步计时器
                if (tableConfig.ReplicationDirection == ReplicationDirection.Bidirectional ||
                    tableConfig.ReplicationDirection == ReplicationDirection.LeaderToFollower)
                {
                    var timerKey = $"LeaderToFollower_{tableConfig.TableName}";
                    if (!_timers.ContainsKey(timerKey))
                    {
                        _executionFlags[timerKey] = false;
                        
                        var timer = new Timer(
                            async _ => await ExecuteWithFlag(timerKey, () => PullAndApplyChanges(tableConfig)),
                            null,
                            TimeSpan.Zero,
                            TimeSpan.FromSeconds(tableConfig.ReplicationIntervalSeconds));

                        _timers[timerKey] = timer;
                        _logger.Info($"已恢复表 {tableName} 的主库到从库复制");
                    }
                }

                // 恢复从库到主库的同步计时器（双向复制）
                if (tableConfig.ReplicationDirection == ReplicationDirection.Bidirectional ||
                    tableConfig.ReplicationDirection == ReplicationDirection.FollowerToLeader)
                {
                    var timerKey = $"FollowerToLeader_{tableConfig.TableName}";
                    if (!_timers.ContainsKey(timerKey))
                    {
                        _executionFlags[timerKey] = false;
                        
                        var timer = new Timer(
                            async _ => await ExecuteWithFlag(timerKey, () => PushFollowerChangesToLeader(tableConfig)),
                            null,
                            TimeSpan.Zero,
                            TimeSpan.FromSeconds(tableConfig.ReplicationIntervalSeconds));

                        _timers[timerKey] = timer;
                        _logger.Info($"已恢复表 {tableName} 的从库到主库复制");
                    }
                }
                
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.Error($"恢复表 {tableName} 复制时出错: {ex.Message}");
                throw;
            }
        }

        // 手动同步指定表从主库到从库
        public async Task SyncTableFromLeaderToFollower(TableConfig tableConfig)
        {
            try
            {
                _logger.Info($"开始手动同步表 {tableConfig.TableName} 从主库到从库");
                
                // 复用现有的初始化表数据方法，这会清空从库表并完整复制主库数据
                await InitializeTableData(tableConfig);
                
                _logger.Info($"表 {tableConfig.TableName} 手动同步完成");
            }
            catch (Exception ex)
            {
                _logger.Error($"手动同步表 {tableConfig.TableName} 时出错: {ex.Message}");
                throw;
            }
        }


        // 实现IDisposable接口
        public void Dispose()
        {
            StopReplication();
        }

        #region 表结构同步功能

        // 执行初始表结构同步
        private async Task PerformInitialSchemaSync()
        {
            try
            {
                _logger.Info("开始执行初始表结构同步...");
                
                var tablesToSync = _tableConfigs.Where(t => t.Enabled && 
                    t.AllowSchemaChanges && 
                    (t.SchemaSync == SchemaSyncStrategy.OnStartup || 
                     t.SchemaSync == SchemaSyncStrategy.OnStartupAndPeriodic)).ToList();
                
                if (!tablesToSync.Any())
                {
                    _logger.Info("没有需要进行初始表结构同步的表");
                    return;
                }
                
                _logger.Info($"共有 {tablesToSync.Count} 个表需要进行初始表结构同步");
                
                var successCount = 0;
                var failureCount = 0;
                
                foreach (var tableConfig in tablesToSync)
                {
                    try
                    {
                        var result = await SyncTableSchema(tableConfig);
                        if (result.Success)
                        {
                            successCount++;
                            if (result.ExecutedStatements.Any())
                            {
                                _logger.Info($"表 {tableConfig.TableName} 结构同步完成，执行了 {result.ExecutedStatements.Count} 个变更");
                            }
                        }
                        else
                        {
                            failureCount++;
                            _logger.Error($"表 {tableConfig.TableName} 结构同步失败: {result.ErrorMessage}");
                        }
                    }
                    catch (Exception ex)
                    {
                        failureCount++;
                        _logger.Error($"表 {tableConfig.TableName} 结构同步异常: {ex.Message}");
                    }
                }
                
                _logger.Info($"初始表结构同步完成，成功: {successCount}，失败: {failureCount}");
            }
            catch (Exception ex)
            {
                _logger.Error($"执行初始表结构同步时发生异常: {ex.Message}");
            }
        }

        // 同步表结构
        public async Task<SchemaSyncResult> SyncTableSchema(TableConfig tableConfig)
        {
            var result = new SchemaSyncResult
            {
                TableName = tableConfig.TableName,
                Success = false
            };

            var startTime = DateTime.Now;

            try
            {
                _logger.Info($"开始同步表 {tableConfig.TableName} 的结构...");

                // 获取主库和从库的表结构
                var leaderSchema = await GetTableSchema(tableConfig, true);
                var followerSchema = await GetTableSchema(tableConfig, false);

                if (leaderSchema == null)
                {
                    throw new Exception($"无法获取主库表 {tableConfig.TableName} 的结构信息");
                }

                if (followerSchema == null)
                {
                    // 从库表不存在，需要创建
                    _logger.Info($"从库表 {tableConfig.TableName} 不存在，将创建表");
                    var createTableSql = await GenerateCreateTableStatement(leaderSchema);
                    result.ExecutedStatements.Add(createTableSql);
                    await ExecuteSchemaChange(createTableSql, false);
                }
                else
                {
                    // 对比表结构差异
                    var differences = CompareTableSchemas(leaderSchema, followerSchema);
                    result.AppliedDifferences = differences;

                    if (differences.HasDifferences)
                    {
                        _logger.Info($"检测到表 {tableConfig.TableName} 结构差异，开始应用变更...");
                        
                        // 生成并执行ALTER语句
                        var alterStatements = GenerateAlterTableStatements(differences);
                        result.ExecutedStatements.AddRange(alterStatements);

                        foreach (var statement in alterStatements)
                        {
                            await ExecuteSchemaChange(statement, false);
                        }
                    }
                    else
                    {
                        _logger.Info($"表 {tableConfig.TableName} 结构无差异，无需同步");
                    }
                }

                result.Success = true;
                _logger.Info($"表 {tableConfig.TableName} 结构同步完成");
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
                _logger.Error($"表 {tableConfig.TableName} 结构同步失败: {ex.Message}");
            }
            finally
            {
                result.Duration = DateTime.Now - startTime;
            }

            return result;
        }

        // 获取表结构信息
        private async Task<TableSchema> GetTableSchema(TableConfig tableConfig, bool isLeader)
        {
            return await GetTableSchemaFromDatabase(tableConfig.TableName, isLeader);
        }

        // 从实体类获取表结构
        private TableSchema GetTableSchemaFromEntity(Type entityType, string tableName)
        {
            var schema = new TableSchema
            {
                TableName = tableName
            };

            var properties = entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            var ordinalPosition = 1;

            foreach (var property in properties)
            {
                // 跳过导航属性
                if (property.PropertyType.IsClass && property.PropertyType != typeof(string) && 
                    !property.PropertyType.IsValueType && Nullable.GetUnderlyingType(property.PropertyType) == null)
                {
                    continue;
                }

                var column = new ColumnInfo
                {
                    ColumnName = property.Name,
                    OrdinalPosition = ordinalPosition++,
                    IsNullable = IsNullableProperty(property)
                };

                // 映射.NET类型到MySQL类型
                column.DataType = MapDotNetTypeToMySql(property.PropertyType);
                column.FullDataType = GetFullDataType(property.PropertyType, column.DataType);

                schema.Columns.Add(column);
            }

            return schema;
        }

        // 从数据库获取表结构
        private async Task<TableSchema> GetTableSchemaFromDatabase(string tableName, bool isLeader)
        {
            var connectionString = isLeader ? _leaderConnectionString : _followerConnectionString;
            
            try
            {
                using var connection = new MySqlConnection(connectionString);
                await connection.OpenAsync();

                var schema = new TableSchema
                {
                    TableName = tableName
                };

                // 检查表是否存在
                var tableExistsQuery = @"
                    SELECT COUNT(*) 
                    FROM information_schema.TABLES 
                    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @TableName";

                using var tableExistsCmd = new MySqlCommand(tableExistsQuery, connection);
                tableExistsCmd.Parameters.AddWithValue("@TableName", tableName);
                var tableExists = Convert.ToInt32(await tableExistsCmd.ExecuteScalarAsync()) > 0;

                if (!tableExists)
                {
                    return null;
                }

                // 获取列信息
                var columnsQuery = @"
                    SELECT 
                        COLUMN_NAME,
                        DATA_TYPE,
                        COLUMN_TYPE,
                        IS_NULLABLE,
                        COLUMN_DEFAULT,
                        EXTRA,
                        COLUMN_COMMENT,
                        CHARACTER_MAXIMUM_LENGTH,
                        NUMERIC_PRECISION,
                        NUMERIC_SCALE,
                        ORDINAL_POSITION
                    FROM information_schema.COLUMNS 
                    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @TableName
                    ORDER BY ORDINAL_POSITION";

                using var columnsCmd = new MySqlCommand(columnsQuery, connection);
                columnsCmd.Parameters.AddWithValue("@TableName", tableName);

                using var reader = await columnsCmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var column = new ColumnInfo
                    {
                        ColumnName = reader.GetString("COLUMN_NAME"),
                        DataType = reader.GetString("DATA_TYPE"),
                        FullDataType = reader.GetString("COLUMN_TYPE"),
                        IsNullable = reader.GetString("IS_NULLABLE") == "YES",
                        DefaultValue = reader.IsDBNull("COLUMN_DEFAULT") ? null : reader.GetString("COLUMN_DEFAULT"),
                        IsAutoIncrement = reader.GetString("EXTRA").Contains("auto_increment"),
                        Comment = reader.IsDBNull("COLUMN_COMMENT") ? null : reader.GetString("COLUMN_COMMENT"),
                        MaxLength = reader.IsDBNull("CHARACTER_MAXIMUM_LENGTH") ? null : reader.GetInt32("CHARACTER_MAXIMUM_LENGTH"),
                        NumericPrecision = reader.IsDBNull("NUMERIC_PRECISION") ? null : reader.GetInt32("NUMERIC_PRECISION"),
                        NumericScale = reader.IsDBNull("NUMERIC_SCALE") ? null : reader.GetInt32("NUMERIC_SCALE"),
                        OrdinalPosition = reader.GetInt32("ORDINAL_POSITION")
                    };
                    schema.Columns.Add(column);
                }
                reader.Close();

                //// 获取索引信息
                //var indexesQuery = @"
                //    SELECT 
                //        INDEX_NAME,
                //        COLUMN_NAME,
                //        NON_UNIQUE,
                //        INDEX_TYPE
                //    FROM information_schema.STATISTICS 
                //    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @TableName
                //    ORDER BY INDEX_NAME, SEQ_IN_INDEX";

                //using var indexesCmd = new MySqlCommand(indexesQuery, connection);
                //indexesCmd.Parameters.AddWithValue("@TableName", tableName);

                //var indexDict = new Dictionary<string, IndexInfo>();
                //using var indexReader = await indexesCmd.ExecuteReaderAsync();
                //while (await indexReader.ReadAsync())
                //{
                //    var indexName = indexReader.GetString("INDEX_NAME");
                //    var columnName = indexReader.GetString("COLUMN_NAME");
                //    var nonUnique = indexReader.GetInt32("NON_UNIQUE");
                //    var indexType = indexReader.GetString("INDEX_TYPE");

                //    if (!indexDict.ContainsKey(indexName))
                //    {
                //        indexDict[indexName] = new IndexInfo
                //        {
                //            IndexName = indexName,
                //            IsUnique = nonUnique == 0,
                //            IsPrimary = indexName == "PRIMARY",
                //            IndexType = indexType
                //        };
                //    }
                //    indexDict[indexName].ColumnNames.Add(columnName);
                //}
                //indexReader.Close();

                //schema.Indexes.AddRange(indexDict.Values);

                // 设置主键
                var primaryIndex = schema.Indexes.FirstOrDefault(i => i.IsPrimary);
                if (primaryIndex != null && primaryIndex.ColumnNames.Any())
                {
                    schema.PrimaryKey = string.Join(",", primaryIndex.ColumnNames);
                }

                return schema;
            }
            catch (Exception ex)
            {
                _logger.Error($"获取表 {tableName} 结构信息失败: {ex.Message}");
                throw;
            }
        }

        // 对比表结构
        private TableSchemaDifference CompareTableSchemas(TableSchema leaderSchema, TableSchema followerSchema)
        {
            var differences = new TableSchemaDifference
            {
                TableName = leaderSchema.TableName
            };

            // 对比列
            var leaderColumns = leaderSchema.Columns.ToDictionary(c => c.ColumnName, c => c);
            var followerColumns = followerSchema.Columns.ToDictionary(c => c.ColumnName, c => c);

            // 需要添加的列
            foreach (var leaderColumn in leaderColumns.Values)
            {
                if (!followerColumns.ContainsKey(leaderColumn.ColumnName))
                {
                    differences.ColumnsToAdd.Add(leaderColumn);
                }
            }

            // 需要删除的列
            foreach (var followerColumn in followerColumns.Values)
            {
                if (!leaderColumns.ContainsKey(followerColumn.ColumnName))
                {
                    differences.ColumnsToDrop.Add(followerColumn.ColumnName);
                }
            }

            // 需要修改的列
            foreach (var leaderColumn in leaderColumns.Values)
            {
                if (followerColumns.TryGetValue(leaderColumn.ColumnName, out var followerColumn))
                {
                    if (!AreColumnsEqual(leaderColumn, followerColumn))
                    {
                        differences.ColumnsToModify.Add(leaderColumn);
                    }
                }
            }

            // 对比索引
            var leaderIndexes = leaderSchema.Indexes.Where(i => !i.IsPrimary).ToDictionary(i => i.IndexName, i => i);
            var followerIndexes = followerSchema.Indexes.Where(i => !i.IsPrimary).ToDictionary(i => i.IndexName, i => i);

            // 需要添加的索引
            foreach (var leaderIndex in leaderIndexes.Values)
            {
                if (!followerIndexes.ContainsKey(leaderIndex.IndexName))
                {
                    differences.IndexesToAdd.Add(leaderIndex);
                }
            }

            // 需要删除的索引
            foreach (var followerIndex in followerIndexes.Values)
            {
                if (!leaderIndexes.ContainsKey(followerIndex.IndexName))
                {
                    differences.IndexesToDrop.Add(followerIndex.IndexName);
                }
            }

            return differences;
        }

        // 判断两个列是否相等
        private bool AreColumnsEqual(ColumnInfo column1, ColumnInfo column2)
        {
            return NormalizeDataType(column1.FullDataType).Equals(NormalizeDataType(column2.FullDataType), StringComparison.OrdinalIgnoreCase) &&
                   column1.IsNullable == column2.IsNullable &&
                   column1.DefaultValue == column2.DefaultValue &&
                   column1.IsAutoIncrement == column2.IsAutoIncrement;
        }

        // 标准化数据类型，移除显示宽度等差异
        private string NormalizeDataType(string dataType)
        {
            if (string.IsNullOrEmpty(dataType))
                return dataType;

            // 移除整数类型的显示宽度
            dataType = System.Text.RegularExpressions.Regex.Replace(dataType, @"\b(tinyint|smallint|mediumint|int|bigint)\(\d+\)", "$1", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            
            // 移除 unsigned 和 zerofill 等修饰符进行比较（如果需要的话）
            // dataType = System.Text.RegularExpressions.Regex.Replace(dataType, @"\s+(unsigned|zerofill)", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            
            return dataType.Trim();
        }

        // 生成CREATE TABLE语句
        private async Task<string> GenerateCreateTableStatement(TableSchema schema)
        {
            var sql = new System.Text.StringBuilder();
            sql.AppendLine($"CREATE TABLE `{schema.TableName}` (");

            // 添加列定义
            var columnDefinitions = new List<string>();
            foreach (var column in schema.Columns.OrderBy(c => c.OrdinalPosition))
            {
                var columnDef = $"  `{column.ColumnName}` {column.FullDataType}";
                
                if (!column.IsNullable)
                {
                    columnDef += " NOT NULL";
                }
                
                if (!string.IsNullOrEmpty(column.DefaultValue))
                {
                    // 为字符串类型的默认值添加引号
                    var defaultValue = column.DefaultValue;
                    if (IsStringType(column.FullDataType) && !defaultValue.StartsWith("'") && !defaultValue.EndsWith("'"))
                    {
                        defaultValue = $"'{defaultValue}'";
                    }
                    columnDef += $" DEFAULT {defaultValue}";
                }
                
                if (column.IsAutoIncrement)
                {
                    columnDef += " AUTO_INCREMENT";
                }
                
                if (!string.IsNullOrEmpty(column.Comment))
                {
                    columnDef += $" COMMENT '{column.Comment.Replace("'", "\\'")}'";
                }
                
                columnDefinitions.Add(columnDef);
            }

            sql.AppendLine(string.Join(",\n", columnDefinitions));

            // 添加主键
            if (!string.IsNullOrEmpty(schema.PrimaryKey))
            {
                sql.AppendLine($",  PRIMARY KEY (`{schema.PrimaryKey.Replace(",", "`,`")}`)");
            }

            // 添加索引
            foreach (var index in schema.Indexes.Where(i => !i.IsPrimary))
            {
                var indexType = index.IsUnique ? "UNIQUE KEY" : "KEY";
                var columnList = "`" + string.Join("`,`", index.ColumnNames) + "`";
                sql.AppendLine($",  {indexType} `{index.IndexName}` ({columnList})");
            }

            sql.AppendLine(") ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;");

            return sql.ToString();
        }

        // 生成ALTER TABLE语句
        private List<string> GenerateAlterTableStatements(TableSchemaDifference differences)
        {
            var statements = new List<string>();

            // 删除索引（必须在修改列之前）
            foreach (var indexName in differences.IndexesToDrop)
            {
                statements.Add($"ALTER TABLE `{differences.TableName}` DROP INDEX `{indexName}`;");
            }

            // 添加列
            foreach (var column in differences.ColumnsToAdd)
            {
                var columnDef = $"`{column.ColumnName}` {column.FullDataType}";
                
                if (!column.IsNullable)
                {
                    columnDef += " NOT NULL";
                }
                
                if (!string.IsNullOrEmpty(column.DefaultValue))
                {
                    // 为字符串类型的默认值添加引号
                    var defaultValue = column.DefaultValue;
                    if (IsStringType(column.FullDataType) && !defaultValue.StartsWith("'") && !defaultValue.EndsWith("'"))
                    {
                        defaultValue = $"'{defaultValue}'";
                    }
                    columnDef += $" DEFAULT {defaultValue}";
                }
                
                if (column.IsAutoIncrement)
                {
                    columnDef += " AUTO_INCREMENT";
                }
                
                if (!string.IsNullOrEmpty(column.Comment))
                {
                    columnDef += $" COMMENT '{column.Comment.Replace("'", "\\'")}'";
                }
                
                statements.Add($"ALTER TABLE `{differences.TableName}` ADD COLUMN {columnDef};");
            }

            // 修改列
            foreach (var column in differences.ColumnsToModify)
            {
                var columnDef = $"`{column.ColumnName}` {column.FullDataType}";
                
                if (!column.IsNullable)
                {
                    columnDef += " NOT NULL";
                }
                
                if (!string.IsNullOrEmpty(column.DefaultValue))
                {
                    // 为字符串类型的默认值添加引号
                    var defaultValue = column.DefaultValue;
                    if (IsStringType(column.FullDataType) && !defaultValue.StartsWith("'") && !defaultValue.EndsWith("'"))
                    {
                        defaultValue = $"'{defaultValue}'";
                    }
                    columnDef += $" DEFAULT {defaultValue}";
                }
                
                if (column.IsAutoIncrement)
                {
                    columnDef += " AUTO_INCREMENT";
                }
                
                if (!string.IsNullOrEmpty(column.Comment))
                {
                    columnDef += $" COMMENT '{column.Comment.Replace("'", "\\'")}'";
                }
                
                statements.Add($"ALTER TABLE `{differences.TableName}` MODIFY COLUMN {columnDef};");
            }

            // 删除列
            foreach (var columnName in differences.ColumnsToDrop)
            {
                statements.Add($"ALTER TABLE `{differences.TableName}` DROP COLUMN `{columnName}`;");
            }

            // 添加索引
            foreach (var index in differences.IndexesToAdd)
            {
                var indexType = index.IsUnique ? "UNIQUE INDEX" : "INDEX";
                var columnList = "`" + string.Join("`,`", index.ColumnNames) + "`";
                statements.Add($"ALTER TABLE `{differences.TableName}` ADD {indexType} `{index.IndexName}` ({columnList});");
            }

            return statements;
        }

        // 执行表结构变更
        private async Task ExecuteSchemaChange(string sql, bool isLeader)
        {
            var connectionString = isLeader ? _leaderConnectionString : _followerConnectionString;
            
            try
            {
                using var connection = new MySqlConnection(connectionString);
                await connection.OpenAsync();
                
                using var command = new MySqlCommand(sql, connection);
                await command.ExecuteNonQueryAsync();
                
                _logger.Info($"执行表结构变更成功: {sql.Substring(0, Math.Min(100, sql.Length))}...");
            }
            catch (Exception ex)
            {
                _logger.Error($"执行表结构变更失败: {sql}\n错误: {ex.Message}");
                throw;
            }
        }

        // 判断是否为字符串类型
        private bool IsStringType(string dataType)
        {
            if (string.IsNullOrEmpty(dataType))
                return false;
                
            var lowerType = dataType.ToLower();
            return lowerType.StartsWith("varchar") || 
                   lowerType.StartsWith("char") || 
                   lowerType.StartsWith("text") || 
                   lowerType.StartsWith("longtext") || 
                   lowerType.StartsWith("mediumtext") || 
                   lowerType.StartsWith("tinytext");
        }

        // 判断属性是否可空
        private bool IsNullableProperty(PropertyInfo property)
        {
            var nullableType = Nullable.GetUnderlyingType(property.PropertyType);
            if (nullableType != null)
            {
                return true;
            }
            
            return !property.PropertyType.IsValueType;
        }

        // 映射.NET类型到MySQL类型
        private string MapDotNetTypeToMySql(Type type)
        {
            var underlyingType = Nullable.GetUnderlyingType(type) ?? type;
            
            return underlyingType.Name switch
            {
                nameof(Int32) => "int",
                nameof(Int64) => "bigint",
                nameof(Int16) => "smallint",
                nameof(Byte) => "tinyint",
                nameof(Boolean) => "tinyint",
                nameof(DateTime) => "datetime",
                nameof(DateTimeOffset) => "datetime",
                nameof(Decimal) => "decimal",
                nameof(Double) => "double",
                nameof(Single) => "float",
                nameof(String) => "varchar",
                nameof(Guid) => "char",
                _ => "varchar"
            };
        }

        // 获取完整的数据类型定义
        private string GetFullDataType(Type type, string baseType)
        {
            var underlyingType = Nullable.GetUnderlyingType(type) ?? type;
            
            return underlyingType.Name switch
            {
                nameof(Int32) => "int(11)",
                nameof(Int64) => "bigint(20)",
                nameof(Int16) => "smallint(6)",
                nameof(Byte) => "tinyint(4)",
                nameof(Boolean) => "tinyint(1)",
                nameof(DateTime) => "datetime",
                nameof(DateTimeOffset) => "datetime",
                nameof(Decimal) => "decimal(18,2)",
                nameof(Double) => "double",
                nameof(Single) => "float",
                nameof(String) => "varchar(255)",
                nameof(Guid) => "char(36)",
                _ => "varchar(255)"
            };
        }

        // 批量同步所有表的结构
        public async Task<List<SchemaSyncResult>> SyncAllTableSchemas()
        {
            var results = new List<SchemaSyncResult>();
            
            foreach (var tableConfig in _tableConfigs.Where(t => t.Enabled && t.AllowSchemaChanges))
            {
                try
                {
                    var result = await SyncTableSchema(tableConfig);
                    results.Add(result);
                }
                catch (Exception ex)
                {
                    results.Add(new SchemaSyncResult
                    {
                        TableName = tableConfig.TableName,
                        Success = false,
                        ErrorMessage = ex.Message
                    });
                }
            }
            
            return results;
        }

        // 手动触发表结构同步
        public async Task<SchemaSyncResult> ManualSyncTableSchema(string tableName)
        {
            var tableConfig = _tableConfigs.FirstOrDefault(t => t.TableName.Equals(tableName, StringComparison.OrdinalIgnoreCase));
            if (tableConfig == null)
            {
                return new SchemaSyncResult
                {
                    TableName = tableName,
                    Success = false,
                    ErrorMessage = $"未找到表配置: {tableName}"
                };
            }

            if (!tableConfig.Enabled)
            {
                return new SchemaSyncResult
                {
                    TableName = tableName,
                    Success = false,
                    ErrorMessage = $"表 {tableName} 的复制功能未启用"
                };
            }

            if (!tableConfig.AllowSchemaChanges)
            {
                return new SchemaSyncResult
                {
                    TableName = tableName,
                    Success = false,
                    ErrorMessage = $"表 {tableName} 不允许结构变更"
                };
            }

            try
            {
                _logger.Info($"手动触发表 {tableName} 的结构同步");
                return await SyncTableSchema(tableConfig);
            }
            catch (Exception ex)
            {
                _logger.Error($"手动同步表 {tableName} 结构时发生异常: {ex.Message}");
                return new SchemaSyncResult
                {
                    TableName = tableName,
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        #endregion
    }
}