# 数据库复制系统配置说明

## 配置文件

本项目使用 `appsettings.json` 配置文件来管理数据库复制的相关参数，避免硬编码。

### 配置文件结构

```json
{
  "DatabaseReplication": {
    "InitializeExistingData": true,
    "LeaderConnectionString": "主库连接字符串",
    "FollowerConnectionString": "从库连接字符串",
    "FollowerServerId": "从库服务器唯一标识",
    "Tables": [
      {
        "EntityTypeName": "实体类型名称",
        "TableName": "数据库表名",
        "PrimaryKey": "主键字段名",
        "ReplicationIntervalSeconds": 3,
        "ReplicationDirection": "复制方向"
      }
    ]
  }
}
```

### 配置参数说明

#### 主要配置

- **InitializeExistingData**: 布尔值，是否在启动时初始化现有数据
  - `true`: 启动时会复制主库的现有数据到从库
  - `false`: 跳过初始化，只同步增量变更

- **LeaderConnectionString**: 主库的 MySQL 连接字符串
- **FollowerConnectionString**: 从库的 MySQL 连接字符串
- **FollowerServerId**: 从库服务器的唯一标识符，用于区分不同的从库实例

#### 全局配置

- **BatchSize**: 每批处理的记录数量，默认1000条
- **DataRetentionDays**: 数据保留天数，默认30天
- **CleanupIntervalHours**: 清理任务执行间隔（小时），默认24小时

#### 表配置

每个表的配置包含以下字段：

- **EntityTypeName**: 实体类的名称（不包含命名空间），如 `d_truck`
- **TableName**: 数据库中的表名
- **PrimaryKey**: 表的主键字段名
- **ReplicationIntervalSeconds**: 复制检查间隔（秒）
- **Enabled**: 是否启用此表的复制，默认true
- **InitializeExistingData**: 是否初始化此表的现有数据，默认true
- **ReplicationDirection**: 复制方向，支持以下值：
  - `LeaderToFollower`: 主库到从库（单向）
  - `FollowerToLeader`: 从库到主库（单向）
  - `Bidirectional`: 双向复制
- **ConflictStrategy**: 冲突解决策略，支持以下值：
  - `PreferLeader`: 优先选择主库的变更
  - `PreferFollower`: 优先选择从库的变更
  - `LastWriteWins`: 最后写入获胜，基于时间戳和优先级字段
  - `FieldPriority`: 基于字段优先级解决冲突
  - `Custom`: 自定义冲突解决逻辑
  - `ManualReview`: 需要人工审核的冲突
- **ConflictResolutionPriorityFields**: 冲突解决优先级字段数组，用于LastWriteWins和FieldPriority策略

### 使用方法

1. 修改 `appsettings.json` 文件中的配置参数
2. 运行程序，系统会自动读取配置文件
3. 如果需要修改 `InitializeExistingData` 参数，只需要在配置文件中更改，无需修改代码

### 注意事项

- 确保 `appsettings.json` 文件位于程序的输出目录中
- 连接字符串中的密码等敏感信息请妥善保管
- 实体类型必须存在于项目中，否则会跳过该表的配置
- 复制方向配置错误会导致该表配置被跳过

### 示例配置

```json
{
  "DatabaseReplication": {
    "InitializeExistingData": false,
    "LeaderConnectionString": "Data Source=localhost;Initial Catalog=main_db;User ID=root;Password=password;",
    "FollowerConnectionString": "Data Source=localhost;Initial Catalog=replica_db;User ID=root;Password=password;",
    "FollowerServerId": "replica_001",
    "BatchSize": 1000,
    "DataRetentionDays": 30,
    "CleanupIntervalHours": 24,
    "Tables": [
      {
        "EntityTypeName": "d_truck",
        "TableName": "d_truck",
        "PrimaryKey": "Id",
        "ReplicationIntervalSeconds": 5,
        "Enabled": true,
        "InitializeExistingData": true,
        "ReplicationDirection": "Bidirectional",
        "ConflictStrategy": "LastWriteWins",
        "ConflictResolutionPriorityFields": [
          "version",
          "updated_at"
        ]
      },
      {
        "EntityTypeName": "d_man",
        "TableName": "d_man",
        "PrimaryKey": "Id",
        "ReplicationIntervalSeconds": 10,
        "Enabled": true,
        "InitializeExistingData": false,
        "ReplicationDirection": "LeaderToFollower",
        "ConflictStrategy": "PreferLeader",
        "ConflictResolutionPriorityFields": []
      }
    ]
  }
}
```

### 高级配置示例

项目中还提供了 `appsettings.example.json` 文件，包含了更详细的配置示例和说明，展示了不同冲突解决策略的使用场景：

