using MySqlConnector;
using System;
using System.Threading.Tasks;

namespace DataBaseAsync
{
    /// <summary>
    /// 直接时间同步功能测试类
    /// 注意：需要管理员权限才能执行直接时间修改
    /// </summary>
    public class DirectTimeSyncTest
    {
        private readonly string _leaderConnectionString;
        private readonly string _followerConnectionString;

        public DirectTimeSyncTest(string leaderConnectionString, string followerConnectionString)
        {
            _leaderConnectionString = leaderConnectionString;
            _followerConnectionString = followerConnectionString;
        }

        /// <summary>
        /// 测试直接时间同步功能
        /// </summary>
        public async Task TestDirectTimeSynchronization()
        {
            Console.WriteLine("=== 直接时间同步功能测试 ===");
            Console.WriteLine();

            // 1. 检查权限
            Console.WriteLine("1. 检查系统时间修改权限...");
            bool hasPrivilege = DirectTimeSynchronizationService.HasSystemTimePrivilege();
            Console.WriteLine($"   权限检查结果: {(hasPrivilege ? "有权限" : "无权限")}");
            
            if (!hasPrivilege)
            {
                Console.WriteLine("   警告：需要管理员权限才能修改系统时间！");
                Console.WriteLine("   请以管理员身份运行程序。");
                return;
            }
            Console.WriteLine();

            // 2. 显示当前时间状态
            Console.WriteLine("2. 当前时间状态:");
            await ShowCurrentTimeStatus();
            Console.WriteLine();

            // 3. 检查时间差异
            Console.WriteLine("3. 检查主从库时间差异...");
            try
            {
                TimeSpan difference = DirectTimeSynchronizationService.CheckTimeDifference(
                    _leaderConnectionString, _followerConnectionString);
                Console.WriteLine($"   时间差异: {difference.TotalSeconds:F3}秒");
                Console.WriteLine($"   差异详情: {(difference.TotalSeconds >= 0 ? "+" : "")}{difference.TotalSeconds:F6}秒");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   检查时间差异失败: {ex.Message}");
            }
            Console.WriteLine();

            // 4. 执行时间同步
            Console.WriteLine("4. 执行直接时间同步...");
            try
            {
                bool syncResult = DirectTimeSynchronizationService.SynchronizeToLeaderTime(
                    _leaderConnectionString, 30); // 30秒阈值
                Console.WriteLine($"   同步结果: {(syncResult ? "已同步" : "跳过同步（时间差异在阈值内）")}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   时间同步失败: {ex.Message}");
            }
            Console.WriteLine();

            // 5. 验证同步后的时间
            Console.WriteLine("5. 同步后时间状态:");
            await ShowCurrentTimeStatus();
            Console.WriteLine();

            // 6. 再次检查时间差异
            Console.WriteLine("6. 验证同步效果...");
            try
            {
                TimeSpan difference = DirectTimeSynchronizationService.CheckTimeDifference(
                    _leaderConnectionString, _followerConnectionString);
                Console.WriteLine($"   同步后时间差异: {difference.TotalSeconds:F3}秒");
                
                if (Math.Abs(difference.TotalSeconds) <= 30)
                {
                    Console.WriteLine($"   ✓ 时间同步成功，差异在可接受范围内");
                }
                else
                {
                    Console.WriteLine($"   ✗ 时间差异仍然较大，可能需要进一步调整");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   验证同步效果失败: {ex.Message}");
            }
            Console.WriteLine();

            Console.WriteLine("=== 直接时间同步测试完成 ===");
        }

        /// <summary>
        /// 显示当前时间状态
        /// </summary>
        private async Task ShowCurrentTimeStatus()
        {
            try
            {
                DateTime systemTime = DirectTimeSynchronizationService.GetCurrentSystemTime();
                Console.WriteLine($"   系统时间: {systemTime:yyyy-MM-dd HH:mm:ss.ffffff}");
                
                // 获取数据库时间进行对比
                using (var connection = new MySqlConnection(_leaderConnectionString))
                {
                    await connection.OpenAsync();
                    using (var command = new MySqlCommand("SELECT NOW(6)", connection))
                    {
                        var result = await command.ExecuteScalarAsync();
                        DateTime leaderTime = Convert.ToDateTime(result);
                        Console.WriteLine($"   主库时间: {leaderTime:yyyy-MM-dd HH:mm:ss.ffffff}");
                        
                        TimeSpan diff = systemTime - leaderTime;
                        Console.WriteLine($"   时间差异: {diff.TotalSeconds:F3}秒");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   获取时间状态失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 测试权限检查功能
        /// </summary>
        public static void TestPrivilegeCheck()
        {
            Console.WriteLine("=== 权限检查测试 ===");
            
            bool hasPrivilege = DirectTimeSynchronizationService.HasSystemTimePrivilege();
            Console.WriteLine($"系统时间修改权限: {(hasPrivilege ? "有权限" : "无权限")}");
            
            if (!hasPrivilege)
            {
                Console.WriteLine("建议:");
                Console.WriteLine("1. 以管理员身份重新运行程序");
                Console.WriteLine("2. 或者调用 RequestAdministratorPrivileges() 方法请求权限");
                
                Console.WriteLine("\n是否尝试请求管理员权限？(y/n):");
                var input = Console.ReadLine();
                if (input?.ToLower() == "y" || input?.ToLower() == "yes")
                {
                    Console.WriteLine("正在请求管理员权限...");
                    bool requestResult = DirectTimeSynchronizationService.RequestAdministratorPrivileges();
                    if (!requestResult)
                    {
                        Console.WriteLine("权限请求失败或被用户取消");
                    }
                }
            }
            else
            {
                Console.WriteLine("权限检查通过，可以进行直接时间同步");
            }
            
            Console.WriteLine();
        }

        /// <summary>
        /// 演示时间同步的完整流程
        /// </summary>
        public static async Task DemoDirectTimeSync(string leaderConnectionString, string followerConnectionString)
        {
            Console.WriteLine("=== 直接时间同步演示 ===");
            Console.WriteLine();

            var test = new DirectTimeSyncTest(leaderConnectionString, followerConnectionString);
            
            // 首先检查权限
            TestPrivilegeCheck();
            
            // 如果有权限，执行完整测试
            if (DirectTimeSynchronizationService.HasSystemTimePrivilege())
            {
                await test.TestDirectTimeSynchronization();
            }
            else
            {
                Console.WriteLine("由于没有管理员权限，跳过直接时间同步测试。");
                Console.WriteLine("请以管理员身份重新运行程序以测试直接时间同步功能。");
            }
        }
    }
}