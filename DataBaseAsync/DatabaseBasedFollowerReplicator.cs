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

namespace DatabaseReplication.Follower
{
    public class DatabaseBasedFollowerReplicator : IDisposable
    {
        private readonly string _followerServerId;
        private readonly string _followerConnectionString;
        private readonly string _leaderConnectionString;
        private readonly List<TableConfig> _tableConfigs;
        private readonly Dictionary<string, Timer> _timers = new Dictionary<string, Timer>();
        private readonly int _batchSize;
        private readonly int _dataRetentionDays;
        private readonly int _cleanupIntervalHours;
        private bool _isRunning = false;
        private bool _isInitializationMode = false;
        private Timer _cleanupTimer;

        public DatabaseBasedFollowerReplicator(
            string followerServerId,
            string followerConnectionString,
            string leaderConnectionString,
            List<TableConfig> tableConfigs,
            int batchSize = 1000,
            int dataRetentionDays = 30,
            int cleanupIntervalHours = 24)
        {
            _followerServerId = followerServerId;
            _followerConnectionString = followerConnectionString;
            _leaderConnectionString = leaderConnectionString;
            _tableConfigs = tableConfigs;
            _batchSize = batchSize;
            _dataRetentionDays = dataRetentionDays;
            _cleanupIntervalHours = cleanupIntervalHours;
            CreateReplicationStatusTable();
        }

        // 启动复制服务
        public void StartReplication()
        {
            if (_isRunning) return;

            _isRunning = true;

            foreach (var tableConfig in _tableConfigs.Where(t => t.Enabled))
            {
                // 启动主库到从库的同步计时器
                if (tableConfig.ReplicationDirection == ReplicationDirection.Bidirectional ||
                    tableConfig.ReplicationDirection == ReplicationDirection.LeaderToFollower)
                {
                    var timer = new Timer(
                        async _ => await PullAndApplyChanges(tableConfig),
                        null,
                        TimeSpan.Zero,
                        TimeSpan.FromSeconds(tableConfig.ReplicationIntervalSeconds));

                    _timers[$"LeaderToFollower_{tableConfig.TableName}"] = timer;
                }

                // 启动从库到主库的同步计时器（双向复制）
                if (tableConfig.ReplicationDirection == ReplicationDirection.Bidirectional ||
                    tableConfig.ReplicationDirection == ReplicationDirection.FollowerToLeader)
                {
                    var timer = new Timer(
                        async _ => await PushFollowerChangesToLeader(tableConfig),
                        null,
                        TimeSpan.Zero,
                        TimeSpan.FromSeconds(tableConfig.ReplicationIntervalSeconds));

                    _timers[$"FollowerToLeader_{tableConfig.TableName}"] = timer;
                }
            }

            // 启动数据清理定时器
            _cleanupTimer = new Timer(
                async _ => await CleanupOldReplicationLogs(),
                null,
                TimeSpan.Zero, // 立即执行一次
                TimeSpan.FromHours(_cleanupIntervalHours)); // 定期执行

            Console.WriteLine($"从库 {_followerServerId} 复制服务已启动");
            Console.WriteLine($"数据清理任务已启动，保留 {_dataRetentionDays} 天数据，每 {_cleanupIntervalHours} 小时执行一次清理");
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
            
            // 停止清理定时器
            _cleanupTimer?.Dispose();
            _cleanupTimer = null;
            
            Console.WriteLine($"从库 {_followerServerId} 复制服务已停止");
        }

