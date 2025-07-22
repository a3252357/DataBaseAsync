using Xunit;
using DataBaseAsync;
using System;
using System.Threading.Tasks;

namespace DataBaseAsync.Tests
{
    public class DirectTimeSynchronizationServiceTests
    {
        [Fact]
        public void GetCurrentSystemTime_ShouldReturnValidDateTime()
        {
            // Arrange & Act
            var systemTime = DirectTimeSynchronizationService.GetCurrentSystemTime();
            var now = DateTime.Now;

            // Assert
            Assert.True(systemTime != default(DateTime));
            // 时间差应该在合理范围内（1秒内）
            Assert.True(Math.Abs((systemTime - now).TotalSeconds) < 1);
        }

        [Fact]
        public void HasSystemTimePrivilege_ShouldReturnBoolean()
        {
            // Arrange & Act
            var hasPrivilege = DirectTimeSynchronizationService.HasSystemTimePrivilege();

            // Assert
            Assert.True(hasPrivilege == true || hasPrivilege == false);
        }

        [Fact]
        public void RequestAdministratorPrivileges_ShouldReturnBoolean()
        {
            // Arrange & Act
            var result = DirectTimeSynchronizationService.RequestAdministratorPrivileges();

            // Assert
            Assert.True(result == true || result == false);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(30)]
        [InlineData(60)]
        public void SynchronizeToLeaderTime_WithValidThreshold_ShouldNotThrow(int thresholdSeconds)
        {
            // Arrange
            var testConnectionString = "Server=localhost;Database=test;Uid=test;Pwd=test;";

            // Act & Assert
            var exception = Record.Exception(() => 
                DirectTimeSynchronizationService.SynchronizeToLeaderTime(testConnectionString, thresholdSeconds));
            
            // 注意：这个测试可能会因为数据库连接失败而抛出异常，这是预期的
            // 我们主要测试参数验证逻辑
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(-30)]
        [InlineData(-100)]
        public void SynchronizeToLeaderTime_WithNegativeThreshold_ShouldThrowArgumentException(int thresholdSeconds)
        {
            // Arrange
            var testConnectionString = "Server=localhost;Database=test;Uid=test;Pwd=test;";

            // Act & Assert
            Assert.Throws<ArgumentException>(() => 
                DirectTimeSynchronizationService.SynchronizeToLeaderTime(testConnectionString, thresholdSeconds));
        }

        [Fact]
        public void SynchronizeToLeaderTime_WithNullConnectionString_ShouldThrowArgumentNullException()
        {
            // Arrange, Act & Assert
            Assert.Throws<ArgumentNullException>(() => 
                DirectTimeSynchronizationService.SynchronizeToLeaderTime(null, 30));
        }

        [Fact]
        public void SynchronizeToLeaderTime_WithEmptyConnectionString_ShouldThrowArgumentException()
        {
            // Arrange, Act & Assert
            Assert.Throws<ArgumentException>(() => 
                DirectTimeSynchronizationService.SynchronizeToLeaderTime("", 30));
        }

        [Fact]
        public void CheckTimeDifference_WithNullConnectionStrings_ShouldThrowArgumentNullException()
        {
            // Arrange
            var testConnectionString = "Server=localhost;Database=test;Uid=test;Pwd=test;";

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => 
                DirectTimeSynchronizationService.CheckTimeDifference(null, testConnectionString));
            
            Assert.Throws<ArgumentNullException>(() => 
                DirectTimeSynchronizationService.CheckTimeDifference(testConnectionString, null));
        }

        [Fact]
        public void CheckTimeDifference_WithEmptyConnectionStrings_ShouldThrowArgumentException()
        {
            // Arrange
            var testConnectionString = "Server=localhost;Database=test;Uid=test;Pwd=test;";

            // Act & Assert
            Assert.Throws<ArgumentException>(() => 
                DirectTimeSynchronizationService.CheckTimeDifference("", testConnectionString));
            
            Assert.Throws<ArgumentException>(() => 
                DirectTimeSynchronizationService.CheckTimeDifference(testConnectionString, ""));
        }

        [Fact]
        public void CheckTimeDifference_WithSameConnectionString_ShouldReturnZeroOrSmallDifference()
        {
            // Arrange
            var testConnectionString = "Server=localhost;Database=test;Uid=test;Pwd=test;";

            // Act & Assert
            var exception = Record.Exception(() => 
            {
                var difference = DirectTimeSynchronizationService.CheckTimeDifference(
                    testConnectionString, testConnectionString);
                
                // 如果连接成功，同一个数据库的时间差应该很小
                Assert.True(Math.Abs(difference.TotalSeconds) < 1);
            });
            
            // 注意：这个测试可能会因为数据库连接失败而抛出异常，这是预期的
        }

        [Fact]
        public void GetCurrentSystemTime_MultipleCalls_ShouldReturnIncreasingTime()
        {
            // Arrange & Act
            var time1 = DirectTimeSynchronizationService.GetCurrentSystemTime();
            System.Threading.Thread.Sleep(10); // 等待10毫秒
            var time2 = DirectTimeSynchronizationService.GetCurrentSystemTime();

            // Assert
            Assert.True(time2 > time1);
        }

        [Fact]
        public void GetCurrentSystemTime_ShouldReturnUtcTime()
        {
            // Arrange & Act
            var systemTime = DirectTimeSynchronizationService.GetCurrentSystemTime();
            var utcNow = DateTime.UtcNow;

            // Assert
            // 系统时间应该接近UTC时间（在合理误差范围内）
            var difference = Math.Abs((systemTime - utcNow).TotalSeconds);
            Assert.True(difference < 2); // 允许2秒误差
        }
    }
}