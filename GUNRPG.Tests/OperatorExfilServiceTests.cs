using GUNRPG.Application.Combat;
using GUNRPG.Application.Operators;
using GUNRPG.Application.Results;
using GUNRPG.Core.Operators;
using GUNRPG.Infrastructure.Persistence;
using LiteDB;
using Xunit;

namespace GUNRPG.Tests;

public class OperatorExfilServiceTests : IDisposable
{
    private readonly LiteDatabase _database;
    private readonly IOperatorEventStore _eventStore;
    private readonly OperatorExfilService _service;

    public OperatorExfilServiceTests()
    {
        _database = new LiteDatabase(":memory:");
        _eventStore = new LiteDbOperatorEventStore(_database);
        _service = new OperatorExfilService(_eventStore);
    }

    public void Dispose()
    {
        _database?.Dispose();
    }

    [Fact]
    public async Task CreateOperatorAsync_ShouldSucceed()
    {
        // Act
        var result = await _service.CreateOperatorAsync("TestOperator");

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotEqual(Guid.Empty, result.Value!.Value);
    }

    [Fact]
    public async Task CreateOperatorAsync_ShouldRejectEmptyName()
    {
        // Act
        var result = await _service.CreateOperatorAsync("");

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(ResultStatus.ValidationError, result.Status);
    }

    [Fact]
    public async Task LoadOperatorAsync_ShouldSucceedForExistingOperator()
    {
        // Arrange
        var createResult = await _service.CreateOperatorAsync("TestOperator");
        var operatorId = createResult.Value!;

        // Act
        var loadResult = await _service.LoadOperatorAsync(operatorId);

        // Assert
        Assert.True(loadResult.IsSuccess);
        Assert.Equal(operatorId, loadResult.Value!.Id);
        Assert.Equal("TestOperator", loadResult.Value.Name);
    }

