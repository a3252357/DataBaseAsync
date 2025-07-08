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
        }

        // 初始化从库
        public void Initialize()
        {
            CreateReplicationLogTable();
            CreateReplicationStatusTable();
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
            string triggerSql = $@"
                DROP TRIGGER IF EXISTS `{triggerName}`;
                
                CREATE TRIGGER `{triggerName}`
                AFTER INSERT ON `{tableConfig.TableName}`
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

            ExecuteSql(connection, triggerSql);
        }

        // 创建更新触发器
        private void CreateUpdateTrigger(MySqlConnection connection, TableConfig tableConfig)
        {
            string triggerName = $"tr_{tableConfig.TableName}_update";
            string triggerSql = $@"
                DROP TRIGGER IF EXISTS `{triggerName}`;
                
                CREATE TRIGGER `{triggerName}`
                AFTER UPDATE ON `{tableConfig.TableName}`
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

            ExecuteSql(connection, triggerSql);
        }

        // 创建删除触发器
        private void CreateDeleteTrigger(MySqlConnection connection, TableConfig tableConfig)
        {
            string triggerName = $"tr_{tableConfig.TableName}_delete";
            string triggerSql = $@"
                DROP TRIGGER IF EXISTS `{triggerName}`;
                
                CREATE TRIGGER `{triggerName}`
                AFTER DELETE ON `{tableConfig.TableName}`
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

            ExecuteSql(connection, triggerSql);
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