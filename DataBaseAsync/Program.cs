using Coldairarrow.bgmj.Entity;
using DatabaseReplication.Follower;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.Reflection;
using System.Linq;
using DataBaseAsync;

namespace DatabaseReplication.Leader
{
    //class Program
    //{
    //    static async Task Main(string[] args)
    //    {
    //        // 配置主库连接
    //        string leaderConnectionString = "Data Source=116.204.80.112;Initial Catalog=bg202089;User ID=root;Password=@Aa3252357;charset=utf8mb4;sslmode=none;Default Command Timeout=600;";

    //        // 配置需要复制的表
    //        var tableConfigs = new List<TableConfig>
    //        {
    //            new TableConfig
    //            {
    //                EntityType = typeof(d_man),
    //                TableName = "d_man",
    //                PrimaryKey = "Id",
    //                ReplicationIntervalSeconds = 3,
    //                ReplicationDirection = ReplicationDirection.LeaderToFollower
    //            }
    //        };

    //        // 创建并初始化主库上下文
    //        using (var context = new LeaderDbContext(
    //            new DbContextOptionsBuilder<LeaderDbContext>()
    //                .UseMySql(ServerVersion.AutoDetect(leaderConnectionString))
    //                .Options, leaderConnectionString,
    //            tableConfigs))
    //        {
    //            // 创建复制触发器
    //            context.Initialize();

    //            Console.WriteLine("主库初始化完成，复制触发器已创建");
    //            Console.WriteLine("按任意键退出...");
    //            Console.ReadKey();
    //        }
    //    }
    //}

