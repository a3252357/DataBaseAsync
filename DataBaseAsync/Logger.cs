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
        private string _logFilePath;
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private readonly object _lockObject = new object();
        private const long MaxLogFileSizeBytes = 10 * 1024 * 1024; // 10MB
        private long _currentLogSizeBytes = 0; // 当前日志文件累计大小

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
            
            // 初始化当前文件大小
            InitializeCurrentFileSize();
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
                
                // 检查并轮转日志文件
                CheckAndRotateLogFile();
                
                // 输出到文件
                try
                {
                    string logLine = formattedMessage + Environment.NewLine;
                    File.AppendAllText(_logFilePath, logLine);
                    
                    // 累计文件大小（估算）
                    _currentLogSizeBytes += System.Text.Encoding.UTF8.GetByteCount(logLine);
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
                
                // 检查并轮转日志文件
                CheckAndRotateLogFile();
                
                // 异步输出到文件
                try
                {
                    string logLine = formattedMessage + Environment.NewLine;
                    await File.AppendAllTextAsync(_logFilePath, logLine);
                    
                    // 累计文件大小（估算）
                    _currentLogSizeBytes += System.Text.Encoding.UTF8.GetByteCount(logLine);
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
        /// 初始化当前文件大小
        /// </summary>
        private void InitializeCurrentFileSize()
        {
            try
            {
                if (File.Exists(_logFilePath))
                {
                    var fileInfo = new FileInfo(_logFilePath);
                    _currentLogSizeBytes = fileInfo.Length;
                }
                else
                {
                    _currentLogSizeBytes = 0;
                }
            }
            catch
            {
                _currentLogSizeBytes = 0;
            }
        }

        /// <summary>
        /// 检查并轮转日志文件
        /// </summary>
        private void CheckAndRotateLogFile()
        {
            try
            {
                // 使用累计大小检查，避免频繁的文件系统调用
                if (_currentLogSizeBytes >= MaxLogFileSizeBytes)
                {
                    // 创建新的日志文件名
                    string logDirectory = Path.GetDirectoryName(_logFilePath);
                    string baseFileName = $"DatabaseReplication_{DateTime.Now:yyyyMMdd}";
                    string extension = ".log";
                    
                    // 查找当前日期的日志文件数量，生成序号
                    int fileIndex = 1;
                    string newFileName;
                    do
                    {
                        newFileName = Path.Combine(logDirectory, $"{baseFileName}_{fileIndex:D3}{extension}");
                        fileIndex++;
                    }
                    while (File.Exists(newFileName));
                    
                    // 更新日志文件路径
                    _logFilePath = newFileName;
                    
                    // 重置累计大小
                    _currentLogSizeBytes = 0;
                    
                    // 记录日志轮转信息到新文件
                    string rotationMessage = $"日志文件轮转: 前一个文件已达到大小限制 ({MaxLogFileSizeBytes / (1024 * 1024)}MB)";
                    Console.WriteLine(FormatMessage(rotationMessage, LogLevel.Info));
                }
            }
            catch (Exception ex)
            {
                // 如果轮转失败，记录错误但不影响正常日志写入
                Console.WriteLine($"[ERROR] 日志文件轮转失败: {ex.Message}");
            }
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