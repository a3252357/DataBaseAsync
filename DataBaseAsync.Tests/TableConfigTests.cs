using Xunit;
using DatabaseReplication;
using Coldairarrow.bgmj.Entity;
using System;

namespace DataBaseAsync.Tests
{
    public class TableConfigTests
    {
        [Fact]
        public void TableConfig_DefaultValues_ShouldBeSetCorrectly()
        {
            // Arrange & Act
            var config = new TableConfig();

            // Assert
            Assert.Equal(5, config.ReplicationIntervalSeconds);
            Assert.True(config.InitializeExistingData);
            Assert.True(config.Enabled);
            Assert.Equal(ReplicationDirection.LeaderToFollower, config.ReplicationDirection);
        }

        [Fact]
        public void TableConfig_SetProperties_ShouldRetainValues()
        {
            // Arrange
            var config = new TableConfig();
            var entityType = typeof(d_man);
            var tableName = "test_table";
            var primaryKey = "Id";
            var interval = 10;
            var direction = ReplicationDirection.Bidirectional;

            // Act
            config.EntityType = entityType;
            config.TableName = tableName;
            config.PrimaryKey = primaryKey;
            config.ReplicationIntervalSeconds = interval;
            config.ReplicationDirection = direction;
            config.InitializeExistingData = false;
            config.Enabled = false;

            // Assert
            Assert.Equal(entityType, config.EntityType);
            Assert.Equal(tableName, config.TableName);
            Assert.Equal(primaryKey, config.PrimaryKey);
            Assert.Equal(interval, config.ReplicationIntervalSeconds);
            Assert.Equal(direction, config.ReplicationDirection);
            Assert.False(config.InitializeExistingData);
            Assert.False(config.Enabled);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(5)]
        [InlineData(60)]
        [InlineData(3600)]
        public void TableConfig_ValidReplicationInterval_ShouldBeAccepted(int interval)
        {
            // Arrange
            var config = new TableConfig();

            // Act
            config.ReplicationIntervalSeconds = interval;

            // Assert
            Assert.Equal(interval, config.ReplicationIntervalSeconds);
        }

        [Fact]
        public void TableConfig_WithCompleteConfiguration_ShouldBeValid()
        {
            // Arrange & Act
            var config = new TableConfig
            {
                EntityType = typeof(d_man),
                TableName = "d_man",
                PrimaryKey = "Id",
                ReplicationIntervalSeconds = 30,
                InitializeExistingData = true,
                Enabled = true,
                ReplicationDirection = ReplicationDirection.LeaderToFollower
            };

            // Assert
            Assert.NotNull(config.EntityType);
            Assert.NotNull(config.TableName);
            Assert.NotNull(config.PrimaryKey);
            Assert.True(config.ReplicationIntervalSeconds > 0);
            Assert.True(config.Enabled);
        }

        [Theory]
        [InlineData(ReplicationDirection.LeaderToFollower)]
        [InlineData(ReplicationDirection.FollowerToLeader)]
        [InlineData(ReplicationDirection.Bidirectional)]
        public void TableConfig_AllReplicationDirections_ShouldBeSupported(ReplicationDirection direction)
        {
            // Arrange
            var config = new TableConfig();

            // Act
            config.ReplicationDirection = direction;

            // Assert
            Assert.Equal(direction, config.ReplicationDirection);
        }

        [Fact]
        public void TableConfig_EnabledProperty_ShouldToggleCorrectly()
        {
            // Arrange
            var config = new TableConfig();
            Assert.True(config.Enabled); // Default should be true

            // Act & Assert
            config.Enabled = false;
            Assert.False(config.Enabled);

            config.Enabled = true;
            Assert.True(config.Enabled);
        }

        [Fact]
        public void TableConfig_InitializeExistingDataProperty_ShouldToggleCorrectly()
        {
            // Arrange
            var config = new TableConfig();
            Assert.True(config.InitializeExistingData); // Default should be true

            // Act & Assert
            config.InitializeExistingData = false;
            Assert.False(config.InitializeExistingData);

            config.InitializeExistingData = true;
            Assert.True(config.InitializeExistingData);
        }
    }
}