    class Program
    {
        static async Task Main(string[] args)
        {
            // 读取配置文件
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            var config = configuration.GetSection("DatabaseReplication").Get<DatabaseReplicationConfig>();
            if (config == null)
            {
                Console.WriteLine("无法读取配置文件，请检查 appsettings.json");
                return;
            }

            // 从配置文件获取连接字符串和其他参数
            string leaderConnectionString = config.LeaderConnectionString;
            string followerConnectionString = config.FollowerConnectionString;
            string followerServerId = config.FollowerServerId;
            bool initializeExistingData = config.InitializeExistingData;

            // 根据配置创建表配置
            var tableConfigs = new List<TableConfig>();
            foreach (var tableConfigData in config.Tables)
            {
                // 根据实体类型名称获取类型
                Type? entityType = GetEntityType(tableConfigData.EntityTypeName);
                if (entityType == null)
                {
                    Console.WriteLine($"找不到实体类型: {tableConfigData.EntityTypeName}");
                    continue;
                }

                // 解析复制方向
                if (!Enum.TryParse<ReplicationDirection>(tableConfigData.ReplicationDirection, out var direction))
                {
                    Console.WriteLine($"无效的复制方向: {tableConfigData.ReplicationDirection}");
                    continue;
                }

                // 解析冲突解决策略
                if (!Enum.TryParse<ConflictResolutionStrategy>(tableConfigData.ConflictStrategy, out var conflictStrategy))
                {
                    Console.WriteLine($"无效的冲突解决策略: {tableConfigData.ConflictStrategy}，使用默认策略 PreferLeader");
                    conflictStrategy = ConflictResolutionStrategy.PreferLeader;
                }

                tableConfigs.Add(new TableConfig
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
            // 创建并初始化主库上下文
            using (var context = new LeaderDbContext(
                new DbContextOptionsBuilder<LeaderDbContext>()
                    .UseMySql(ServerVersion.AutoDetect(leaderConnectionString))
                    .Options, leaderConnectionString,
                tableConfigs))
            {
                // 创建复制触发器
                context.Initialize();
            }
            // 创建并初始化从库上下文
            using (var followerContext = new FollowerDbContext(
                new DbContextOptionsBuilder<FollowerDbContext>()
                    .UseMySql(ServerVersion.AutoDetect(followerConnectionString))
                    .Options,
                followerConnectionString,
                followerServerId,
                tableConfigs))
            {
                // 初始化从库（创建表和触发器）
                followerContext.Initialize();
                Console.WriteLine("从库初始化完成，复制触发器已创建");

                // 直接时间同步处理
                Timer? timeSyncTimer = null;
                if (config.TimeSynchronization.Enabled)
                {
                    Console.WriteLine("正在检查系统时间权限...");
                    if (!DirectTimeSynchronizationService.HasSystemTimePrivilege())
                    {
                        Console.WriteLine("检测到程序需要管理员权限才能修改系统时间");
                        Console.WriteLine("正在请求管理员权限...");
                        
                        // 尝试请求管理员权限
                        if (!DirectTimeSynchronizationService.RequestAdministratorPrivileges())
                        {
                            Console.WriteLine("错误：无法获取管理员权限");
                            Console.WriteLine("请手动以管理员身份重新运行此程序");
                            Console.WriteLine("按任意键退出...");
                            Console.ReadKey();
                            return;
                        }
                        // 如果成功请求权限，程序会自动重启，这里不会执行到
                    }
                    
                    Console.WriteLine("检测到管理员权限，启用直接时间同步功能");
                    
                    // 启动时同步
                    if (config.TimeSynchronization.SyncOnStartup)
                    {
                        Console.WriteLine("正在执行启动时间同步...");
                        bool syncResult = DirectTimeSynchronizationService.SynchronizeToLeaderTime(
                            leaderConnectionString, 
                            config.TimeSynchronization.MaxAllowedDifferenceSeconds);
                        Console.WriteLine(syncResult ? "启动时间同步完成" : "启动时间同步跳过（时间差异在阈值内或同步失败）");
                    }
                    
                    // 设置定期同步
                    if (config.TimeSynchronization.TimeSyncIntervalMinutes > 0)
                    {
                        timeSyncTimer = new Timer(_ =>
                        {
                            try
                            {
                                Console.WriteLine("执行定期时间同步检查...");
                                bool syncResult = DirectTimeSynchronizationService.SynchronizeToLeaderTime(
                                    leaderConnectionString, 
                                    config.TimeSynchronization.MaxAllowedDifferenceSeconds);
                                Console.WriteLine(syncResult ? "定期时间同步完成" : "定期时间同步跳过（时间差异在阈值内或同步失败）");
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"定期时间同步出错: {ex.Message}");
                            }
                        }, null, TimeSpan.FromMinutes(config.TimeSynchronization.TimeSyncIntervalMinutes), TimeSpan.FromMinutes(config.TimeSynchronization.TimeSyncIntervalMinutes));
                    }
                }

                // 创建并启动复制器
                var replicator = new DatabaseBasedFollowerReplicator(
                    followerServerId,
                    followerConnectionString,
                    leaderConnectionString,
                    tableConfigs,
                    config.BatchSize,
                    config.DataRetentionDays,
                    config.CleanupIntervalHours);

                // 启动复制过程（从配置文件读取参数）
                if (initializeExistingData)
                {
                    await replicator.InitializeExistingData(true);
                }
                replicator.StartReplication();
                Console.WriteLine("从库复制服务已启动，开始同步数据...");

                Console.WriteLine("按ESC键退出...");
                while (Console.ReadKey(true).Key != ConsoleKey.Escape) { }

                // 停止复制器
                replicator.StopReplication();
                
                // 停止时间同步定时器
                if (timeSyncTimer != null)
                {
                    timeSyncTimer.Dispose();
                    Console.WriteLine("时间同步定时器已停止");
                }
            }
        }

        /// <summary>
        /// 根据实体类型名称获取类型
        /// </summary>
        /// <param name="entityTypeName">实体类型名称</param>
        /// <returns>实体类型，如果找不到则返回null</returns>
        private static Type? GetEntityType(string entityTypeName)
        {
            // 在当前程序集中查找类型
            var currentAssembly = Assembly.GetExecutingAssembly();
            var type = currentAssembly.GetType($"Coldairarrow.bgmj.Entity.{entityTypeName}");
            
            if (type != null)
                return type;

            // 如果在当前程序集中找不到，尝试在所有已加载的程序集中查找
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = assembly.GetType($"Coldairarrow.bgmj.Entity.{entityTypeName}");
                if (type != null)
                    return type;

                // 也尝试不带命名空间的类型名
                type = assembly.GetTypes().FirstOrDefault(t => t.Name == entityTypeName);
                if (type != null)
                    return type;
            }

            return null;
        }
    }
}