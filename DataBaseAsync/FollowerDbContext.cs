using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text;
using DataBaseAsync;

namespace DatabaseReplication
{
    public class FollowerDbContext : DbContext
    {
        private readonly string _connectionString;
        private readonly string _followerServerId;
        private readonly List<TableConfig> _tableConfigs;
        private readonly Logger _logger;

        public FollowerDbContext(
            DbContextOptions<FollowerDbContext> options,
            string connectionString,
            string followerServerId,
            List<TableConfig> tableConfigs)
            : base(options)
        {
            _connectionString = connectionString;
            _followerServerId = followerServerId;
            _tableConfigs = tableConfigs;
            _logger = Logger.Instance;
            Database.SetConnectionString(_connectionString);
        }

        public DbSet<ReplicationLogEntry> ReplicationLogs { get; set; }
        public DbSet<ReplicationFailureLog> ReplicationFailureLogs { get; set; }
        public DbSet<SyncProgress> SyncProgresses { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            _tableConfigs.ForEach(x =>
            {
                var entityTypeBuilder = modelBuilder.Entity(x.EntityType);
                x.EntityType.GetProperties().ToList().ForEach(z => {
                    var key = z.GetCustomAttribute<KeyAttribute>();
                    if (key != null)
                        entityTypeBuilder.HasKey(z.Name);
                });
                ////主键
                //var keys = x.GetCustomAttribute<KeysAttribute>();
                //if (keys != null)
                //    entityTypeBuilder.HasKey(keys.PropertyNames);

                ////索引
                //var indexs = x.GetCustomAttributes<IndexAttribute>();
                //if (indexs != null)
                //{
                //    indexs.ToList().ForEach(aIndex =>
                //    {
                //        entityTypeBuilder.HasIndex(aIndex.PropertyNames).IsUnique(aIndex.IsUnique);
                //    });
                //}
            });
            //支持IEntityTypeConfiguration配置
            _tableConfigs.Select(x => x.EntityType.Assembly).ToList().ForEach(aAssembly =>
            {
                modelBuilder.ApplyConfigurationsFromAssembly(aAssembly);
            });
            // 配置复制日志表
            modelBuilder.Entity<ReplicationLogEntry>(entity =>
            {
                entity.ToTable("replication_logs");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.TableName).HasColumnName("table_name").IsRequired().HasMaxLength(100);
                entity.Property(e => e.OperationType).HasColumnName("operation_type").IsRequired();
                entity.Property(e => e.RecordId).HasColumnName("record_id").IsRequired().HasMaxLength(200);
                entity.Property(e => e.Data).IsRequired().HasMaxLength(100); // 仅存储主键
                entity.Property(e => e.Timestamp).IsRequired();
                entity.Property(e => e.Processed).IsRequired();
                entity.Property(e => e.Direction).IsRequired();
                entity.Property(e => e.SourceServer).HasColumnName("source_server").IsRequired().HasMaxLength(100);
                entity.Property(e => e.OperationId).HasColumnName("operation_id").IsRequired();
            });

            // 配置复制失败日志表
            modelBuilder.Entity<ReplicationFailureLog>(entity =>
            {
                entity.ToTable("replication_failure_logs");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.TableName).HasColumnName("table_name").IsRequired().HasMaxLength(100);
                entity.Property(e => e.OperationType).HasColumnName("operation_type").IsRequired();
                entity.Property(e => e.RecordId).HasColumnName("record_id").IsRequired().HasMaxLength(200);
                entity.Property(e => e.Data).HasColumnType("text");
                entity.Property(e => e.ErrorMessage).HasColumnName("error_message").IsRequired().HasColumnType("text");
                entity.Property(e => e.FailureTime).HasColumnName("failure_time").IsRequired();
                entity.Property(e => e.RetryCount).HasColumnName("retry_count").IsRequired();
                entity.Property(e => e.FollowerServerId).HasColumnName("follower_server_id").IsRequired().HasMaxLength(100);
            });

