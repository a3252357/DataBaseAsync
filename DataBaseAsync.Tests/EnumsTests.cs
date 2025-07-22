using Xunit;
using DatabaseReplication;
using System;
using System.Linq;

namespace DataBaseAsync.Tests
{
    public class EnumsTests
    {
        [Fact]
        public void ReplicationOperation_ShouldHaveExpectedValues()
        {
            // Arrange
            var expectedValues = new[] { "Insert", "Update", "Delete" };

            // Act
            var actualValues = Enum.GetNames(typeof(ReplicationOperation));

            // Assert
            Assert.Equal(expectedValues.Length, actualValues.Length);
            foreach (var expectedValue in expectedValues)
            {
                Assert.Contains(expectedValue, actualValues);
            }
        }

        [Fact]
        public void ReplicationDirection_ShouldHaveExpectedValues()
        {
            // Arrange
            var expectedValues = new[] { "LeaderToFollower", "FollowerToLeader", "Bidirectional" };

            // Act
            var actualValues = Enum.GetNames(typeof(ReplicationDirection));

            // Assert
            Assert.Equal(expectedValues.Length, actualValues.Length);
            foreach (var expectedValue in expectedValues)
            {
                Assert.Contains(expectedValue, actualValues);
            }
        }

        [Fact]
        public void ConflictResolutionStrategy_ShouldHaveExpectedValues()
        {
            // Arrange
            var expectedValues = new[] { "PreferLeader", "PreferFollower", "LastWriteWins", "Custom", "FieldPriority", "ManualReview" };

            // Act
            var actualValues = Enum.GetNames(typeof(ConflictResolutionStrategy));

            // Assert
            Assert.Equal(expectedValues.Length, actualValues.Length);
            foreach (var expectedValue in expectedValues)
            {
                Assert.Contains(expectedValue, actualValues);
            }
        }

        [Fact]
        public void ConflictType_ShouldHaveExpectedValues()
        {
            // Arrange
            var expectedValues = new[] { "ConcurrentUpdate", "DeleteAfterUpdate", "UpdateAfterDelete", "DuplicateInsert", "VersionMismatch" };

            // Act
            var actualValues = Enum.GetNames(typeof(ConflictType));

            // Assert
            Assert.Equal(expectedValues.Length, actualValues.Length);
            foreach (var expectedValue in expectedValues)
            {
                Assert.Contains(expectedValue, actualValues);
            }
        }

        [Fact]
        public void ConflictResolutionResult_ShouldHaveExpectedValues()
        {
            // Arrange
            var expectedValues = new[] { "ResolvedAutomatically", "RequiresManualReview", "Failed", "Skipped" };

            // Act
            var actualValues = Enum.GetNames(typeof(ConflictResolutionResult));

            // Assert
            Assert.Equal(expectedValues.Length, actualValues.Length);
            foreach (var expectedValue in expectedValues)
            {
                Assert.Contains(expectedValue, actualValues);
            }
        }

        [Fact]
        public void ReplicationStatus_ShouldHaveExpectedValues()
        {
            // Arrange
            var expectedValues = new[] { "Running", "Stopped", "Error" };

            // Act
            var actualValues = Enum.GetNames(typeof(ReplicationStatus));

            // Assert
            Assert.Equal(expectedValues.Length, actualValues.Length);
            foreach (var expectedValue in expectedValues)
            {
                Assert.Contains(expectedValue, actualValues);
            }
        }

        [Fact]
        public void TableSyncMode_ShouldHaveExpectedValues()
        {
            // Arrange
            var expectedValues = new[] { "Entity", "NoEntity" };

            // Act
            var actualValues = Enum.GetNames(typeof(TableSyncMode));

            // Assert
            Assert.Equal(expectedValues.Length, actualValues.Length);
            foreach (var expectedValue in expectedValues)
            {
                Assert.Contains(expectedValue, actualValues);
            }
        }

        [Fact]
        public void SchemaSyncStrategy_ShouldHaveExpectedValues()
        {
            // Arrange
            var expectedValues = new[] { "Disabled", "OnStartup", "Periodic", "OnStartupAndPeriodic" };

            // Act
            var actualValues = Enum.GetNames(typeof(SchemaSyncStrategy));

            // Assert
            Assert.Equal(expectedValues.Length, actualValues.Length);
            foreach (var expectedValue in expectedValues)
            {
                Assert.Contains(expectedValue, actualValues);
            }
        }

        [Theory]
        [InlineData(ReplicationOperation.Insert)]
        [InlineData(ReplicationOperation.Update)]
        [InlineData(ReplicationOperation.Delete)]
        public void ReplicationOperation_AllValues_ShouldBeValid(ReplicationOperation operation)
        {
            // Act & Assert
            Assert.True(Enum.IsDefined(typeof(ReplicationOperation), operation));
        }

        [Theory]
        [InlineData(ReplicationDirection.LeaderToFollower)]
        [InlineData(ReplicationDirection.FollowerToLeader)]
        [InlineData(ReplicationDirection.Bidirectional)]
        public void ReplicationDirection_AllValues_ShouldBeValid(ReplicationDirection direction)
        {
            // Act & Assert
            Assert.True(Enum.IsDefined(typeof(ReplicationDirection), direction));
        }

        [Theory]
        [InlineData(ConflictResolutionStrategy.PreferLeader)]
        [InlineData(ConflictResolutionStrategy.PreferFollower)]
        [InlineData(ConflictResolutionStrategy.LastWriteWins)]
        [InlineData(ConflictResolutionStrategy.Custom)]
        [InlineData(ConflictResolutionStrategy.FieldPriority)]
        [InlineData(ConflictResolutionStrategy.ManualReview)]
        public void ConflictResolutionStrategy_AllValues_ShouldBeValid(ConflictResolutionStrategy strategy)
        {
            // Act & Assert
            Assert.True(Enum.IsDefined(typeof(ConflictResolutionStrategy), strategy));
        }

        [Theory]
        [InlineData(ReplicationStatus.Running)]
        [InlineData(ReplicationStatus.Stopped)]
        [InlineData(ReplicationStatus.Error)]
        public void ReplicationStatus_AllValues_ShouldBeValid(ReplicationStatus status)
        {
            // Act & Assert
            Assert.True(Enum.IsDefined(typeof(ReplicationStatus), status));
        }

        [Fact]
        public void ReplicationOperation_ToString_ShouldReturnCorrectNames()
        {
            // Act & Assert
            Assert.Equal("Insert", ReplicationOperation.Insert.ToString());
            Assert.Equal("Update", ReplicationOperation.Update.ToString());
            Assert.Equal("Delete", ReplicationOperation.Delete.ToString());
        }

        [Fact]
        public void ReplicationDirection_ToString_ShouldReturnCorrectNames()
        {
            // Act & Assert
            Assert.Equal("LeaderToFollower", ReplicationDirection.LeaderToFollower.ToString());
            Assert.Equal("FollowerToLeader", ReplicationDirection.FollowerToLeader.ToString());
            Assert.Equal("Bidirectional", ReplicationDirection.Bidirectional.ToString());
        }

        [Fact]
        public void ReplicationStatus_ToString_ShouldReturnCorrectNames()
        {
            // Act & Assert
            Assert.Equal("Running", ReplicationStatus.Running.ToString());
            Assert.Equal("Stopped", ReplicationStatus.Stopped.ToString());
            Assert.Equal("Error", ReplicationStatus.Error.ToString());
        }
    }
}