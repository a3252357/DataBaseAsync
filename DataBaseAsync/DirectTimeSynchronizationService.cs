using System;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using System.Security.Principal;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using MySqlConnector;

namespace DataBaseAsync
{
    /// <summary>
    /// 直接修改系统时间的时间同步服务
    /// 注意：需要管理员权限才能修改系统时间
    /// </summary>
    public static class DirectTimeSynchronizationService
    {
        private static readonly ILogger _logger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger("DirectTimeSynchronizationService");

        // Windows API 用于设置系统时间
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetSystemTime(ref SYSTEMTIME st);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemTime(out SYSTEMTIME st);

        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEMTIME
        {
            public ushort wYear;
            public ushort wMonth;
            public ushort wDayOfWeek;
            public ushort wDay;
            public ushort wHour;
            public ushort wMinute;
            public ushort wSecond;
            public ushort wMilliseconds;
        }

        /// <summary>
        /// 检查主从库时间差异
        /// </summary>
        /// <param name="leaderConnectionString">主库连接字符串</param>
        /// <param name="followerConnectionString">从库连接字符串</param>
        /// <returns>时间差异（从库时间 - 主库时间）</returns>
        public static TimeSpan CheckTimeDifference(string leaderConnectionString, string followerConnectionString)
        {
            try
            {
                DateTime leaderTime = GetDatabaseTime(leaderConnectionString);
                DateTime followerTime = GetDatabaseTime(followerConnectionString);
                
                TimeSpan difference = followerTime - leaderTime;
                _logger.LogInformation($"时间差异检查: 主库时间={leaderTime:yyyy-MM-dd HH:mm:ss.ffffff}, 从库时间={followerTime:yyyy-MM-dd HH:mm:ss.ffffff}, 差异={difference.TotalSeconds:F3}秒");
                
                return difference;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "检查时间差异时发生错误");
                throw;
            }
        }

        /// <summary>
        /// 同步系统时间到主库时间
        /// </summary>
        /// <param name="leaderConnectionString">主库连接字符串</param>
        /// <param name="thresholdSeconds">时间差异阈值（秒）</param>
        /// <returns>是否执行了时间同步</returns>
        public static bool SynchronizeToLeaderTime(string leaderConnectionString, double thresholdSeconds = 30)
        {
            try
            {
                // 获取主库时间
                DateTime leaderTime = GetDatabaseTime(leaderConnectionString);
                DateTime systemTime = DateTime.Now;
                
                TimeSpan difference = systemTime - leaderTime;
                double differenceSeconds = Math.Abs(difference.TotalSeconds);
                
                _logger.LogInformation($"当前系统时间: {systemTime:yyyy-MM-dd HH:mm:ss.ffffff}");
                _logger.LogInformation($"主库时间: {leaderTime:yyyy-MM-dd HH:mm:ss.ffffff}");
                _logger.LogInformation($"时间差异: {difference.TotalSeconds:F3}秒");
                
                if (differenceSeconds <= thresholdSeconds)
                {
                    _logger.LogInformation($"时间差异({differenceSeconds:F3}秒)在阈值({thresholdSeconds}秒)内，无需同步");
                    return false;
                }
                
                // 设置系统时间为主库时间
                bool success = SetSystemTimeToDateTime(leaderTime);
                
                if (success)
                {
                    _logger.LogInformation($"系统时间已同步到主库时间: {leaderTime:yyyy-MM-dd HH:mm:ss.ffffff}");
                    return true;
                }
                else
                {
                    int errorCode = Marshal.GetLastWin32Error();
                    _logger.LogError($"设置系统时间失败，错误代码: {errorCode}。可能需要管理员权限。");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "同步系统时间时发生错误");
                return false;
            }
        }

        /// <summary>
        /// 获取数据库服务器时间
        /// </summary>
        /// <param name="connectionString">数据库连接字符串</param>
        /// <returns>数据库服务器时间</returns>
        private static DateTime GetDatabaseTime(string connectionString)
        {
            using (var connection = new MySqlConnection(connectionString))
            {
                connection.Open();
                using (var command = new MySqlCommand("SELECT NOW(6)", connection))
                {
                    var result = command.ExecuteScalar();
                    return Convert.ToDateTime(result);
                }
            }
        }

        /// <summary>
        /// 设置系统时间
        /// </summary>
        /// <param name="dateTime">要设置的时间</param>
        /// <returns>是否成功</returns>
        private static bool SetSystemTimeToDateTime(DateTime dateTime)
        {
            // 转换为UTC时间，因为SetSystemTime需要UTC时间
            DateTime utcTime = dateTime.ToUniversalTime();
            
            SYSTEMTIME systemTime = new SYSTEMTIME
            {
                wYear = (ushort)utcTime.Year,
                wMonth = (ushort)utcTime.Month,
                wDay = (ushort)utcTime.Day,
                wHour = (ushort)utcTime.Hour,
                wMinute = (ushort)utcTime.Minute,
                wSecond = (ushort)utcTime.Second,
                wMilliseconds = (ushort)utcTime.Millisecond
            };

            return SetSystemTime(ref systemTime);
        }

        /// <summary>
        /// 检查是否具有修改系统时间的权限
        /// </summary>
        /// <returns>如果具有权限返回true，否则返回false</returns>
        public static bool HasSystemTimePrivilege()
        {
            try
            {
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                {
                    WindowsPrincipal principal = new WindowsPrincipal(identity);
                    return principal.IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"检查系统时间权限时发生错误: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 请求管理员权限并重新启动程序
        /// </summary>
        /// <returns>如果成功请求权限并重启返回true，否则返回false</returns>
        public static bool RequestAdministratorPrivileges()
        {
            try
            {
                // 如果已经是管理员，直接返回true
                if (HasSystemTimePrivilege())
                {
                    return true;
                }

                _logger.LogInformation("正在请求管理员权限...");

                // 获取当前程序的路径
                string currentExecutable = Process.GetCurrentProcess().MainModule.FileName;
                
                // 创建进程启动信息
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = currentExecutable,
                    UseShellExecute = true,
                    Verb = "runas", // 请求管理员权限
                    Arguments = string.Join(" ", Environment.GetCommandLineArgs().Skip(1)) // 传递原始命令行参数
                };

                // 启动新的进程
                Process elevatedProcess = Process.Start(startInfo);
                
                if (elevatedProcess != null)
                {
                    _logger.LogInformation("已成功请求管理员权限，程序将重新启动");
                    // 当前进程退出
                    Environment.Exit(0);
                    return true;
                }
                else
                {
                    _logger.LogWarning("无法启动具有管理员权限的进程");
                    return false;
                }
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                // 用户取消了UAC提示
                _logger.LogWarning("用户取消了管理员权限请求");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError($"请求管理员权限时发生错误: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 获取当前系统时间（用于验证）
        /// </summary>
        /// <returns>当前系统时间</returns>
        public static DateTime GetCurrentSystemTime()
        {
            return DateTime.Now;
        }
    }
}