    [Fact]
    public async Task LoadOperatorAsync_ShouldFailForNonexistentOperator()
    {
        // Arrange
        var nonexistentId = OperatorId.NewId();

        // Act
        var result = await _service.LoadOperatorAsync(nonexistentId);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(ResultStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task ApplyXpAsync_ShouldSucceed()
    {
        // Arrange
        var createResult = await _service.CreateOperatorAsync("TestOperator");
        var operatorId = createResult.Value!;

        // Act
        var result = await _service.ApplyXpAsync(operatorId, 150, "Victory");

        // Assert
        Assert.True(result.IsSuccess);

        // Verify by loading
        var loadResult = await _service.LoadOperatorAsync(operatorId);
        Assert.Equal(150, loadResult.Value!.TotalXp);
    }

    [Fact]
    public async Task ApplyXpAsync_ShouldRejectNegativeXp()
    {
        // Arrange
        var createResult = await _service.CreateOperatorAsync("TestOperator");
        var operatorId = createResult.Value!;

        // Act
        var result = await _service.ApplyXpAsync(operatorId, -10, "Invalid");

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(ResultStatus.ValidationError, result.Status);
    }

    [Fact]
    public async Task ApplyXpAsync_ShouldAccumulateXp()
    {
        // Arrange
        var createResult = await _service.CreateOperatorAsync("TestOperator");
        var operatorId = createResult.Value!;

        // Act
        await _service.ApplyXpAsync(operatorId, 100, "First");
        await _service.ApplyXpAsync(operatorId, 50, "Second");
        await _service.ApplyXpAsync(operatorId, 25, "Third");

        // Assert
        var loadResult = await _service.LoadOperatorAsync(operatorId);
        Assert.Equal(175, loadResult.Value!.TotalXp);
    }

    [Fact]
    public async Task TreatWoundsAsync_ShouldSucceed()
    {
        // Arrange
        var createResult = await _service.CreateOperatorAsync("TestOperator");
        var operatorId = createResult.Value!;

        // Act
        var result = await _service.TreatWoundsAsync(operatorId, 30f);

        // Assert
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task TreatWoundsAsync_ShouldRejectNegativeAmount()
    {
        // Arrange
        var createResult = await _service.CreateOperatorAsync("TestOperator");
        var operatorId = createResult.Value!;

        // Act
        var result = await _service.TreatWoundsAsync(operatorId, -10f);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(ResultStatus.ValidationError, result.Status);
    }

    [Fact]
    public async Task ChangeLoadoutAsync_ShouldSucceed()
    {
        // Arrange
        var createResult = await _service.CreateOperatorAsync("TestOperator");
        var operatorId = createResult.Value!;

        // Act
        var result = await _service.ChangeLoadoutAsync(operatorId, "AK-47");

        // Assert
        Assert.True(result.IsSuccess);

        // Verify by loading
        var loadResult = await _service.LoadOperatorAsync(operatorId);
        Assert.Equal("AK-47", loadResult.Value!.EquippedWeaponName);
    }

    [Fact]
    public async Task ChangeLoadoutAsync_ShouldRejectEmptyWeaponName()
    {
        // Arrange
        var createResult = await _service.CreateOperatorAsync("TestOperator");
        var operatorId = createResult.Value!;

        // Act
        var result = await _service.ChangeLoadoutAsync(operatorId, "");

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(ResultStatus.ValidationError, result.Status);
    }

    [Fact]
    public async Task UnlockPerkAsync_ShouldSucceed()
    {
        // Arrange
        var createResult = await _service.CreateOperatorAsync("TestOperator");
        var operatorId = createResult.Value!;

        // Act
        var result = await _service.UnlockPerkAsync(operatorId, "Fast Reload");

        // Assert
        Assert.True(result.IsSuccess);

        // Verify by loading
        var loadResult = await _service.LoadOperatorAsync(operatorId);
        Assert.Contains("Fast Reload", loadResult.Value!.UnlockedPerks);
    }

    [Fact]
    public async Task UnlockPerkAsync_ShouldRejectDuplicatePerk()
    {
        // Arrange
        var createResult = await _service.CreateOperatorAsync("TestOperator");
        var operatorId = createResult.Value!;
        await _service.UnlockPerkAsync(operatorId, "Fast Reload");

        // Act
        var result = await _service.UnlockPerkAsync(operatorId, "Fast Reload");

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(ResultStatus.ValidationError, result.Status);
    }

    [Fact]
    public async Task UnlockPerkAsync_ShouldAllowMultiplePerks()
    {
        // Arrange
        var createResult = await _service.CreateOperatorAsync("TestOperator");
        var operatorId = createResult.Value!;

        // Act
        await _service.UnlockPerkAsync(operatorId, "Fast Reload");
        await _service.UnlockPerkAsync(operatorId, "Double Tap");
        await _service.UnlockPerkAsync(operatorId, "Steady Aim");

        // Assert
        var loadResult = await _service.LoadOperatorAsync(operatorId);
        Assert.Equal(3, loadResult.Value!.UnlockedPerks.Count);
    }

    [Fact]
    public async Task ListOperatorsAsync_ShouldReturnAllOperators()
    {
        // Arrange
        await _service.CreateOperatorAsync("Operator1");
        await _service.CreateOperatorAsync("Operator2");
        await _service.CreateOperatorAsync("Operator3");

        // Act
        var result = await _service.ListOperatorsAsync();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value!.Count);
    }

    [Fact]
    public async Task OperatorExistsAsync_ShouldReturnTrueForExistingOperator()
    {
        // Arrange
        var createResult = await _service.CreateOperatorAsync("TestOperator");
        var operatorId = createResult.Value!;

        // Act
        var exists = await _service.OperatorExistsAsync(operatorId);

        // Assert
        Assert.True(exists);
    }

    [Fact]
    public async Task OperatorExistsAsync_ShouldReturnFalseForNonexistentOperator()
    {
        // Arrange
        var nonexistentId = OperatorId.NewId();

        // Act
        var exists = await _service.OperatorExistsAsync(nonexistentId);

        // Assert
        Assert.False(exists);
    }

    [Fact]
    public async Task ExfilService_ShouldMaintainEventOrder()
    {
        // Arrange
        var createResult = await _service.CreateOperatorAsync("TestOperator");
        var operatorId = createResult.Value!;

        // Act - Apply multiple operations
        await _service.ApplyXpAsync(operatorId, 100, "First");
        await _service.ChangeLoadoutAsync(operatorId, "AK-47");
        await _service.ApplyXpAsync(operatorId, 50, "Second");
        await _service.UnlockPerkAsync(operatorId, "Fast Reload");
        await _service.TreatWoundsAsync(operatorId, 20f);

        // Assert - Verify final state
        var loadResult = await _service.LoadOperatorAsync(operatorId);
        var aggregate = loadResult.Value!;

        Assert.Equal(150, aggregate.TotalXp);
        Assert.Equal("AK-47", aggregate.EquippedWeaponName);
        Assert.Single(aggregate.UnlockedPerks);
        Assert.Equal(6, aggregate.Events.Count); // 1 create + 5 operations
    }

    [Fact]
    public async Task RecordExfilSuccess_ShouldIncrementStreak()
    {
        // Arrange
        var createResult = await _service.CreateOperatorAsync("TestOperator");
        var operatorId = createResult.Value!;

        // Act
        await _service.RecordExfilSuccessAsync(operatorId);
        await _service.RecordExfilSuccessAsync(operatorId);
        await _service.RecordExfilSuccessAsync(operatorId);

        // Assert
        var loadResult = await _service.LoadOperatorAsync(operatorId);
        var aggregate = loadResult.Value!;
        Assert.Equal(3, aggregate.ExfilStreak);
        Assert.False(aggregate.IsDead);
    }

    [Fact]
    public async Task RecordExfilFailure_ShouldResetStreak()
    {
        // Arrange
        var createResult = await _service.CreateOperatorAsync("TestOperator");
        var operatorId = createResult.Value!;
        await _service.RecordExfilSuccessAsync(operatorId);
        await _service.RecordExfilSuccessAsync(operatorId);

        // Act
        await _service.RecordExfilFailureAsync(operatorId, "Ran out of time");

        // Assert
        var loadResult = await _service.LoadOperatorAsync(operatorId);
        var aggregate = loadResult.Value!;
        Assert.Equal(0, aggregate.ExfilStreak);
        Assert.False(aggregate.IsDead);
    }

    [Fact]
    public async Task RecordOperatorDeath_ShouldResetStreakAndMarkDead()
    {
        // Arrange
        var createResult = await _service.CreateOperatorAsync("TestOperator");
        var operatorId = createResult.Value!;
        await _service.RecordExfilSuccessAsync(operatorId);
        await _service.RecordExfilSuccessAsync(operatorId);

        // Act
        await _service.RecordOperatorDeathAsync(operatorId, "Killed in action");

        // Assert
        var loadResult = await _service.LoadOperatorAsync(operatorId);
        var aggregate = loadResult.Value!;
        Assert.Equal(0, aggregate.ExfilStreak);
        Assert.True(aggregate.IsDead);
        Assert.Equal(0, aggregate.CurrentHealth);
    }

    [Fact]
    public async Task RecordOperatorDeath_ShouldRejectDeadOperator()
    {
        // Arrange
        var createResult = await _service.CreateOperatorAsync("TestOperator");
        var operatorId = createResult.Value!;
        await _service.RecordOperatorDeathAsync(operatorId, "First death");

        // Act
        var result = await _service.RecordOperatorDeathAsync(operatorId, "Second death");

        // Assert
        Assert.Equal(ResultStatus.InvalidState, result.Status);
        Assert.Contains("already dead", result.ErrorMessage!);
    }

    [Fact]
    public async Task ExfilSuccess_AfterDeath_ShouldFail()
    {
        // Arrange
        var createResult = await _service.CreateOperatorAsync("TestOperator");
        var operatorId = createResult.Value!;
        await _service.RecordOperatorDeathAsync(operatorId, "Killed in action");

        // Act
        var result = await _service.RecordExfilSuccessAsync(operatorId);

        // Assert
        Assert.Equal(ResultStatus.InvalidState, result.Status);
        Assert.Contains("dead operator", result.ErrorMessage!);
    }

    [Fact]
    public async Task ExfilFailure_AfterDeath_ShouldFail()
    {
        // Arrange
        var createResult = await _service.CreateOperatorAsync("TestOperator");
        var operatorId = createResult.Value!;
        await _service.RecordOperatorDeathAsync(operatorId, "Killed in action");

        // Act
        var result = await _service.RecordExfilFailureAsync(operatorId, "Failed after death");

        // Assert
        Assert.Equal(ResultStatus.InvalidState, result.Status);
        Assert.Contains("dead operator", result.ErrorMessage!);
    }

    [Fact]
    public async Task RecordExfilSuccess_ShouldRequireExistingOperator()
    {
        // Arrange
        var nonexistentId = OperatorId.NewId();

        // Act
        var result = await _service.RecordExfilSuccessAsync(nonexistentId);

        // Assert
        Assert.Equal(ResultStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task ApplyXpAsync_AfterDeath_ShouldFail()
    {
        // Arrange
        var createResult = await _service.CreateOperatorAsync("TestOperator");
        var operatorId = createResult.Value!;
        await _service.RecordOperatorDeathAsync(operatorId, "Killed in action");

        // Act
        var result = await _service.ApplyXpAsync(operatorId, 100, "posthumous");

        // Assert
        Assert.Equal(ResultStatus.InvalidState, result.Status);
        Assert.Contains("dead operator", result.ErrorMessage!);
    }

    [Fact]
    public async Task TreatWoundsAsync_AfterDeath_ShouldFail()
    {
        // Arrange
        var createResult = await _service.CreateOperatorAsync("TestOperator");
        var operatorId = createResult.Value!;
        await _service.RecordOperatorDeathAsync(operatorId, "Killed in action");

        // Act
        var result = await _service.TreatWoundsAsync(operatorId, 50f);

        // Assert
        Assert.Equal(ResultStatus.InvalidState, result.Status);
        Assert.Contains("dead operator", result.ErrorMessage!);
    }

    [Fact]
    public async Task ChangeLoadoutAsync_AfterDeath_ShouldFail()
    {
        // Arrange
        var createResult = await _service.CreateOperatorAsync("TestOperator");
        var operatorId = createResult.Value!;
        await _service.RecordOperatorDeathAsync(operatorId, "Killed in action");

        // Act
        var result = await _service.ChangeLoadoutAsync(operatorId, "AK-47");

        // Assert
        Assert.Equal(ResultStatus.InvalidState, result.Status);
        Assert.Contains("dead operator", result.ErrorMessage!);
    }

    [Fact]
    public async Task UnlockPerkAsync_AfterDeath_ShouldFail()
    {
        // Arrange
        var createResult = await _service.CreateOperatorAsync("TestOperator");
        var operatorId = createResult.Value!;
        await _service.RecordOperatorDeathAsync(operatorId, "Killed in action");

        // Act
        var result = await _service.UnlockPerkAsync(operatorId, "Fast Reload");

        // Assert
        Assert.Equal(ResultStatus.InvalidState, result.Status);
        Assert.Contains("dead operator", result.ErrorMessage!);
    }

    [Fact]
    public async Task ProcessCombatOutcomeAsync_AfterDeath_ShouldFail()
    {
        // Arrange
        var createResult = await _service.CreateOperatorAsync("TestOperator");
        var operatorId = createResult.Value!;
        await _service.RecordOperatorDeathAsync(operatorId, "Killed in action");

        var outcome = new CombatOutcome(
            Guid.NewGuid(),
            operatorId,
            survived: true,
            damageTaken: 10f,
            remainingHealth: 90f,
            xpEarned: 100,
            xpReason: "Victory",
            enemiesEliminated: 1,
            isVictory: true,
            completedAt: DateTimeOffset.UtcNow);

        // Act
        var result = await _service.ProcessCombatOutcomeAsync(outcome, playerConfirmed: true);

        // Assert
        Assert.Equal(ResultStatus.InvalidState, result.Status);
        Assert.Contains("dead operator", result.ErrorMessage!);
    }
}