        // 初始化旧数据同步
        public async Task InitializeExistingData(bool parallel = true, int maxConcurrency = 3)
        {
            Console.WriteLine("开始初始化旧数据同步...");
            var startTime = DateTime.Now;

            var enabledTables = _tableConfigs.Where(t => t.Enabled && t.InitializeExistingData).ToList();
            
            if (!enabledTables.Any())
            {
                Console.WriteLine("没有需要初始化的表");
                return;
            }

            Console.WriteLine($"共有 {enabledTables.Count} 个表需要初始化，并发度: {(parallel ? maxConcurrency : 1)}");

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
                                Console.WriteLine($"进度: {completedTables}/{totalTables} ({progress:F1}%) - 表 {tableConfig.TableName} 初始化完成");
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"表 {tableConfig.TableName} 初始化失败: {ex.Message}");
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
                    Console.WriteLine("所有表初始化成功完成");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"部分表初始化失败: {ex.Message}");
                    // 检查哪些任务失败了
                    for (int i = 0; i < tasks.Count; i++)
                    {
                        if (tasks[i].IsFaulted)
                        {
                            Console.WriteLine($"表 {enabledTables[i].TableName} 初始化失败: {tasks[i].Exception?.GetBaseException().Message}");
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
                        Console.WriteLine($"进度: {i + 1}/{enabledTables.Count} ({progress:F1}%) - 表 {tableConfig.TableName} 初始化完成");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"表 {tableConfig.TableName} 初始化失败: {ex.Message}");
                        throw;
                    }
                }
            }

            var duration = DateTime.Now - startTime;
            Console.WriteLine($"旧数据初始化同步完成，总耗时: {duration.TotalMinutes:F2} 分钟");
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
                    Console.WriteLine($"表 {tableConfig.TableName} 初始化失败 (第 {retryCount} 次重试)，{delay.TotalSeconds} 秒后重试: {ex.Message}");
                    await Task.Delay(delay);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"表 {tableConfig.TableName} 初始化最终失败，已重试 {maxRetries} 次: {ex.Message}");
                    throw;
                }
            }
        }

        // 初始化单个表的旧数据
        private async Task InitializeTableData(TableConfig tableConfig)
        {
            Console.WriteLine($"开始初始化表 {tableConfig.TableName} 的旧数据...");

            await ClearTableData(tableConfig);
            await CopyTableData(tableConfig);

            Console.WriteLine($"表 {tableConfig.TableName} 的旧数据初始化完成");
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
                Console.WriteLine($"清空从库 {_followerConnectionString} 中表 {tableConfig.TableName} 的数据...");

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
            Console.WriteLine($"从 {_leaderConnectionString} 复制表 {tableConfig.TableName} 数据到 {_followerConnectionString}...");

            using (var sourceConnection = new MySqlConnection(_leaderConnectionString))
            {
                await sourceConnection.OpenAsync();

                // 获取表结构信息
                var columns = new List<string>();
                await using (var schemaCmd = new MySqlCommand(
                    $"SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @TableName AND TABLE_SCHEMA = DATABASE()",
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

                var columnList = string.Join(", ", columns);

                // 获取总行数
                int totalRows = 0;
                await using (var countCmd = new MySqlCommand($"SELECT COUNT(*) FROM {tableConfig.TableName}", sourceConnection))
                {
                    totalRows = Convert.ToInt32(await countCmd.ExecuteScalarAsync());
                }

                Console.WriteLine($"表 {tableConfig.TableName} 共有 {totalRows} 条记录需要复制");

                int batchSize = 5000;
                var batches = new List<(int offset, int size, int batchNumber)>();
                
                // 计算所有批次
                for (int offset = 0; offset < totalRows; offset += batchSize)
                {
                    int currentBatchSize = Math.Min(batchSize, totalRows - offset);
                    batches.Add((offset, currentBatchSize, batches.Count + 1));
                }

                Console.WriteLine($"表 {tableConfig.TableName} 将分为 {batches.Count} 个批次进行并行复制，并发度: {maxConcurrency}");

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
                                Console.WriteLine($"批次进度: {completedBatches}/{batches.Count} ({progress:F1}%) - 批次 {batchNumber} 完成，复制了 {copiedRows} 条记录");
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"批次 {batchNumber} 复制失败: {ex.Message}");
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
                    Console.WriteLine($"表 {tableConfig.TableName} 所有批次复制完成，共复制 {totalCopiedRows} 条记录");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"表 {tableConfig.TableName} 部分批次复制失败: {ex.Message}");
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
                    Console.WriteLine($"{operationName} 第 {attempt} 次尝试失败: {ex.Message}");
                    
                    if (attempt == maxRetries)
                    {
                        Console.WriteLine($"{operationName} 达到最大重试次数 ({maxRetries})，操作失败");
                        throw;
                    }
                    
                    // 指数退避策略
                    int delay = delayMs * (int)Math.Pow(2, attempt - 1);
                    Console.WriteLine($"等待 {delay}ms 后进行第 {attempt + 1} 次重试...");
                    await Task.Delay(delay);
                }
            }
            
            throw lastException;
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
                    Console.WriteLine($"{operationName} 第 {attempt} 次尝试失败: {ex.Message}");

                    if (attempt == maxRetries)
                    {
                        Console.WriteLine($"{operationName} 达到最大重试次数 ({maxRetries})，操作失败");
                        throw;
                    }

                    // 指数退避策略
                    int delay = delayMs * (int)Math.Pow(2, attempt - 1);
                    Console.WriteLine($"等待 {delay}ms 后进行第 {attempt + 1} 次重试...");
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

                                    // 使用 MySqlBulkLoader 进行批量插入
                                    var bulkLoader = new MySqlBulkLoader(destinationConnection)
                                    {
                                        TableName = tableConfig.TableName,
                                        CharacterSet = "utf8mb4",
                                        NumberOfLinesToSkip = 0,
                                        Timeout = 300, // 5分钟超时
                                        FieldTerminator = ",",
                                        LineTerminator = Environment.NewLine,
                                        FieldQuotationCharacter = '"',
                                        FieldQuotationOptional = false
                                    };

                                    // 添加列映射
                                    foreach (var column in columns)
                                    {
                                        bulkLoader.Columns.Add(column);
                                    }

                                    // 将 DataReader 转换为 CSV 格式并加载
                                    bulkLoader.SourceStream = new DataReaderStream(reader);
                                    var rowsLoaded = await bulkLoader.LoadAsync();
                                    return (int)rowsLoaded;
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
                        string valueStr = value == DBNull.Value ? "" : EscapeCsvValue(value.ToString());
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
                using (var leaderContext = CreateLeaderDbContext())
                using (var followerContext = CreateFollowerDbContext())
                {
                    // 获取主库待同步的变更
                    var pendingChanges = await GetPendingChangesFromLeader(leaderContext, tableConfig);

                    if (pendingChanges.Any())
                    {
                        Console.WriteLine($"从主库拉取了 {pendingChanges.Count} 条表 {tableConfig.TableName} 的变更");

                        // 应用变更到从库
                        await ApplyChangesToFollower(followerContext, tableConfig, pendingChanges);

                        // 标记变更为已处理
                        await MarkChangesAsProcessed(leaderContext, pendingChanges);

                        Console.WriteLine($"成功将 {pendingChanges.Count} 条表 {tableConfig.TableName} 的变更应用到从库");
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
            string statusTableName = $"replication_status_{_followerServerId}";

            var sql = $@"
                SELECT l.* 
                FROM replication_logs l
                LEFT JOIN {statusTableName} s ON l.id = s.log_entry_id
                WHERE l.table_name = @TableName
                  AND l.direction = 0
                  AND (s.is_synced IS NULL OR s.is_synced = 0)
                ORDER BY l.timestamp ASC
                LIMIT @BatchSize";

            var parameters = new List<MySqlParameter>
            {
                new MySqlParameter("@TableName", tableConfig.TableName),
                new MySqlParameter("@BatchSize", _batchSize)
            };

            return await leaderContext.ReplicationLogs
                .FromSqlRaw(sql, parameters.ToArray())
                .ToListAsync();
        }

        // 应用变更到从库（带冲突检测）
        private async Task ApplyChangesToFollower(
            FollowerDbContext followerContext,
            TableConfig tableConfig,
            List<ReplicationLogEntry> changes)
        {
            await ExecuteWithRetry(async () =>
            {
                await using var transaction = await followerContext.Database.BeginTransactionAsync();

                try
                {
                    // 只有双向同步才需要检测冲突
                    if (tableConfig.ReplicationDirection == ReplicationDirection.Bidirectional)
                    {
                        var conflicts = await DetectConflicts(tableConfig, changes);
                        
                        if (conflicts.Any())
                        {
                            Console.WriteLine($"检测到 {conflicts.Count} 个冲突，开始解决冲突...");
                            
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

                    foreach (var change in changes)
                    {
                        // 设置会话变量防止从库触发器递归
                        await followerContext.Database.ExecuteSqlRawAsync("SET @is_replicating = 1");

                        // 通过主键从主库获取完整实体
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

                        await followerContext.Database.ExecuteSqlRawAsync("SET @is_replicating = 0");
                    }

                    await followerContext.SaveChangesAsync();
                    await transaction.CommitAsync();
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    Console.WriteLine($"应用变更到从库时出错: {ex.Message}");
                    throw;
                }
            }, 3, 1000, $"应用变更到从库 - 表 {tableConfig.TableName}");
        }

        // 通用的根据主键查找实体的方法（使用 IQueryable）
        private async Task<object> FindByPrimaryKeyAsync(dynamic dbSet, TableConfig tableConfig, object primaryKeyValue)
        {
            // 使用 IQueryable 方式查询
            IQueryable queryable = dbSet;
            var parameter = Expression.Parameter(tableConfig.EntityType, "x");
            var property = Expression.Property(parameter, tableConfig.PrimaryKey);
            var constant = Expression.Constant(primaryKeyValue);
            var equal = Expression.Equal(property, constant);
            var lambda = Expression.Lambda(equal, parameter);
            
            var whereMethod = typeof(Queryable).GetMethods()
                .Where(m => m.Name == "Where" && m.GetParameters().Length == 2)
                .First()
                .MakeGenericMethod(tableConfig.EntityType);
            
            var filteredQuery = whereMethod.Invoke(null, new object[] { queryable, lambda });
            
            var firstOrDefaultMethod = typeof(Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions)
                .GetMethods()
                .Where(m => m.Name == "FirstOrDefaultAsync" && m.GetParameters().Length == 2)
                .First()
                .MakeGenericMethod(tableConfig.EntityType);
            var data= await (dynamic)firstOrDefaultMethod.Invoke(null, new object[] { filteredQuery, CancellationToken.None });
            return data;
        }

        // 从主库获取实体
        private async Task<object> GetEntityFromLeader(TableConfig tableConfig, string primaryKeyValue)
        {
            using (var leaderContext = CreateLeaderDbContext())
            {
                var setMethod = typeof(DbContext).GetMethod("Set", Type.EmptyTypes);
                var genericSetMethod = setMethod.MakeGenericMethod(tableConfig.EntityType);
                dynamic dbSet = genericSetMethod.Invoke(leaderContext, null);
                var primaryKeyProperty = tableConfig.EntityType.GetProperty(tableConfig.PrimaryKey);

                if (primaryKeyProperty == null)
                    throw new InvalidOperationException($"找不到实体 {tableConfig.EntityType.Name} 的主键属性: {tableConfig.PrimaryKey}");

                var convertedValue = Convert.ChangeType(primaryKeyValue, primaryKeyProperty.PropertyType);
                return await FindByPrimaryKeyAsync(dbSet, tableConfig, convertedValue);
            }
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
                    // 检查是否已经被跟踪
                    var trackedInsertEntity = followerContext.Entry(typedEntity).Entity;
                    if (followerContext.Entry(trackedInsertEntity).State == EntityState.Detached)
                    {
                        dbSet.Add(typedEntity);
                    }
                    else
                    {
                        // 实体已被跟踪，检查是否需要更新
                        var existingTracked = await FindByPrimaryKeyAsync(dbSet, tableConfig, primaryKeyValue);
                        if (existingTracked != null)
                        {
                            // 更新已跟踪的实体
                            followerContext.Entry(existingTracked).CurrentValues.SetValues(typedEntity);
                        }
                        else
                        {
                            dbSet.Add(typedEntity);
                        }
                    }
                    Console.WriteLine($"在从库插入表 {tableConfig.TableName} 记录 {log.Data}");
                    break;

                case ReplicationOperation.Update:
                    // 检查目标记录是否存在
                    var existingEntity = await FindByPrimaryKeyAsync(dbSet, tableConfig, primaryKeyValue);
                    
                    if (existingEntity != null)
                    {
                        // 记录存在，更新已跟踪的实体
                        followerContext.Entry(existingEntity).CurrentValues.SetValues(typedEntity);

                        // 特殊处理：如果是自增主键，确保不更新主键值
                        if (IsAutoIncrementPrimaryKey(tableConfig))
                        {
                            followerContext.Entry(existingEntity).Property(tableConfig.PrimaryKey).IsModified = false;
                        }

                        Console.WriteLine($"在从库更新表 {tableConfig.TableName} 记录 {log.Data}");
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
                        Console.WriteLine($"在从库中记录不存在，将更新操作转换为插入操作 - 表 {tableConfig.TableName} 记录 {log.Data}");
                    }
                    break;

                case ReplicationOperation.Delete:
                    // 对于删除操作，先查找已跟踪的实体
                    var entityToDelete = await FindByPrimaryKeyAsync(dbSet, tableConfig, primaryKeyValue);
                    if (entityToDelete != null)
                    {
                        dbSet.Remove(entityToDelete);
                        Console.WriteLine($"在从库删除表 {tableConfig.TableName} 记录 {log.Data}");
                    }
                    else
                    {
                        // 如果实体不存在，创建一个只包含主键的实体进行删除
                        var deleteEntity = Activator.CreateInstance(tableConfig.EntityType);
                        primaryKeyProperty.SetValue(deleteEntity, primaryKeyValue);
                        followerContext.Entry(deleteEntity).State = EntityState.Deleted;
                        Console.WriteLine($"在从库删除表 {tableConfig.TableName} 记录 {log.Data}（实体不存在，创建删除标记）");
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

            // 创建一个只包含主键的实体实例
            var entity = Activator.CreateInstance(tableConfig.EntityType);
            primaryKeyProperty.SetValue(entity, convertedValue);

            // 标记为删除状态
            followerContext.Entry(entity).State = EntityState.Deleted;
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
                    FROM INFORMATION_SCHEMA.COLUMNS 
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
                                command.Parameters.AddWithValue("@SyncTime", DateTime.UtcNow);
                                await command.ExecuteNonQueryAsync();
                            }
                        }

                        await transaction.CommitAsync();
                    }
                    catch (Exception ex)
                    {
                        await transaction.RollbackAsync();
                        Console.WriteLine($"标记变更为已处理时出错: {ex.Message}");
                        throw;
                    }
                }
            }, 3, 1000, "标记变更为已处理");
        }

        // 从库向主库同步变更
        private async Task PushFollowerChangesToLeader(TableConfig tableConfig)
        {
            try
            {
                using (var leaderContext = CreateLeaderDbContext())
                using (var followerContext = CreateFollowerDbContext())
                {
                    // 获取从库待同步的变更
                    var pendingChanges = await GetPendingChangesFromFollower(followerContext, tableConfig);

                    if (pendingChanges.Any())
                    {
                        Console.WriteLine($"从从库拉取了 {pendingChanges.Count} 条表 {tableConfig.TableName} 的变更，准备推送到主库");

                        // 应用变更到主库
                        await ApplyChangesToLeader(leaderContext, tableConfig, pendingChanges);

                        // 标记变更为已处理
                        await MarkFollowerChangesAsProcessed(followerContext, pendingChanges);

                        Console.WriteLine($"成功将 {pendingChanges.Count} 条表 {tableConfig.TableName} 的变更推送到主库");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"推送从库变更到主库时出错: {ex.Message}");
            }
        }

        // 获取从库待同步的变更
        private async Task<List<ReplicationLogEntry>> GetPendingChangesFromFollower(
            FollowerDbContext followerContext,
            TableConfig tableConfig)
        {
            string statusTableName = $"replication_status_{_followerServerId}_to_leader";

            var sql = $@"
                SELECT l.* 
                FROM replication_logs l
                LEFT JOIN {statusTableName} s ON l.id = s.log_entry_id
                WHERE l.table_name = @TableName
                  AND l.direction = 1
                  AND (s.is_synced IS NULL OR s.is_synced = 0)
                ORDER BY l.timestamp ASC";

            return await followerContext.ReplicationLogs
                .FromSqlRaw(sql, new MySqlParameter("@TableName", tableConfig.TableName))
                .ToListAsync();
        }

        // 应用变更到主库
        private async Task ApplyChangesToLeader(
            LeaderDbContext leaderContext,
            TableConfig tableConfig,
            List<ReplicationLogEntry> changes)
        {
            await ExecuteWithRetry(async () =>
            {
                await using var transaction = await leaderContext.Database.BeginTransactionAsync();

                try
                {
                    foreach (var change in changes)
                    {
                        // 设置会话变量防止主库触发器递归
                        await leaderContext.Database.ExecuteSqlRawAsync("SET @is_replicating = 1");

                        // 通过主键从从库获取完整实体
                        var entity = await GetEntityFromFollower(tableConfig, change.Data);

                        if (entity != null)
                        {
                            // 应用变更到主库
                            await ApplyEntityChangeToLeader(leaderContext, tableConfig, change, entity);
                        }
                        else if (change.OperationType == ReplicationOperation.Delete)
                        {
                            // 对于删除操作，如果实体不存在，直接执行删除
                            await DeleteEntityInLeader(leaderContext, tableConfig, change.Data);
                        }

                        // 记录主库已处理的变更
                        leaderContext.ReplicationLogs.Add(new ReplicationLogEntry
                        {
                            TableName = change.TableName,
                            OperationType = change.OperationType,
                            RecordId = change.RecordId,
                            Data = change.Data,
                            Timestamp = DateTime.UtcNow,
                            Processed = true,
                            Direction =ReplicationDirection.FollowerToLeader,
                            SourceServer = _followerServerId,
                            OperationId = Guid.NewGuid()
                        });

                        await leaderContext.Database.ExecuteSqlRawAsync("SET @is_replicating = 0");
                    }

                    await leaderContext.SaveChangesAsync();
                    await transaction.CommitAsync();
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    Console.WriteLine($"应用变更到主库时出错: {ex.Message}");
                    throw;
                }
            }, 3, 1000, $"应用变更到主库 - 表 {tableConfig.TableName}");
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
                return await FindByPrimaryKeyAsync(dbSet, tableConfig, convertedValue);
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
            var typedEntity = ConvertToTypedEntity(entity, tableConfig.EntityType);
            var primaryKeyProperty = tableConfig.EntityType.GetProperty(tableConfig.PrimaryKey);
            var primaryKeyValue = primaryKeyProperty.GetValue(typedEntity);

            switch (log.OperationType)
            {
                case ReplicationOperation.Insert:
                    // 检查是否已经被跟踪
                    var trackedInsertEntity = leaderContext.Entry(typedEntity).Entity;
                    if (leaderContext.Entry(trackedInsertEntity).State == EntityState.Detached)
                    {
                        dbSet.Add(typedEntity);
                    }
                    else
                    {
                        // 实体已被跟踪，检查是否需要更新
                        var existingTracked = await FindByPrimaryKeyAsync(dbSet, tableConfig, primaryKeyValue);
                        if (existingTracked != null)
                        {
                            // 更新已跟踪的实体
                            leaderContext.Entry(existingTracked).CurrentValues.SetValues(typedEntity);
                        }
                        else
                        {
                            dbSet.Add(typedEntity);
                        }
                    }
                    Console.WriteLine($"在主库插入表 {tableConfig.TableName} 记录 {log.Data}（来自从库 {_followerServerId}）");
                    break;

                case ReplicationOperation.Update:
                    // 检查目标记录是否存在
                    var existingEntity = await FindByPrimaryKeyAsync(dbSet, tableConfig, primaryKeyValue);
                    
                    if (existingEntity != null)
                    {
                        // 记录存在，更新已跟踪的实体
                        leaderContext.Entry(existingEntity).CurrentValues.SetValues(typedEntity);

                        // 特殊处理：如果是自增主键，确保不更新主键值
                        if (IsAutoIncrementPrimaryKey(tableConfig))
                        {
                            leaderContext.Entry(existingEntity).Property(tableConfig.PrimaryKey).IsModified = false;
                        }

                        Console.WriteLine($"在主库更新表 {tableConfig.TableName} 记录 {log.Data}（来自从库 {_followerServerId}）");
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
                        Console.WriteLine($"在主库中记录不存在，将更新操作转换为插入操作 - 表 {tableConfig.TableName} 记录 {log.Data}（来自从库 {_followerServerId}）");
                    }
                    break;

                case ReplicationOperation.Delete:
                    // 对于删除操作，先查找已跟踪的实体
                    var entityToDelete = await FindByPrimaryKeyAsync(dbSet, tableConfig, primaryKeyValue);
                    if (entityToDelete != null)
                    {
                        dbSet.Remove(entityToDelete);
                        Console.WriteLine($"在主库删除表 {tableConfig.TableName} 记录 {log.Data}（来自从库 {_followerServerId}）");
                    }
                    else
                    {
                        // 如果实体不存在，创建一个只包含主键的实体进行删除
                        var deleteEntity = Activator.CreateInstance(tableConfig.EntityType);
                        primaryKeyProperty.SetValue(deleteEntity, primaryKeyValue);
                        leaderContext.Entry(deleteEntity).State = EntityState.Deleted;
                        Console.WriteLine($"在主库删除表 {tableConfig.TableName} 记录 {log.Data}（实体不存在，创建删除标记）（来自从库 {_followerServerId}）");
                    }
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

        // 标记从库变更为已处理
        private async Task MarkFollowerChangesAsProcessed(
            FollowerDbContext followerContext,
            List<ReplicationLogEntry> changes)
        {
            await ExecuteWithRetry(async () =>
            {
                string statusTableName = $"replication_status_{_followerServerId}_to_leader";

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
                                command.Parameters.AddWithValue("@SyncTime", DateTime.UtcNow);
                                await command.ExecuteNonQueryAsync();
                            }
                        }

                        await transaction.CommitAsync();
                    }
                    catch (Exception ex)
                    {
                        await transaction.RollbackAsync();
                        Console.WriteLine($"标记从库变更为已处理时出错: {ex.Message}");
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
                Console.WriteLine($"开始清理 {_dataRetentionDays} 天前的复制日志数据...");
                var cutoffDate = DateTime.Now.AddDays(-_dataRetentionDays);
                
                // 清理主库的复制日志
                await CleanupReplicationLogsInDatabase("主库", () => CreateLeaderDbContext(), cutoffDate);
                
                // 清理从库的复制日志
                await CleanupReplicationLogsInDatabase("从库", () => CreateFollowerDbContext(), cutoffDate);
                
                Console.WriteLine($"复制日志清理完成，已删除 {cutoffDate:yyyy-MM-dd HH:mm:ss} 之前的数据");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"清理复制日志时出错: {ex.Message}");
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
                Console.WriteLine($"{dbName} - 已删除 {deletedCount} 条过期的复制日志记录");
                
                // 清理复制状态表（如果存在）
                await CleanupReplicationStatusTables(connection, cutoffDate, dbName);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"清理 {dbName} 复制日志时出错: {ex.Message}");
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
                                Console.WriteLine($"{dbName} - 已删除 {deletedStatusCount} 条过期的复制状态记录 (表: {tableName})");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"清理状态表 {tableName} 时出错: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"清理 {dbName} 复制状态表时出错: {ex.Message}");
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
                using var leaderContext = CreateLeaderDbContext();
                
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
                Console.WriteLine($"检测冲突时出错: {ex.Message}");
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
            
            // 检查主库是否有更新的变更
            var newerLeaderChanges = await GetNewerChangesFromLeader(leaderContext, tableConfig, change);
            conflicts.AddRange(newerLeaderChanges);
            
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
                Console.WriteLine($"获取冲突字段时出错: {ex.Message}");
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
                        Console.WriteLine($"冲突需要人工审核: 表 {conflict.TableName}, 记录 {conflict.RecordId}");
                        return null;
                        
                    default:
                        throw new NotSupportedException($"不支持的冲突解决策略: {tableConfig.ConflictStrategy}");
                }
                
                if (resolvedEntry != null)
                {
                    conflict.Resolution = ConflictResolutionResult.ResolvedAutomatically;
                    Console.WriteLine($"冲突已自动解决: 表 {conflict.TableName}, 记录 {conflict.RecordId}, 策略: {tableConfig.ConflictStrategy}");
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
                Console.WriteLine($"解决冲突时出错: {ex.Message}");
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
                Console.WriteLine($"最后写入获胜策略执行时出错: {ex.Message}");
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
                command.Parameters.AddWithValue("@ResolvedAt", DateTime.UtcNow);
                
                await command.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"记录冲突日志时出错: {ex.Message}");
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
                using var leaderContext = CreateLeaderDbContext();
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
                Console.WriteLine($"检测同步间隙时出错: {ex.Message}");
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
                        EndTime = DateTime.UtcNow,
                        MissedOperations = missedCount,
                        Reason = "网络中断或同步延迟"
                    };
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"检测表 {tableConfig.TableName} 同步间隙时出错: {ex.Message}");
            }
            
            return null;
        }

        // 获取最后同步时间
        private async Task<DateTime> GetLastSyncTime(TableConfig tableConfig, FollowerDbContext followerContext)
        {
            try
            {
                string statusTableName = $"replication_status_{_followerServerId}";
                
                using var connection = (MySqlConnection)followerContext.Database.GetDbConnection();
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
                Console.WriteLine($"获取最后同步时间时出错: {ex.Message}");
                return DateTime.MinValue;
            }
        }

        // 网络恢复处理
        private async Task HandleNetworkRecovery()
        {
            try
            {
                Console.WriteLine("检测网络恢复，开始同步间隙检测...");
                
                var gapDetection = await DetectSynchronizationGaps();
                
                if (gapDetection.HasGaps)
                {
                    Console.WriteLine($"检测到同步间隙，共 {gapDetection.MissedOperationsCount} 个未同步操作");
                    await RecoverFromSynchronizationGap(gapDetection);
                }
                else
                {
                    Console.WriteLine("未检测到同步间隙，数据同步正常");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"网络恢复处理时出错: {ex.Message}");
            }
        }

        // 从同步间隙恢复
        private async Task RecoverFromSynchronizationGap(SynchronizationGapDetection gapDetection)
        {
            try
            {
                foreach (var gap in gapDetection.Gaps)
                {
                    Console.WriteLine($"开始恢复表 {gap.TableName} 的同步间隙，时间范围: {gap.StartTime:yyyy-MM-dd HH:mm:ss} - {gap.EndTime:yyyy-MM-dd HH:mm:ss}");
                    
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
                Console.WriteLine($"从同步间隙恢复时出错: {ex.Message}");
            }
        }

        // 强制同步表
        private async Task ForceSyncTable(TableConfig tableConfig, DateTime fromTime)
        {
            try
            {
                using var leaderContext = CreateLeaderDbContext();
                using var followerContext = CreateFollowerDbContext();
                
                // 获取指定时间之后的所有变更
                var missedChanges = await leaderContext.ReplicationLogs
                    .Where(l => l.TableName == tableConfig.TableName &&
                               l.Timestamp > fromTime &&
                               l.Direction == ReplicationDirection.LeaderToFollower)
                    .OrderBy(l => l.Timestamp)
                    .ToListAsync();
                
                if (missedChanges.Any())
                {
                    Console.WriteLine($"开始强制同步表 {tableConfig.TableName} 的 {missedChanges.Count} 个变更");
                    
                    // 分批处理
                    var batches = missedChanges.Chunk(_batchSize);
                    foreach (var batch in batches)
                    {
                        await ApplyChangesToFollower(followerContext, tableConfig, batch.ToList());
                        await MarkChangesAsProcessed(leaderContext, batch.ToList());
                    }
                    
                    Console.WriteLine($"表 {tableConfig.TableName} 强制同步完成");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"强制同步表 {tableConfig.TableName} 时出错: {ex.Message}");
            }
        }

        #endregion

        // 实现IDisposable接口
        public void Dispose()
        {
            StopReplication();
        }
    }
}