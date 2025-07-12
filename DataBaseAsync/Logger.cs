using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DataBaseAsync
{
    /// <summary>
    /// 日志管理器，支持同时输出到控制台和文件，使用队列优化性能
    /// </summary>
    public class Logger : IDisposable
    {
        private static readonly Lazy<Logger> _instance = new Lazy<Logger>(() => new Logger());
        private string _logFilePath;
        private readonly ConcurrentQueue<LogEntry> _logQueue = new ConcurrentQueue<LogEntry>();
        private readonly AutoResetEvent _logEvent = new AutoResetEvent(false);
        private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
        private readonly Task _backgroundTask;
        private const long MaxLogFileSizeBytes = 10 * 1024 * 1024; // 10MB
        private long _currentLogSizeBytes = 0; // 当前日志文件累计大小
        private const int MaxQueueSize = 10000; // 最大队列大小，防止内存溢出
        private volatile bool _disposed = false;

        public static Logger Instance => _instance.Value;

        /// <summary>
        /// 日志条目结构
        /// </summary>
        private struct LogEntry
        {
            public string Message { get; set; }
            public LogLevel Level { get; set; }
            public DateTime Timestamp { get; set; }
        }

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
            
            // 启动后台日志处理任务
            _backgroundTask = Task.Run(ProcessLogQueue, _cancellationTokenSource.Token);
        }

        /// <summary>
        /// 写入日志信息（非阻塞，使用队列）
        /// </summary>
        /// <param name="message">日志消息</param>
        /// <param name="level">日志级别</param>
        public void WriteLine(string message, LogLevel level = LogLevel.Info)
        {
            if (_disposed) return;
            
            // 检查队列大小，防止内存溢出
            if (_logQueue.Count >= MaxQueueSize)
            {
                // 队列满时，直接输出到控制台，跳过文件写入
                Console.WriteLine($"[WARNING] 日志队列已满，跳过文件写入: {message}");
                return;
            }
            
            var logEntry = new LogEntry
            {
                Message = message,
                Level = level,
                Timestamp = DateTime.Now
            };
            
            _logQueue.Enqueue(logEntry);
            _logEvent.Set(); // 通知后台线程处理
        }

        /// <summary>
        /// 后台处理日志队列
        /// </summary>
        private async Task ProcessLogQueue()
        {
            while (!_cancellationTokenSource.Token.IsCancellationRequested)
            {
                try
                {
                    // 等待日志事件或超时（1秒）
                    _logEvent.WaitOne(1000);
                    
                    // 批量处理队列中的日志
                    var processedCount = 0;
                    while (_logQueue.TryDequeue(out var logEntry) && processedCount < 100) // 每次最多处理100条
                    {
                        await ProcessSingleLogEntry(logEntry);
                        processedCount++;
                    }
                }
                catch (Exception ex)
                {
                    // 后台处理出错时，直接输出到控制台
                    Console.WriteLine($"[ERROR] 后台日志处理出错: {ex.Message}");
                }
            }
        }
        
        /// <summary>
        /// 处理单个日志条目
        /// </summary>
        private async Task ProcessSingleLogEntry(LogEntry logEntry)
        {
            try
            {
                string formattedMessage = FormatMessage(logEntry.Message, logEntry.Level, logEntry.Timestamp);
                
                // 输出到控制台
                Console.WriteLine(formattedMessage);
                
                // 检查并轮转日志文件
                CheckAndRotateLogFile();
                
                // 异步输出到文件
                string logLine = formattedMessage + Environment.NewLine;
                await File.AppendAllTextAsync(_logFilePath, logLine);
                
                // 累计文件大小（估算）- 使用原子操作
                var messageSize = System.Text.Encoding.UTF8.GetByteCount(logLine);
                Interlocked.Add(ref _currentLogSizeBytes, messageSize);
            }
            catch (Exception ex)
            {
                // 如果文件写入失败，只在控制台显示错误
                Console.WriteLine($"[ERROR] 写入日志文件失败: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 异步写入日志信息（兼容性方法，实际使用队列）
        /// </summary>
        /// <param name="message">日志消息</param>
        /// <param name="level">日志级别</param>
        public async Task WriteLineAsync(string message, LogLevel level = LogLevel.Info)
        {
            WriteLine(message, level); // 直接使用队列方式
            await Task.CompletedTask; // 保持异步接口兼容性
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
            return FormatMessage(message, level, DateTime.Now);
        }
        
        /// <summary>
        /// 格式化日志消息（带时间戳）
        /// </summary>
        private string FormatMessage(string message, LogLevel level, DateTime timestamp)
        {
            string timestampStr = timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff");
            string levelStr = level.ToString().ToUpper().PadRight(7);
            return $"[{timestampStr}] [{levelStr}] {message}";
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
        /// 检查并轮转日志文件（线程安全）
        /// </summary>
        private void CheckAndRotateLogFile()
        {
            try
            {
                // 使用原子读取检查大小
                var currentSize = Interlocked.Read(ref _currentLogSizeBytes);
                if (currentSize >= MaxLogFileSizeBytes)
                {
                    lock (this) // 确保轮转操作的原子性
                    {
                        // 双重检查，防止多线程重复轮转
                        if (Interlocked.Read(ref _currentLogSizeBytes) >= MaxLogFileSizeBytes)
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
                            Interlocked.Exchange(ref _currentLogSizeBytes, 0);
                            
                            // 记录日志轮转信息到新文件
                            string rotationMessage = $"日志文件轮转: 前一个文件已达到大小限制 ({MaxLogFileSizeBytes / (1024 * 1024)}MB)";
                            Console.WriteLine(FormatMessage(rotationMessage, LogLevel.Info));
                        }
                    }
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
        
        /// <summary>
        /// 刷新日志队列，确保所有日志都被写入
        /// </summary>
        public async Task FlushAsync(int timeoutMs = 5000)
        {
            if (_disposed) return;
            
            var startTime = DateTime.Now;
            while (_logQueue.Count > 0 && (DateTime.Now - startTime).TotalMilliseconds < timeoutMs)
            {
                await Task.Delay(50);
            }
        }
        
        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            
            _disposed = true;
            
            // 等待队列处理完成
            var flushTask = FlushAsync();
            flushTask.Wait(5000); // 最多等待5秒
            
            // 停止后台任务
            _cancellationTokenSource.Cancel();
            
            try
            {
                _backgroundTask?.Wait(3000); // 最多等待3秒
            }
            catch (AggregateException)
            {
                // 忽略取消异常
            }
            
            // 释放资源
            _logEvent?.Dispose();
            _cancellationTokenSource?.Dispose();
        }
        
        /// <summary>
        /// 析构函数
        /// </summary>
        ~Logger()
        {
            Dispose();
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