using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data;
using System.Data.Common;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.Configuration;

namespace DatabaseReplication
{
    // 复制操作类型
    public enum ReplicationOperation
    {
        Insert,
        Update,
        Delete
    }

    // 复制方向
    public enum ReplicationDirection
    {
        LeaderToFollower,
        FollowerToLeader,
        Bidirectional
    }

    // 冲突解决策略
    public enum ConflictResolutionStrategy
    {
        PreferLeader,
        PreferFollower,
        LastWriteWins,
        Custom,
        FieldPriority,
        ManualReview
    }

    // 冲突类型
    public enum ConflictType
    {
        ConcurrentUpdate,    // 并发更新
        DeleteAfterUpdate,   // 删除后更新
        UpdateAfterDelete,   // 更新后删除
        DuplicateInsert,     // 重复插入
        VersionMismatch      // 版本不匹配
    }

    // 冲突解决结果
    public enum ConflictResolutionResult
    {
        ResolvedAutomatically,
        RequiresManualReview,
        Failed,
        Skipped
    }

    // 复制状态
    public enum ReplicationStatus
    {
        Running,
        Stopped,
        Error
    }

    // 表同步模式
    public enum TableSyncMode
    {
        Entity,     // 基于实体类的同步
        NoEntity    // 无需实体类的同步
    }

    // 表结构同步策略
    public enum SchemaSyncStrategy
    {
        Disabled,           // 禁用表结构同步
        OnStartup,          // 仅在启动时同步
        Periodic,           // 定期同步
        OnStartupAndPeriodic // 启动时和定期同步
    }

    // 表配置
    public class TableConfig
    {
        public Type EntityType { get; set; }
        public string TableName { get; set; }
        public string PrimaryKey { get; set; }
        public int ReplicationIntervalSeconds { get; set; } = 5;
        public bool InitializeExistingData { get; set; } = true;
        public bool Enabled { get; set; } = true;
        public ReplicationDirection ReplicationDirection { get; set; } = ReplicationDirection.LeaderToFollower;
        public ConflictResolutionStrategy ConflictStrategy { get; set; } = ConflictResolutionStrategy.PreferLeader;
        public List<string> ConflictResolutionPriorityFields { get; set; }
        
        // 表级同步相关配置
        public TableSyncMode SyncMode { get; set; } = TableSyncMode.Entity;
        public SchemaSyncStrategy SchemaSync { get; set; } = SchemaSyncStrategy.OnStartup;
        public int SchemaSyncIntervalMinutes { get; set; } = 60; // 表结构同步间隔（分钟）
        public bool AllowSchemaChanges { get; set; } = true; // 是否允许表结构变更
    }
    // 复制日志条目
    public class ReplicationLogEntry
    {
        public int Id { get; set; }
        public string TableName { get; set; }
        public ReplicationOperation OperationType { get; set; }
        public string RecordId { get; set; }
        public string Data { get; set; }
        public DateTime Timestamp { get; set; }
        public bool Processed { get; set; }
        public ReplicationDirection Direction { get; set; }
        public string SourceServer { get; set; }
        public Guid OperationId { get; set; } = Guid.NewGuid();
    }

    // 数据冲突
    public class DataConflict
    {
        public string TableName { get; set; }
        public string RecordId { get; set; }
        public string FollowerServer { get; set; }
        public ReplicationLogEntry SourceEntry { get; set; }
        public ReplicationLogEntry TargetEntry { get; set; }
        public DateTime DetectedAt { get; set; } = DateTime.Now;
        public ConflictType Type { get; set; }
        public ConflictResolutionResult Resolution { get; set; }
        public string ResolutionReason { get; set; }
        public Dictionary<string, object> ConflictingFields { get; set; } = new Dictionary<string, object>();
        public string ConflictDetails { get; set; }
    }

    // 冲突日志记录
    public class ConflictLog
    {
        public int Id { get; set; }
        public string TableName { get; set; }
        public string RecordId { get; set; }
        public ConflictType ConflictType { get; set; }
        public DateTime DetectedAt { get; set; }
        public ConflictResolutionResult Resolution { get; set; }
        public string ResolutionStrategy { get; set; }
        public string Details { get; set; }
        public string ResolvedBy { get; set; }
        public DateTime? ResolvedAt { get; set; }
    }

