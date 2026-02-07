using GUNRPG.Application.Operators;
using GUNRPG.Core.Operators;
using GUNRPG.Infrastructure.Persistence;
using LiteDB;
using Xunit;

namespace GUNRPG.Tests;

/// <summary>
/// Tests for OperatorExfilService ensuring proper exfil semantics and boundaries.
/// </summary>
public class OperatorExfilServiceTests : IDisposable
{
    private readonly LiteDatabase _database;
    private readonly IOperatorEventStore _eventStore;
    private readonly OperatorExfilService _service;
    private readonly string _dbPath;

    public OperatorExfilServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"test_exfil_service_{Guid.NewGuid()}.db");
        _database = new LiteDatabase(_dbPath);
        _eventStore = new LiteDbOperatorEventStore(_database);
        _service = new OperatorExfilService(_eventStore);
    }

    public void Dispose()
    {
        _database.Dispose();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    [Fact]
    public async Task CreateOperator_CreatesOperatorSuccessfully()
    {
        // Act
        var result = await _service.CreateOperatorAsync("Ghost");

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotEqual(Guid.Empty, result.Value);

        // Verify operator was created
        var loadResult = await _service.LoadOperatorAsync(result.Value!);
        Assert.True(loadResult.IsSuccess);
        Assert.Equal("Ghost", loadResult.Value!.Name);
        Assert.Equal(0, loadResult.Value.ExfilStreak);
    }

    [Fact]
    public async Task CreateOperator_WithEmptyName_ReturnsValidationError()
    {
        // Act
        var result = await _service.CreateOperatorAsync("");

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(Application.Results.ResultStatus.ValidationError, result.Status);
    }

    [Fact]
    public async Task LoadOperator_NonExistent_ReturnsNotFound()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();

        // Act
        var result = await _service.LoadOperatorAsync(nonExistentId);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(Application.Results.ResultStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task CommitSuccessfulExfil_IncrementsStreak()
    {
        // Arrange
        var createResult = await _service.CreateOperatorAsync("Ghost");
        var operatorId = createResult.Value!;
        var combatSessionId = Guid.NewGuid();

        // Act
        var exfilResult = await _service.CommitSuccessfulExfilAsync(
            operatorId,
            combatSessionId,
            experienceGained: 100);

        // Assert
        Assert.True(exfilResult.IsSuccess);
        var aggregate = exfilResult.Value!;
        Assert.Equal(1, aggregate.ExfilStreak);
        Assert.Equal(100, aggregate.TotalExperience);
        Assert.Equal(1, aggregate.SuccessfulExfils);
        Assert.Equal(0, aggregate.FailedExfils);
        Assert.Equal(0, aggregate.Deaths);
    }

    [Fact]
    public async Task MultipleSuccessfulExfils_BuildsStreak()
    {
        // Arrange
        var createResult = await _service.CreateOperatorAsync("Ghost");
        var operatorId = createResult.Value!;
        var combatSessionId1 = Guid.NewGuid();
        var combatSessionId2 = Guid.NewGuid();
        var combatSessionId3 = Guid.NewGuid();

        // Act
        await _service.CommitSuccessfulExfilAsync(operatorId, combatSessionId1, 100);
        await _service.CommitSuccessfulExfilAsync(operatorId, combatSessionId2, 150);
        var finalResult = await _service.CommitSuccessfulExfilAsync(operatorId, combatSessionId3, 200);

        // Assert
        Assert.True(finalResult.IsSuccess);
        var aggregate = finalResult.Value!;
        Assert.Equal(3, aggregate.ExfilStreak);
        Assert.Equal(450, aggregate.TotalExperience); // 100 + 150 + 200
        Assert.Equal(3, aggregate.SuccessfulExfils);
    }

    [Fact]
    public async Task CommitFailedExfil_ResetsStreak()
    {
        // Arrange
        var createResult = await _service.CreateOperatorAsync("Ghost");
        var operatorId = createResult.Value!;
        var combatSessionId1 = Guid.NewGuid();
        var combatSessionId2 = Guid.NewGuid();
        var combatSessionId3 = Guid.NewGuid();

        // Build up streak
        await _service.CommitSuccessfulExfilAsync(operatorId, combatSessionId1, 100);
        await _service.CommitSuccessfulExfilAsync(operatorId, combatSessionId2, 150);

        // Act - fail exfil
        var failResult = await _service.CommitFailedExfilAsync(
            operatorId,
            combatSessionId3,
            "Abandoned extraction zone");

        // Assert
        Assert.True(failResult.IsSuccess);
        var aggregate = failResult.Value!;
        Assert.Equal(0, aggregate.ExfilStreak); // Reset
        Assert.Equal(250, aggregate.TotalExperience); // XP retained
        Assert.Equal(2, aggregate.SuccessfulExfils);
        Assert.Equal(1, aggregate.FailedExfils);
    }

    [Fact]
    public async Task RecordOperatorDeath_ResetsStreak()
    {
        // Arrange
        var createResult = await _service.CreateOperatorAsync("Ghost");
        var operatorId = createResult.Value!;
        var combatSessionId1 = Guid.NewGuid();
        var combatSessionId2 = Guid.NewGuid();

        // Build up streak
        await _service.CommitSuccessfulExfilAsync(operatorId, combatSessionId1, 100);

        // Act - record death
        var deathResult = await _service.RecordOperatorDeathAsync(
            operatorId,
            combatSessionId2,
            "KIA - gunshot wound");

        // Assert
        Assert.True(deathResult.IsSuccess);
        var aggregate = deathResult.Value!;
        Assert.Equal(0, aggregate.ExfilStreak); // Reset
        Assert.Equal(100, aggregate.TotalExperience); // XP retained
        Assert.Equal(1, aggregate.SuccessfulExfils);
        Assert.Equal(0, aggregate.FailedExfils);
        Assert.Equal(1, aggregate.Deaths);
    }

    [Fact]
    public async Task StreakRecovery_AfterFailure()
    {
        // Arrange
        var createResult = await _service.CreateOperatorAsync("Ghost");
        var operatorId = createResult.Value!;

        // Act - build, fail, rebuild
        await _service.CommitSuccessfulExfilAsync(operatorId, Guid.NewGuid(), 100);
        await _service.CommitSuccessfulExfilAsync(operatorId, Guid.NewGuid(), 150);
        await _service.CommitFailedExfilAsync(operatorId, Guid.NewGuid(), "Failed");
        await _service.CommitSuccessfulExfilAsync(operatorId, Guid.NewGuid(), 120);
        var finalResult = await _service.CommitSuccessfulExfilAsync(operatorId, Guid.NewGuid(), 140);

        // Assert
        Assert.True(finalResult.IsSuccess);
        var aggregate = finalResult.Value!;
        Assert.Equal(2, aggregate.ExfilStreak); // Rebuilt streak
        Assert.Equal(510, aggregate.TotalExperience); // All XP from successful exfils
        Assert.Equal(4, aggregate.SuccessfulExfils);
        Assert.Equal(1, aggregate.FailedExfils);
    }

    [Fact]
    public async Task GetCombatSnapshot_ReturnsReadOnlyData()
    {
        // Arrange
        var createResult = await _service.CreateOperatorAsync("Ghost");
        var operatorId = createResult.Value!;
        await _service.CommitSuccessfulExfilAsync(operatorId, Guid.NewGuid(), 100);

        // Act
        var snapshotResult = await _service.GetCombatSnapshotAsync(operatorId);

        // Assert
        Assert.True(snapshotResult.IsSuccess);
        var snapshot = snapshotResult.Value!;
        Assert.Equal(operatorId, snapshot.OperatorId);
        Assert.Equal("Ghost", snapshot.Name);
        Assert.Equal(1, snapshot.ExfilStreak);
        Assert.Equal(100, snapshot.TotalExperience);
    }

    [Fact]
    public async Task GetCombatSnapshot_NonExistentOperator_ReturnsNotFound()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();

        // Act
        var result = await _service.GetCombatSnapshotAsync(nonExistentId);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(Application.Results.ResultStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task ExfilOperations_NonExistentOperator_ReturnsNotFound()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();
        var combatSessionId = Guid.NewGuid();

        // Act
        var successResult = await _service.CommitSuccessfulExfilAsync(nonExistentId, combatSessionId, 100);
        var failResult = await _service.CommitFailedExfilAsync(nonExistentId, combatSessionId, "Failed");
        var deathResult = await _service.RecordOperatorDeathAsync(nonExistentId, combatSessionId, "KIA");

        // Assert
        Assert.False(successResult.IsSuccess);
        Assert.False(failResult.IsSuccess);
        Assert.False(deathResult.IsSuccess);
        Assert.Equal(Application.Results.ResultStatus.NotFound, successResult.Status);
        Assert.Equal(Application.Results.ResultStatus.NotFound, failResult.Status);
        Assert.Equal(Application.Results.ResultStatus.NotFound, deathResult.Status);
    }

    [Fact]
    public async Task ComplexStreakPattern_TracksCorrectly()
    {
        // Arrange
        var createResult = await _service.CreateOperatorAsync("Ghost");
        var operatorId = createResult.Value!;

        // Act - simulate complex pattern
        await _service.CommitSuccessfulExfilAsync(operatorId, Guid.NewGuid(), 100); // Streak = 1
        await _service.CommitSuccessfulExfilAsync(operatorId, Guid.NewGuid(), 100); // Streak = 2
        await _service.CommitSuccessfulExfilAsync(operatorId, Guid.NewGuid(), 100); // Streak = 3
        await _service.RecordOperatorDeathAsync(operatorId, Guid.NewGuid(), "KIA"); // Streak = 0
        await _service.CommitSuccessfulExfilAsync(operatorId, Guid.NewGuid(), 100); // Streak = 1
        await _service.CommitFailedExfilAsync(operatorId, Guid.NewGuid(), "Failed"); // Streak = 0
        await _service.CommitSuccessfulExfilAsync(operatorId, Guid.NewGuid(), 100); // Streak = 1
        await _service.CommitSuccessfulExfilAsync(operatorId, Guid.NewGuid(), 100); // Streak = 2
        var finalResult = await _service.CommitSuccessfulExfilAsync(operatorId, Guid.NewGuid(), 100); // Streak = 3

        // Assert
        var aggregate = finalResult.Value!;
        Assert.Equal(3, aggregate.ExfilStreak);
        Assert.Equal(700, aggregate.TotalExperience); // 7 successful exfils * 100
        Assert.Equal(7, aggregate.SuccessfulExfils);
        Assert.Equal(1, aggregate.FailedExfils);
        Assert.Equal(1, aggregate.Deaths);
    }
}