            // 配置同步进度表
            modelBuilder.Entity<SyncProgress>(entity =>
            {
                entity.ToTable("sync_progress");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.TableName).HasColumnName("table_name").IsRequired().HasMaxLength(100);
                entity.Property(e => e.FollowerServerId).HasColumnName("follower_server_id").IsRequired().HasMaxLength(100);
                entity.Property(e => e.LastSyncedId).HasColumnName("last_synced_id").IsRequired();
                entity.Property(e => e.LastSyncTime).HasColumnName("last_sync_time");
                entity.HasIndex(e => new { e.TableName, e.FollowerServerId }).IsUnique();
            });
        }

        // 初始化从库
        public void Initialize()
        {
            CreateReplicationLogTable();
            CreateReplicationStatusTable();
                CreateReplicationFailureLogTable();
            CreateSyncProgressTable();
            CreateReplicationTriggers();
        }

        // 创建复制日志表
        private void CreateReplicationLogTable()
        {
            string createTableSql = @"
                CREATE TABLE IF NOT EXISTS `replication_logs` (
                  `id` int NOT NULL AUTO_INCREMENT,
                  `table_name` varchar(100) NOT NULL,
                  `operation_type` int(11) NOT NULL,
                  `record_id` varchar(200) NOT NULL,
                  `data` varchar(100) NOT NULL,
                  `timestamp` datetime NOT NULL,
                  `processed` tinyint(1) NOT NULL DEFAULT '0',
                  `direction` int(11) NOT NULL,
                  `source_server` varchar(100) NOT NULL,
                  `operation_id` char(36) NOT NULL,
                  PRIMARY KEY (`id`),
                  KEY `idx_table_operation` (`table_name`,`operation_type`),
                  KEY `idx_timestamp` (`timestamp`),
                  KEY `idx_operation_id` (`operation_id`)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;";

            ExecuteSql(createTableSql);
        }

        // 创建复制状态表
        private void CreateReplicationStatusTable()
        {
            string tableName = $"replication_status_{_followerServerId}_to_leader";

            string createTableSql = $@"
                CREATE TABLE IF NOT EXISTS `{tableName}` (
                  `log_entry_id` int NOT NULL,
                  `is_synced` tinyint(1) NOT NULL DEFAULT '0',
                  `sync_time` datetime DEFAULT NULL,
                  `error_message` varchar(500) DEFAULT NULL,
                  PRIMARY KEY (`log_entry_id`),
                  FOREIGN KEY (`log_entry_id`) REFERENCES `replication_logs` (`id`)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;";

            ExecuteSql(createTableSql);
        }

        // 创建复制失败日志表
        private void CreateReplicationFailureLogTable()
        {
            string createTableSql = @"
                CREATE TABLE IF NOT EXISTS `replication_failure_logs` (
                  `id` int NOT NULL AUTO_INCREMENT,
                  `table_name` varchar(100) NOT NULL,
                  `operation_type` int(11) NOT NULL,
                  `record_id` varchar(200) NOT NULL,
                  `data` text,
                  `error_message` text NOT NULL,
                  `retry_count` int NOT NULL DEFAULT '0',
                  `failure_time` datetime NOT NULL,
                  `follower_server_id` varchar(100) NOT NULL,
                  PRIMARY KEY (`id`),
                  KEY `idx_table_operation` (`table_name`,`operation_type`),
                  KEY `idx_failure_time` (`failure_time`)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;";

            ExecuteSql(createTableSql);
        }

        // 创建同步进度表
        private void CreateSyncProgressTable()
        {
            string createTableSql = @"
                CREATE TABLE IF NOT EXISTS `sync_progress` (
                  `id` int NOT NULL AUTO_INCREMENT,
                  `table_name` varchar(100) NOT NULL,
                  `follower_server_id` varchar(100) NOT NULL,
                  `last_synced_id` int NOT NULL DEFAULT '0',
                  `last_sync_time` datetime DEFAULT NULL,
                  PRIMARY KEY (`id`),
                  UNIQUE KEY `uk_sync_progress` (`table_name`,`follower_server_id`)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;";

            ExecuteSql(createTableSql);
        }

        // 检查触发器定义是否相同
        private bool IsTriggerDefinitionSame(MySqlConnection connection, string triggerName, string expectedTriggerBody)
        {
            try
            {
                string checkSql = @"
                    SELECT TRIGGER_NAME, ACTION_STATEMENT 
                    FROM INFORMATION_SCHEMA.TRIGGERS 
                    WHERE TRIGGER_SCHEMA = DATABASE() 
                    AND TRIGGER_NAME = @triggerName";

                using (var command = new MySqlCommand(checkSql, connection))
                {
                    command.Parameters.AddWithValue("@triggerName", triggerName);
                    using (var reader = command.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            string existingDefinition = reader.GetString("ACTION_STATEMENT");
                            
                            // 标准化两个定义进行比较（移除多余空格、换行符等）
                            string normalizedExpected = NormalizeTriggerDefinition(expectedTriggerBody);
                            string normalizedExisting = NormalizeTriggerDefinition(existingDefinition);
                            
                            _logger.Info($"触发器 {triggerName} 检查:");
                            
                            bool isEqual = normalizedExpected.Equals(normalizedExisting, StringComparison.OrdinalIgnoreCase);
                            _logger.Info($"定义是否相同: {isEqual}");
                            
                            return isEqual;
                        }
                        else
                        {
                            _logger.Info($"触发器 {triggerName} 不存在");
                        }
                    }
                }
                
                // 触发器不存在
                return false;
            }
            catch (Exception ex)
            {
                _logger.Error($"检查触发器 {triggerName} 定义时出错: {ex.Message}");
                return false;
            }
        }

        // 标准化触发器定义，用于比较
        private string NormalizeTriggerDefinition(string definition)
        {
            if (string.IsNullOrEmpty(definition))
                return string.Empty;

            // 移除多余的空格、制表符、换行符
            string normalized = System.Text.RegularExpressions.Regex.Replace(definition.Trim(), @"\s+", " ")
                .Replace("\r\n", " ")
                .Replace("\n", " ")
                .Replace("\r", " ")
                .Replace("\t", " ")
                .Trim();
            
            // MySQL存储的触发器定义可能不包含AFTER INSERT ON部分，只包含BEGIN...END部分
            // 如果定义以BEGIN开头，说明是MySQL存储的ACTION_STATEMENT格式
            if (normalized.StartsWith("BEGIN", StringComparison.OrdinalIgnoreCase))
            {
                // 只比较BEGIN...END部分
                return normalized;
            }
            
            // 如果是完整的触发器定义，提取BEGIN...END部分
            int beginIndex = normalized.IndexOf("BEGIN", StringComparison.OrdinalIgnoreCase);
            if (beginIndex >= 0)
            {
                return normalized.Substring(beginIndex);
            }
            
            return normalized;
        }

        // 创建复制触发器（仅用于双向复制或从库到主库复制的表）
        public void CreateReplicationTriggers()
        {
            using (var connection = (MySqlConnection)Database.GetDbConnection())
            {
                connection.Open();

                foreach (var tableConfig in _tableConfigs.Where(t =>
                    t.ReplicationDirection == ReplicationDirection.FollowerToLeader ||
                    t.ReplicationDirection == ReplicationDirection.Bidirectional))
                {
                    CreateInsertTrigger(connection, tableConfig);
                    CreateUpdateTrigger(connection, tableConfig);
                    CreateDeleteTrigger(connection, tableConfig);
                }
            }
        }

        // 创建插入触发器
        private void CreateInsertTrigger(MySqlConnection connection, TableConfig tableConfig)
        {
            string triggerName = $"tr_{tableConfig.TableName}_insert";
            string expectedTriggerBody = $@"AFTER INSERT ON `{tableConfig.TableName}`
                FOR EACH ROW
                BEGIN
                    -- 防止触发器递归
                    IF @is_replicating = 0 OR @is_replicating IS NULL THEN
                        INSERT INTO replication_logs (table_name, operation_type, record_id, data, timestamp, direction, source_server, operation_id)
                        VALUES (
                            '{tableConfig.TableName}',
                            0,
                            CAST(NEW.{tableConfig.PrimaryKey} AS CHAR(100)),
                            CAST(NEW.{tableConfig.PrimaryKey} AS CHAR(100)),
                            NOW(),
                            1,
                            '{_followerServerId}',
                            UUID()
                        );
                    END IF;
                END";

            // 检查触发器是否存在且定义相同
            if (IsTriggerDefinitionSame(connection, triggerName, expectedTriggerBody))
            {
                _logger.Info($"触发器 {triggerName} 已存在且定义相同，跳过创建");
                return;
            }

            string triggerSql = $@"
                DROP TRIGGER IF EXISTS `{triggerName}`;
                
                CREATE TRIGGER `{triggerName}`
                {expectedTriggerBody}";

            ExecuteSql(connection, triggerSql);
            _logger.Info($"触发器 {triggerName} 创建完成");
        }

        // 创建更新触发器
        private void CreateUpdateTrigger(MySqlConnection connection, TableConfig tableConfig)
        {
            string triggerName = $"tr_{tableConfig.TableName}_update";
            string expectedTriggerBody = $@"AFTER UPDATE ON `{tableConfig.TableName}`
                FOR EACH ROW
                BEGIN
                    -- 防止触发器递归
                    IF @is_replicating = 0 OR @is_replicating IS NULL THEN
                        INSERT INTO replication_logs (table_name, operation_type, record_id, data, timestamp, direction, source_server, operation_id)
                        VALUES (
                            '{tableConfig.TableName}',
                            1,
                            CAST(NEW.{tableConfig.PrimaryKey} AS CHAR(100)),
                            CAST(NEW.{tableConfig.PrimaryKey} AS CHAR(100)),
                            NOW(),
                            1,
                            '{_followerServerId}',
                            UUID()
                        );
                    END IF;
                END";

            // 检查触发器是否存在且定义相同
            if (IsTriggerDefinitionSame(connection, triggerName, expectedTriggerBody))
            {
                _logger.Info($"触发器 {triggerName} 已存在且定义相同，跳过创建");
                return;
            }

            string triggerSql = $@"
                DROP TRIGGER IF EXISTS `{triggerName}`;
                
                CREATE TRIGGER `{triggerName}`
                {expectedTriggerBody}";

            ExecuteSql(connection, triggerSql);
            _logger.Info($"触发器 {triggerName} 创建完成");
        }

        // 创建删除触发器
        private void CreateDeleteTrigger(MySqlConnection connection, TableConfig tableConfig)
        {
            string triggerName = $"tr_{tableConfig.TableName}_delete";
            string expectedTriggerBody = $@"AFTER DELETE ON `{tableConfig.TableName}`
                FOR EACH ROW
                BEGIN
                    -- 防止触发器递归
                    IF @is_replicating = 0 OR @is_replicating IS NULL THEN
                        INSERT INTO replication_logs (table_name, operation_type, record_id, data, timestamp, direction, source_server, operation_id)
                        VALUES (
                            '{tableConfig.TableName}',
                            2,
                            CAST(OLD.{tableConfig.PrimaryKey} AS CHAR(100)),
                            CAST(OLD.{tableConfig.PrimaryKey} AS CHAR(100)),
                            NOW(),
                            1,
                            '{_followerServerId}',
                            UUID()
                        );
                    END IF;
                END";

            // 检查触发器是否存在且定义相同
            if (IsTriggerDefinitionSame(connection, triggerName, expectedTriggerBody))
            {
                _logger.Info($"触发器 {triggerName} 已存在且定义相同，跳过创建");
                return;
            }

            string triggerSql = $@"
                DROP TRIGGER IF EXISTS `{triggerName}`;
                
                CREATE TRIGGER `{triggerName}`
                {expectedTriggerBody}";

            ExecuteSql(connection, triggerSql);
            _logger.Info($"触发器 {triggerName} 创建完成");
        }

        // 执行SQL语句
        private void ExecuteSql(string sql)
        {
            using (var connection = new MySqlConnection(_connectionString))
            {
                connection.Open();

                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        using (var command = new MySqlCommand(sql, connection, transaction))
                        {
                            command.ExecuteNonQuery();
                        }
                        transaction.Commit();
                    }
                    catch (Exception ex)
                    {
                        transaction.Rollback();
                        _logger.Error($"执行SQL时出错: {ex.Message}");
                        _logger.Error($"SQL: {sql}");
                        throw;
                    }
                }
            }
        }

        // 执行SQL语句（带现有连接）
        private void ExecuteSql(MySqlConnection connection, string sql)
        {
            using (var transaction = connection.BeginTransaction())
            {
                try
                {
                    using (var command = new MySqlCommand(sql, connection, transaction))
                    {
                        command.ExecuteNonQuery();
                    }
                    transaction.Commit();
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    _logger.Error($"执行SQL时出错: {ex.Message}");
                    _logger.Error($"SQL: {sql}");
                    throw;
                }
            }
        }
    }
}