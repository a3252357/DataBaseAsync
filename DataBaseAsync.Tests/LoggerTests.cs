using Xunit;
using DataBaseAsync;
using System;
using System.IO;
using System.Threading.Tasks;

namespace DataBaseAsync.Tests
{
    public class LoggerTests : IDisposable
    {
        private readonly string _testLogDirectory;
        private readonly Logger _logger;

        public LoggerTests()
        {
            _testLogDirectory = Path.Combine(Path.GetTempPath(), "LoggerTests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(_testLogDirectory);
            _logger = Logger.Instance;
        }

        [Fact]
        public void Instance_ShouldReturnSameInstance()
        {
            // Arrange & Act
            var logger1 = Logger.Instance;
            var logger2 = Logger.Instance;

            // Assert
            Assert.Same(logger1, logger2);
        }

        [Fact]
        public void Info_WithValidMessage_ShouldNotThrow()
        {
            // Arrange
            var message = "Test info message";

            // Act & Assert
            var exception = Record.Exception(() => _logger.Info(message));
            Assert.Null(exception);
        }

        [Fact]
        public void Error_WithValidMessage_ShouldNotThrow()
        {
            // Arrange
            var message = "Test error message";

            // Act & Assert
            var exception = Record.Exception(() => _logger.Error(message));
            Assert.Null(exception);
        }

        [Fact]
        public void Warning_WithValidMessage_ShouldNotThrow()
        {
            // Arrange
            var message = "Test warning message";

            // Act & Assert
            var exception = Record.Exception(() => _logger.Warning(message));
            Assert.Null(exception);
        }

        [Fact]
        public void Debug_WithValidMessage_ShouldNotThrow()
        {
            // Arrange
            var message = "Test debug message";

            // Act & Assert
            var exception = Record.Exception(() => _logger.Debug(message));
            Assert.Null(exception);
        }

        [Fact]
        public void Info_WithNullMessage_ShouldNotThrow()
        {
            // Arrange, Act & Assert
            var exception = Record.Exception(() => _logger.Info(null));
            Assert.Null(exception);
        }

        [Fact]
        public void Error_WithNullMessage_ShouldNotThrow()
        {
            // Arrange, Act & Assert
            var exception = Record.Exception(() => _logger.Error(null));
            Assert.Null(exception);
        }

        [Fact]
        public void Info_WithEmptyMessage_ShouldNotThrow()
        {
            // Arrange, Act & Assert
            var exception = Record.Exception(() => _logger.Info(""));
            Assert.Null(exception);
        }

        [Fact]
        public void Error_WithEmptyMessage_ShouldNotThrow()
        {
            // Arrange, Act & Assert
            var exception = Record.Exception(() => _logger.Error(""));
            Assert.Null(exception);
        }

        [Fact]
        public void Info_WithLongMessage_ShouldNotThrow()
        {
            // Arrange
            var longMessage = new string('A', 10000);

            // Act & Assert
            var exception = Record.Exception(() => _logger.Info(longMessage));
            Assert.Null(exception);
        }

        [Fact]
        public void Error_WithException_ShouldNotThrow()
        {
            // Arrange
            var message = "Test error with exception";
            var testException = new InvalidOperationException("Test exception");

            // Act & Assert
            var exception = Record.Exception(() => _logger.Error(message, testException));
            Assert.Null(exception);
        }

        [Fact]
        public async Task MultipleConcurrentLogs_ShouldNotThrow()
        {
            // Arrange
            var tasks = new Task[10];

            // Act
            for (int i = 0; i < tasks.Length; i++)
            {
                int taskId = i;
                tasks[i] = Task.Run(() =>
                {
                    for (int j = 0; j < 100; j++)
                    {
                        _logger.Info($"Task {taskId} - Message {j}");
                        _logger.Error($"Task {taskId} - Error {j}");
                        _logger.Warning($"Task {taskId} - Warning {j}");
                        _logger.Debug($"Task {taskId} - Debug {j}");
                    }
                });
            }

            // Assert
            var exception = await Record.ExceptionAsync(async () => await Task.WhenAll(tasks));
            Assert.Null(exception);
        }

        [Fact]
        public void LogMethods_WithSpecialCharacters_ShouldNotThrow()
        {
            // Arrange
            var specialMessage = "Test message with special chars: \n\r\t\"\'\\ 中文 🚀";

            // Act & Assert
            var exception = Record.Exception(() =>
            {
                _logger.Info(specialMessage);
                _logger.Error(specialMessage);
                _logger.Warning(specialMessage);
                _logger.Debug(specialMessage);
            });
            Assert.Null(exception);
        }

        [Theory]
        [InlineData("Simple message")]
        [InlineData("Message with numbers 12345")]
        [InlineData("Message with symbols !@#$%^&*()")]
        [InlineData("中文消息测试")]
        public void Info_WithVariousMessages_ShouldNotThrow(string message)
        {
            // Act & Assert
            var exception = Record.Exception(() => _logger.Info(message));
            Assert.Null(exception);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_testLogDirectory))
                {
                    Directory.Delete(_testLogDirectory, true);
                }
            }
            catch
            {
                // 忽略清理错误
            }
        }
    }
}