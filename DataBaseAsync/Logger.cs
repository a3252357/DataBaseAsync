using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DataBaseAsync
{
    /// <summary>
    /// 日志管理器，支持同时输出到控制台和文件
    /// </summary>
    public class Logger
    {
        private static readonly Lazy<Logger> _instance = new Lazy<Logger>(() => new Logger());
        private readonly string _logFilePath;
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private readonly object _lockObject = new object();

        public static Logger Instance => _instance.Value;

        private Logger()
        {
            // 创建日志目录
            string logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
            if (!Directory.Exists(logDirectory))
            {
                Directory.CreateDirectory(logDirectory);
            }

            // 生成日志文件名（按日期）
            string fileName = $"DatabaseReplication_{DateTime.Now:yyyyMMdd}.log";
            _logFilePath = Path.Combine(logDirectory, fileName);
        }

        /// <summary>
        /// 写入日志信息
        /// </summary>
        /// <param name="message">日志消息</param>
        /// <param name="level">日志级别</param>
        public void WriteLine(string message, LogLevel level = LogLevel.Info)
        {
            string formattedMessage = FormatMessage(message, level);
            
            lock (_lockObject)
            {
                // 输出到控制台
                Console.WriteLine(formattedMessage);
                
                // 输出到文件
                try
                {
                    File.AppendAllText(_logFilePath, formattedMessage + Environment.NewLine);
                }
                catch (Exception ex)
                {
                    // 如果文件写入失败，只在控制台显示错误
                    Console.WriteLine($"[ERROR] 写入日志文件失败: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 异步写入日志信息
        /// </summary>
        /// <param name="message">日志消息</param>
        /// <param name="level">日志级别</param>
        public async Task WriteLineAsync(string message, LogLevel level = LogLevel.Info)
        {
            string formattedMessage = FormatMessage(message, level);
            
            await _semaphore.WaitAsync();
            try
            {
                // 输出到控制台
                Console.WriteLine(formattedMessage);
                
                // 异步输出到文件
                try
                {
                    await File.AppendAllTextAsync(_logFilePath, formattedMessage + Environment.NewLine);
                }
                catch (Exception ex)
                {
                    // 如果文件写入失败，只在控制台显示错误
                    Console.WriteLine($"[ERROR] 写入日志文件失败: {ex.Message}");
                }
            }
            finally
            {
                _semaphore.Release();
            }
        }

        /// <summary>
        /// 写入信息级别日志
        /// </summary>
        public void Info(string message) => WriteLine(message, LogLevel.Info);

        /// <summary>
        /// 写入警告级别日志
        /// </summary>
        public void Warning(string message) => WriteLine(message, LogLevel.Warning);

        /// <summary>
        /// 写入错误级别日志
        /// </summary>
        public void Error(string message) => WriteLine(message, LogLevel.Error);

        /// <summary>
        /// 写入错误级别日志（包含异常信息）
        /// </summary>
        public void Error(string message, Exception exception) => WriteLine($"{message}: {exception.Message}\n{exception.StackTrace}", LogLevel.Error);

        /// <summary>
        /// 写入调试级别日志
        /// </summary>
        public void Debug(string message) => WriteLine(message, LogLevel.Debug);

        /// <summary>
        /// 异步写入信息级别日志
        /// </summary>
        public async Task InfoAsync(string message) => await WriteLineAsync(message, LogLevel.Info);

        /// <summary>
        /// 异步写入警告级别日志
        /// </summary>
        public async Task WarningAsync(string message) => await WriteLineAsync(message, LogLevel.Warning);

        /// <summary>
        /// 异步写入错误级别日志
        /// </summary>
        public async Task ErrorAsync(string message) => await WriteLineAsync(message, LogLevel.Error);

        /// <summary>
        /// 异步写入错误级别日志（包含异常信息）
        /// </summary>
        public async Task ErrorAsync(string message, Exception exception) => await WriteLineAsync($"{message}: {exception.Message}\n{exception.StackTrace}", LogLevel.Error);

        /// <summary>
        /// 异步写入调试级别日志
        /// </summary>
        public async Task DebugAsync(string message) => await WriteLineAsync(message, LogLevel.Debug);

        /// <summary>
        /// 格式化日志消息
        /// </summary>
        private string FormatMessage(string message, LogLevel level)
        {
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            string levelStr = level.ToString().ToUpper().PadRight(7);
            return $"[{timestamp}] [{levelStr}] {message}";
        }

        /// <summary>
        /// 清理旧日志文件（保留指定天数）
        /// </summary>
        /// <param name="retentionDays">保留天数</param>
        public void CleanupOldLogs(int retentionDays = 30)
        {
            try
            {
                string logDirectory = Path.GetDirectoryName(_logFilePath);
                if (Directory.Exists(logDirectory))
                {
                    var cutoffDate = DateTime.Now.AddDays(-retentionDays);
                    var logFiles = Directory.GetFiles(logDirectory, "DatabaseReplication_*.log");
                    
                    foreach (var file in logFiles)
                    {
                        var fileInfo = new FileInfo(file);
                        if (fileInfo.CreationTime < cutoffDate)
                        {
                            File.Delete(file);
                            WriteLine($"已删除过期日志文件: {Path.GetFileName(file)}", LogLevel.Info);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                WriteLine($"清理旧日志文件时出错: {ex.Message}", LogLevel.Error);
            }
        }
    }

    /// <summary>
    /// 日志级别枚举
    /// </summary>
    public enum LogLevel
    {
        Debug,
        Info,
        Warning,
        Error
    }
}