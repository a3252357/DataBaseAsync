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
}