- **单向复制**：适用于数据仓库、报表系统等只读场景
- **双向同步**：适用于多活架构、分布式系统等需要双向数据同步的场景
- **冲突解决**：提供多种策略处理数据冲突，确保数据一致性

### 冲突解决策略详解

#### LastWriteWins（最后写入获胜）
基于优先级字段进行比较，支持：
- 数值字段比较（version、sequence_number等）
- 日期时间字段比较（updated_at、modified_time等）
- 版本号字符串比较（"1.2.3"格式）

#### FieldPriority（字段优先级）
按照配置的字段顺序依次比较，直到找到差异为止。

#### PreferLeader/PreferFollower（优先策略）
简单直接的冲突解决方式，适用于有明确数据权威性的场景。

#### ManualReview（人工审核）
将冲突记录到日志表中，需要人工介入处理，适用于重要业务数据。

## 时间同步功能

系统提供了直接时间同步功能，确保主从库时间的一致性：

### 直接时间同步

**DirectTimeSynchronizationService** 直接修改系统时间，与主库时间保持一致。

#### 功能特性
- **直接修改系统时间**：使用Windows API直接设置系统时间
- **权限检查**：自动检测是否有管理员权限
- **UAC权限请求**：自动请求管理员权限（UAC弹窗）
- **智能权限处理**：智能权限检测和处理
- **精确同步**：支持微秒级精度的时间同步
- **阈值控制**：可配置的时间差异阈值，避免频繁同步

#### 配置示例
```json
{
  "DatabaseReplication": {
    "TimeSynchronization": {
      "Enabled": true,
      "CheckIntervalMinutes": 5,
      "MaxAllowedDifferenceSeconds": 30,
      "SyncOnStartup": true,
      "TimeSyncIntervalMinutes": 60
    }
  }
}
```

#### 使用要求
- **Windows系统**：使用Windows API，仅支持Windows平台
- **UAC权限**：程序会自动请求管理员权限（UAC）
- **网络连接**：需要能够连接到主库获取时间

#### 工作原理
1. 启动时自动检测管理员权限
2. 如无权限，弹出UAC对话框请求用户授权
3. 获得权限后，检查系统时间与主库时间的差异
4. 如果差异超过阈值，直接调用 Windows API 修改系统时间
5. 定期检查并维护时间同步

### 配置参数说明

#### 时间同步配置
- **TimeSynchronization**: 时间同步配置对象
  - **Enabled**: 是否启用时间同步功能
    - `true`: 启用时间同步
    - `false`: 禁用时间同步
  - **CheckIntervalMinutes**: 时间检查间隔（分钟）
    - 用于定期检查时间差异的间隔
  - **MaxAllowedDifferenceSeconds**: 允许的最大时间差异（秒）
    - 建议值：10-60秒
    - 只有超过此阈值才会执行同步
  - **SyncOnStartup**: 启动时自动同步
    - `true`: 程序启动时立即执行时间同步
    - `false`: 跳过启动时同步
  - **TimeSyncIntervalMinutes**: 定期同步检查间隔（分钟）
    - 建议值：30-120分钟
    - 设置为0禁用定期同步

### 使用场景

#### 直接时间同步适用于：
- **生产环境**：需要系统级时间一致性
- **单机部署**：可以获得管理员权限的环境
- **精确要求**：对时间精度要求极高的场景
- **系统集成**：需要与其他系统保持时间一致

### 最佳实践

1. **生产环境建议**：
   - 以管理员身份运行程序
   - 启用直接时间同步
   - 设置合理的同步间隔（60分钟）
   - 设置适当的阈值（30秒）

2. **监控建议**：
   - 关注控制台输出的时间同步日志
   - 定期检查时间差异是否在可接受范围内
   - 监控同步失败的情况

### 注意事项

1. **权限要求**：
   - 程序会自动请求管理员权限，用户需要在UAC对话框中点击"是"
   - 如果用户拒绝UAC权限请求，程序将退出运行
   - 支持命令行参数传递，权限提升后会保持原始启动参数
   - 建议在生产环境中以服务方式运行

2. **时间回退风险**：
   - 系统时间回退可能影响其他应用
   - 建议在维护窗口期执行大幅度时间调整

3. **网络依赖**：
   - 时间同步依赖数据库连接
   - 网络中断时无法执行同步

4. **系统兼容性**：
   - 直接时间同步仅支持Windows平台
   - Linux/macOS环境暂不支持

### 测试功能

项目提供了测试类：

- **DirectTimeSyncTest**: 直接时间同步功能测试

可以通过这个测试类验证时间同步功能的正确性。
