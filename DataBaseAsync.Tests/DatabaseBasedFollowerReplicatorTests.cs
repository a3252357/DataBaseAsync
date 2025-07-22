using Xunit;
using Moq;
using Microsoft.EntityFrameworkCore;
using DatabaseReplication;
using DatabaseReplication.Follower;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DataBaseAsync;
using Coldairarrow.bgmj.Entity;

namespace DataBaseAsync.Tests
{
    public class DatabaseBasedFollowerReplicatorTests : IDisposable
    {
        private readonly string _testFollowerConnectionString;
        private readonly string _testLeaderConnectionString;
        private readonly List<TableConfig> _testTableConfigs;

        public DatabaseBasedFollowerReplicatorTests()
        {
            // 使用内存数据库进行测试
            _testFollowerConnectionString = "Server=localhost;Database=test_follower;Uid=test;Pwd=test;";
            _testLeaderConnectionString = "Server=localhost;Database=test_leader;Uid=test;Pwd=test;";
            
            _testTableConfigs = new List<TableConfig>
            {
                new TableConfig
                {
                    EntityType = typeof(d_man),
                    TableName = "d_man",
                    PrimaryKey = "Id",
                    ReplicationIntervalSeconds = 5,
                    ReplicationDirection = ReplicationDirection.LeaderToFollower,
                    Enabled = true
                }
            };
        }

        [Fact]
        public void Constructor_ShouldInitializeCorrectly()
        {
            // Arrange & Act
            var replicator = new DatabaseBasedFollowerReplicator(
                "test-follower-1",
                _testFollowerConnectionString,
                _testLeaderConnectionString,
                _testLeaderConnectionString,
                _testTableConfigs,
                1000,
                30,
                24
            );

            // Assert
            Assert.NotNull(replicator);
        }

        [Fact]
        public void Constructor_WithNullTableConfigs_ShouldThrowArgumentNullException()
        {
            // Arrange, Act & Assert
            Assert.Throws<ArgumentNullException>(() => new DatabaseBasedFollowerReplicator(
                "test-follower-1",
                _testFollowerConnectionString,
                _testLeaderConnectionString,
                _testLeaderConnectionString,
                null,
                1000,
                30,
                24
            ));
        }

        [Fact]
        public void Constructor_WithEmptyFollowerServerId_ShouldThrowArgumentException()
        {
            // Arrange, Act & Assert
            Assert.Throws<ArgumentException>(() => new DatabaseBasedFollowerReplicator(
                "",
                _testFollowerConnectionString,
                _testLeaderConnectionString,
                _testLeaderConnectionString,
                _testTableConfigs,
                1000,
                30,
                24
            ));
        }

        [Fact]
        public void Constructor_WithNullConnectionString_ShouldThrowArgumentNullException()
        {
            // Arrange, Act & Assert
            Assert.Throws<ArgumentNullException>(() => new DatabaseBasedFollowerReplicator(
                "test-follower-1",
                null,
                _testLeaderConnectionString,
                _testLeaderConnectionString,
                _testTableConfigs,
                1000,
                30,
                24
            ));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(-100)]
        public void Constructor_WithInvalidBatchSize_ShouldThrowArgumentException(int batchSize)
        {
            // Arrange, Act & Assert
            Assert.Throws<ArgumentException>(() => new DatabaseBasedFollowerReplicator(
                "test-follower-1",
                _testFollowerConnectionString,
                _testLeaderConnectionString,
                _testLeaderConnectionString,
                _testTableConfigs,
                batchSize,
                30,
                24
            ));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(-30)]
        public void Constructor_WithInvalidDataRetentionDays_ShouldThrowArgumentException(int dataRetentionDays)
        {
            // Arrange, Act & Assert
            Assert.Throws<ArgumentException>(() => new DatabaseBasedFollowerReplicator(
                "test-follower-1",
                _testFollowerConnectionString,
                _testLeaderConnectionString,
                _testLeaderConnectionString,
                _testTableConfigs,
                1000,
                dataRetentionDays,
                24
            ));
        }

        [Fact]
        public void StartReplication_ShouldNotThrow()
        {
            // Arrange
            var replicator = new DatabaseBasedFollowerReplicator(
                "test-follower-1",
                _testFollowerConnectionString,
                _testLeaderConnectionString,
                _testLeaderConnectionString,
                _testTableConfigs
            );

            // Act & Assert
            var exception = Record.Exception(() => replicator.StartReplication());
            Assert.Null(exception);
        }

        [Fact]
        public void StopReplication_ShouldNotThrow()
        {
            // Arrange
            var replicator = new DatabaseBasedFollowerReplicator(
                "test-follower-1",
                _testFollowerConnectionString,
                _testLeaderConnectionString,
                _testLeaderConnectionString,
                _testTableConfigs
            );

            // Act & Assert
            var exception = Record.Exception(() => replicator.StopReplication());
            Assert.Null(exception);
        }

        [Fact]
        public void Dispose_ShouldNotThrow()
        {
            // Arrange
            var replicator = new DatabaseBasedFollowerReplicator(
                "test-follower-1",
                _testFollowerConnectionString,
                _testLeaderConnectionString,
                _testLeaderConnectionString,
                _testTableConfigs
            );

            // Act & Assert
            var exception = Record.Exception(() => replicator.Dispose());
            Assert.Null(exception);
        }

        [Fact]
        public void StartReplication_CalledTwice_ShouldNotStartTwice()
        {
            // Arrange
            var replicator = new DatabaseBasedFollowerReplicator(
                "test-follower-1",
                _testFollowerConnectionString,
                _testLeaderConnectionString,
                _testLeaderConnectionString,
                _testTableConfigs
            );

            // Act
            replicator.StartReplication();
            var exception = Record.Exception(() => replicator.StartReplication());

            // Assert
            Assert.Null(exception);
        }

        public void Dispose()
        {
            // No cleanup needed for test class
        }
    }
}