    // 同步间隙检测结果
    public class SynchronizationGapDetection
    {
        public bool HasGaps { get; set; }
        public List<SyncGap> Gaps { get; set; } = new List<SyncGap>();
        public DateTime LastSyncTime { get; set; }
        public int MissedOperationsCount { get; set; }
    }

    // 同步间隙
    public class SyncGap
    {
        public string TableName { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public int MissedOperations { get; set; }
        public string Reason { get; set; }
    }

    // 复制失败日志
    public class ReplicationFailureLog
    {
        public int Id { get; set; }
        public string TableName { get; set; }
        public string OperationType { get; set; }  // 改为string类型，与代码使用一致
        public string RecordId { get; set; }
        public string Data { get; set; }  // 保留此字段
        public string ErrorMessage { get; set; }
        public DateTime FailureTime { get; set; }  // 改为FailureTime，与代码使用一致
        public int RetryCount { get; set; }
        public string FollowerServerId { get; set; }  // 改为FollowerServerId，与代码使用一致
        // 移除不使用的字段：OriginalTimestamp, FailedAt, SourceServer, OriginalOperationId, Direction
    }

    public class SyncProgress
    {
        public int Id { get; set; }
        public string TableName { get; set; }
        public string FollowerServerId { get; set; }  // 与代码使用一致
        public int LastSyncedId { get; set; }         // 与代码使用一致
        public DateTime LastSyncTime { get; set; }
        // 移除用户提供但代码中未使用的字段：SourceServer, TargetServer, Direction, IsActive
    }

    // 手动重试结果
    public class ManualRetryResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public int ProcessedCount { get; set; }
        public Dictionary<string, int> ProcessedTables { get; set; } = new Dictionary<string, int>();
    }

    // 表结构信息
    public class TableSchema
    {
        public string TableName { get; set; }
        public List<ColumnInfo> Columns { get; set; } = new List<ColumnInfo>();
        public List<IndexInfo> Indexes { get; set; } = new List<IndexInfo>();
        public string PrimaryKey { get; set; }
        public string Engine { get; set; }
        public string Charset { get; set; }
        public string Collation { get; set; }
    }

    // 列信息
    public class ColumnInfo
    {
        public string ColumnName { get; set; }
        public string DataType { get; set; }
        public string FullDataType { get; set; } // 包含长度等完整类型信息
        public bool IsNullable { get; set; }
        public string DefaultValue { get; set; }
        public bool IsAutoIncrement { get; set; }
        public string Comment { get; set; }
        public int? MaxLength { get; set; }
        public int? NumericPrecision { get; set; }
        public int? NumericScale { get; set; }
        public int OrdinalPosition { get; set; }
    }

    // 索引信息
    public class IndexInfo
    {
        public string IndexName { get; set; }
        public List<string> ColumnNames { get; set; } = new List<string>();
        public bool IsUnique { get; set; }
        public bool IsPrimary { get; set; }
        public string IndexType { get; set; }
    }

    // 表结构差异
    public class TableSchemaDifference
    {
        public string TableName { get; set; }
        public List<ColumnInfo> ColumnsToAdd { get; set; } = new List<ColumnInfo>();
        public List<ColumnInfo> ColumnsToModify { get; set; } = new List<ColumnInfo>();
        public List<string> ColumnsToDrop { get; set; } = new List<string>();
        public List<IndexInfo> IndexesToAdd { get; set; } = new List<IndexInfo>();
        public List<string> IndexesToDrop { get; set; } = new List<string>();
        public bool HasDifferences => ColumnsToAdd.Any() || ColumnsToModify.Any() || ColumnsToDrop.Any() || IndexesToAdd.Any() || IndexesToDrop.Any();
        public List<string> Warnings { get; set; } = new List<string>();
    }

    // 表结构同步结果
    public class SchemaSyncResult
    {
        public bool Success { get; set; }
        public string TableName { get; set; }
        public List<string> ExecutedStatements { get; set; } = new List<string>();
        public string ErrorMessage { get; set; }
        public TimeSpan Duration { get; set; }
        public TableSchemaDifference AppliedDifferences { get; set; }
    }
}