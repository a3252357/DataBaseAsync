using Xunit;
using DatabaseReplication.Leader;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace DataBaseAsync.Tests
{
    public class DatabaseReplicationConfigTests : IDisposable
    {
        private readonly string _testConfigDirectory;
        private readonly string _testConfigFile;

        public DatabaseReplicationConfigTests()
        {
            _testConfigDirectory = Path.Combine(Path.GetTempPath(), "ConfigTests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(_testConfigDirectory);
            _testConfigFile = Path.Combine(_testConfigDirectory, "appsettings.test.json");
        }

        [Fact]
        public void DatabaseReplicationConfig_DefaultConstructor_ShouldInitializeProperties()
        {
            // Arrange & Act
            var config = new DatabaseReplicationConfig();

            // Assert
            Assert.NotNull(config.Tables);
            Assert.Empty(config.Tables);
            Assert.Equal(string.Empty, config.LeaderConnectionString);
            Assert.Equal(string.Empty, config.FollowerConnectionString);
            Assert.Equal(string.Empty, config.FollowerServerId);
        }

        [Fact]
        public void DatabaseReplicationConfig_SetProperties_ShouldRetainValues()
        {
            // Arrange
            var config = new DatabaseReplicationConfig();
            var leaderConnectionString = "Server=leader;Database=test;";
            var followerConnectionString = "Server=follower;Database=test;";
            var followerServerId = "follower-001";
            var initializeExistingData = true;

            // Act
            config.LeaderConnectionString = leaderConnectionString;
            config.FollowerConnectionString = followerConnectionString;
            config.FollowerServerId = followerServerId;
            config.InitializeExistingData = initializeExistingData;

            // Assert
            Assert.Equal(leaderConnectionString, config.LeaderConnectionString);
            Assert.Equal(followerConnectionString, config.FollowerConnectionString);
            Assert.Equal(followerServerId, config.FollowerServerId);
            Assert.Equal(initializeExistingData, config.InitializeExistingData);
        }

        [Fact]
        public void DatabaseReplicationConfig_AddTable_ShouldIncreaseTablesCount()
        {
            // Arrange
            var config = new DatabaseReplicationConfig();
            var tableConfig = new TableConfigData
            {
                EntityTypeName = "TestEntity",
                TableName = "test_table",
                PrimaryKey = "Id",
                ReplicationIntervalSeconds = 5
            };

            // Act
            config.Tables.Add(tableConfig);

            // Assert
            Assert.Single(config.Tables);
            Assert.Equal(tableConfig, config.Tables[0]);
        }

        [Fact]
        public void TableConfigData_DefaultValues_ShouldBeSetCorrectly()
        {
            // Arrange & Act
            var tableConfig = new TableConfigData();

            // Assert
            Assert.Equal(0, tableConfig.ReplicationIntervalSeconds);
            Assert.Equal(string.Empty, tableConfig.ReplicationDirection);
            Assert.True(tableConfig.Enabled);
            Assert.True(tableConfig.InitializeExistingData);
        }

        [Fact]
        public void TableConfigData_SetProperties_ShouldRetainValues()
        {
            // Arrange
            var tableConfig = new TableConfigData();
            var entityTypeName = "TestEntity";
            var tableName = "test_table";
            var primaryKey = "Id";
            var interval = 10;
            var direction = "Bidirectional";

            // Act
            tableConfig.EntityTypeName = entityTypeName;
            tableConfig.TableName = tableName;
            tableConfig.PrimaryKey = primaryKey;
            tableConfig.ReplicationIntervalSeconds = interval;
            tableConfig.ReplicationDirection = direction;

            // Assert
            Assert.Equal(entityTypeName, tableConfig.EntityTypeName);
            Assert.Equal(tableName, tableConfig.TableName);
            Assert.Equal(primaryKey, tableConfig.PrimaryKey);
            Assert.Equal(interval, tableConfig.ReplicationIntervalSeconds);
            Assert.Equal(direction, tableConfig.ReplicationDirection);
        }

        [Theory]
        [InlineData("LeaderToFollower")]
        [InlineData("FollowerToLeader")]
        [InlineData("Bidirectional")]
        public void TableConfigData_ValidReplicationDirections_ShouldBeAccepted(string direction)
        {
            // Arrange
            var tableConfig = new TableConfigData();

            // Act
            tableConfig.ReplicationDirection = direction;

            // Assert
            Assert.Equal(direction, tableConfig.ReplicationDirection);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(5)]
        [InlineData(60)]
        [InlineData(3600)]
        public void TableConfigData_ValidReplicationIntervals_ShouldBeAccepted(int interval)
        {
            // Arrange
            var tableConfig = new TableConfigData();

            // Act
            tableConfig.ReplicationIntervalSeconds = interval;

            // Assert
            Assert.Equal(interval, tableConfig.ReplicationIntervalSeconds);
        }

        [Fact]
        public void DatabaseReplicationConfig_WithMultipleTables_ShouldMaintainOrder()
        {
            // Arrange
            var config = new DatabaseReplicationConfig();
            var table1 = new TableConfigData { EntityTypeName = "Entity1", TableName = "table1" };
            var table2 = new TableConfigData { EntityTypeName = "Entity2", TableName = "table2" };
            var table3 = new TableConfigData { EntityTypeName = "Entity3", TableName = "table3" };

            // Act
            config.Tables.Add(table1);
            config.Tables.Add(table2);
            config.Tables.Add(table3);

            // Assert
            Assert.Equal(3, config.Tables.Count);
            Assert.Equal(table1, config.Tables[0]);
            Assert.Equal(table2, config.Tables[1]);
            Assert.Equal(table3, config.Tables[2]);
        }

        [Fact]
        public void DatabaseReplicationConfig_JsonSerialization_ShouldWorkCorrectly()
        {
            // Arrange
            var originalConfig = new DatabaseReplicationConfig
            {
                LeaderConnectionString = "Server=leader;Database=test;",
                FollowerConnectionString = "Server=follower;Database=test;",
                FollowerServerId = "follower-001",
                InitializeExistingData = true,
                Tables = new List<TableConfigData>
                {
                    new TableConfigData
                    {
                        EntityTypeName = "TestEntity",
                        TableName = "test_table",
                        PrimaryKey = "Id",
                        ReplicationIntervalSeconds = 10,
                        ReplicationDirection = "Bidirectional"
                    }
                }
            };

            // Act
            var json = JsonSerializer.Serialize(originalConfig, new JsonSerializerOptions { WriteIndented = true });
            var deserializedConfig = JsonSerializer.Deserialize<DatabaseReplicationConfig>(json);

            // Assert
            Assert.NotNull(deserializedConfig);
            Assert.Equal(originalConfig.LeaderConnectionString, deserializedConfig.LeaderConnectionString);
            Assert.Equal(originalConfig.FollowerConnectionString, deserializedConfig.FollowerConnectionString);
            Assert.Equal(originalConfig.FollowerServerId, deserializedConfig.FollowerServerId);
            Assert.Equal(originalConfig.InitializeExistingData, deserializedConfig.InitializeExistingData);
            Assert.Single(deserializedConfig.Tables);
            Assert.Equal(originalConfig.Tables[0].EntityTypeName, deserializedConfig.Tables[0].EntityTypeName);
        }

        [Fact]
        public void DatabaseReplicationConfig_EmptyTables_ShouldBeValid()
        {
            // Arrange & Act
            var config = new DatabaseReplicationConfig
            {
                LeaderConnectionString = "Server=leader;Database=test;",
                FollowerConnectionString = "Server=follower;Database=test;",
                FollowerServerId = "follower-001",
                Tables = new List<TableConfigData>()
            };

            // Assert
            Assert.NotNull(config.Tables);
            Assert.Empty(config.Tables);
            Assert.NotNull(config.LeaderConnectionString);
            Assert.NotNull(config.FollowerConnectionString);
            Assert.NotNull(config.FollowerServerId);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_testConfigDirectory))
                {
                    Directory.Delete(_testConfigDirectory, true);
                }
            }
            catch
            {
                // 忽略清理错误
            }
        }
    }
}