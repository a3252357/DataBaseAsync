using System.Collections.Generic;

namespace DatabaseReplication.Leader
{
    public class DatabaseReplicationConfig
    {
        public bool InitializeExistingData { get; set; }
        public string LeaderConnectionString { get; set; } = string.Empty;
        public string LeaderReadOnlyConnectionString { get; set; } = string.Empty;
        public string FollowerConnectionString { get; set; } = string.Empty;
        public string FollowerServerId { get; set; } = string.Empty;
        public int BatchSize { get; set; } = 1000; // 每批处理的记录数量，默认1000条
        public int DataRetentionDays { get; set; } = 30; // 数据保留天数，默认30天
        public int CleanupIntervalHours { get; set; } = 24; // 清理任务执行间隔（小时），默认24小时
        public TimeSynchronizationConfig TimeSynchronization { get; set; } = new TimeSynchronizationConfig();
        public List<TableConfigData> Tables { get; set; } = new List<TableConfigData>();
    }

    public class TimeSynchronizationConfig
    {
        public bool Enabled { get; set; } = false;
        public int CheckIntervalMinutes { get; set; } = 30; // 时间检查间隔（分钟）
        public int MaxAllowedDifferenceSeconds { get; set; } = 5; // 允许的最大时间差异（秒）
        public bool SyncOnStartup { get; set; } = true; // 是否在启动时执行同步
        public int TimeSyncIntervalMinutes { get; set; } = 30; // 时间同步间隔（分钟）
    }

    public class TableConfigData
    {
        public string EntityTypeName { get; set; } = string.Empty;
        public string TableName { get; set; } = string.Empty;
        public string PrimaryKey { get; set; } = string.Empty;
        public int ReplicationIntervalSeconds { get; set; }
        public bool Enabled { get; set; } = true;
        public bool InitializeExistingData { get; set; } = true;
        public string ReplicationDirection { get; set; } = string.Empty;
        public string ConflictStrategy { get; set; } = string.Empty;
        public List<string> ConflictResolutionPriorityFields { get; set; } = new List<string>();
    }
}