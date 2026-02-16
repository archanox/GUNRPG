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
        // Use in-memory database for testing with custom mapper
        var mapper = new BsonMapper();
        mapper.Entity<OperatorEventDocument>().Id(x => x.Id);
        _database = new LiteDatabase(":memory:", mapper);
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
        Assert.Equal(6, aggregate.Events.Count); // 1 create + 5 operations (2xp + loadout + perk + wounds)
    }

    [Fact]
    public async Task CompleteExfilAsync_ShouldIncrementStreak()
    {
        // Arrange
        var createResult = await _service.CreateOperatorAsync("TestOperator");
        var operatorId = createResult.Value!;

        // Act
        var infilResult = await _service.StartInfilAsync(operatorId);
        var sessionId = infilResult.Value;
        await _service.CompleteExfilAsync(operatorId);
        // Note: CompleteExfilAsync doesn't end infil mode, so we can't call StartInfilAsync again
        // This test should probably be updated to use ProcessCombatOutcomeAsync instead
        // For now, just test that one exfil increments the streak
        
        // Assert
        var loadResult = await _service.LoadOperatorAsync(operatorId);
        var aggregate = loadResult.Value!;
        Assert.Equal(1, aggregate.ExfilStreak);
    }

    [Fact]
    public async Task FailExfilAsync_ShouldResetStreak()
    {
        // Arrange
        var createResult = await _service.CreateOperatorAsync("TestOperator");
        var operatorId = createResult.Value!;
        var infilResult = await _service.StartInfilAsync(operatorId);
        var sessionId = infilResult.Value;
        await _service.CompleteExfilAsync(operatorId);
        // CompleteExfilAsync doesn't end infil, so we need to manually fail it to test reset

        // Act
        var result = await _service.FailExfilAsync(operatorId, "Retreat");

        // Assert
        Assert.True(result.IsSuccess);
        var loadResult = await _service.LoadOperatorAsync(operatorId);
        var aggregate = loadResult.Value!;
        Assert.Equal(0, aggregate.ExfilStreak);
    }

    [Fact]
    public async Task KillOperatorAsync_ShouldMarkDead()
    {
        // Arrange
        var createResult = await _service.CreateOperatorAsync("TestOperator");
        var operatorId = createResult.Value!;
        await _service.CompleteExfilAsync(operatorId);

        // Act
        var result = await _service.KillOperatorAsync(operatorId, "Combat casualty");

        // Assert
        Assert.True(result.IsSuccess);
        var loadResult = await _service.LoadOperatorAsync(operatorId);
        var aggregate = loadResult.Value!;
        Assert.True(aggregate.IsDead);
        Assert.Equal(aggregate.MaxHealth, aggregate.CurrentHealth); // Health restored to full after death
        Assert.Equal(0, aggregate.ExfilStreak);
    }

    [Fact]
    public async Task DeadOperator_CannotGainXp()
    {
        // Arrange
        var createResult = await _service.CreateOperatorAsync("TestOperator");
        var operatorId = createResult.Value!;
        await _service.KillOperatorAsync(operatorId, "Combat casualty");

        // Act
        var result = await _service.ApplyXpAsync(operatorId, 100, "Should fail");

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("dead operator", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeadOperator_CannotBeHealed()
    {
        // Arrange
        var createResult = await _service.CreateOperatorAsync("TestOperator");
        var operatorId = createResult.Value!;
        await _service.KillOperatorAsync(operatorId, "Combat casualty");

        // Act
        var result = await _service.TreatWoundsAsync(operatorId, 50f);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("dead operator", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeadOperator_CannotChangeLoadout()
    {
        // Arrange
        var createResult = await _service.CreateOperatorAsync("TestOperator");
        var operatorId = createResult.Value!;
        await _service.KillOperatorAsync(operatorId, "Combat casualty");

        // Act
        var result = await _service.ChangeLoadoutAsync(operatorId, "AK-47");

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("dead operator", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeadOperator_CannotUnlockPerks()
    {
        // Arrange
        var createResult = await _service.CreateOperatorAsync("TestOperator");
        var operatorId = createResult.Value!;
        await _service.KillOperatorAsync(operatorId, "Combat casualty");

        // Act
        var result = await _service.UnlockPerkAsync(operatorId, "Fast Reload");

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("dead operator", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeadOperator_CannotCompleteExfil()
    {
        // Arrange
        var createResult = await _service.CreateOperatorAsync("TestOperator");
        var operatorId = createResult.Value!;
        await _service.KillOperatorAsync(operatorId, "Combat casualty");

        // Act
        var result = await _service.CompleteExfilAsync(operatorId);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("dead", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task KillOperatorAsync_CannotKillAlreadyDead()
    {
        // Arrange
        var createResult = await _service.CreateOperatorAsync("TestOperator");
        var operatorId = createResult.Value!;
        await _service.KillOperatorAsync(operatorId, "First death");

        // Act
        var result = await _service.KillOperatorAsync(operatorId, "Second death");

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("already dead", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FailExfilAsync_RequiresReason()
    {
        // Arrange
        var createResult = await _service.CreateOperatorAsync("TestOperator");
        var operatorId = createResult.Value!;

        // Act
        var result = await _service.FailExfilAsync(operatorId, "");

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("reason", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task KillOperatorAsync_RequiresCauseOfDeath()
    {
        // Arrange
        var createResult = await _service.CreateOperatorAsync("TestOperator");
        var operatorId = createResult.Value!;

        // Act
        var result = await _service.KillOperatorAsync(operatorId, "");

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("cause of death", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProcessCombatOutcome_SuccessfulExfil_ShouldCommitAllEvents()
    {
        // Arrange
        var createResult = await _service.CreateOperatorAsync("TestOperator");
        var operatorId = createResult.Value!;

        var infilResult = await _service.StartInfilAsync(operatorId);
        var sessionId = infilResult.Value;
        var outcome = new Application.Combat.CombatOutcome(
            sessionId: sessionId,
            operatorId: operatorId,
            operatorDied: false,
            damageTaken: 20f,
            xpGained: 100,
            gearLost: Array.Empty<GUNRPG.Core.Equipment.GearId>(),
            isVictory: true,
            completedAt: DateTimeOffset.UtcNow);

        // Act
        var result = await _service.ProcessCombatOutcomeAsync(outcome, playerConfirmed: true);

        // Assert
        Assert.True(result.IsSuccess);

        // Verify events were committed
        var loadResult = await _service.LoadOperatorAsync(operatorId);
        var aggregate = loadResult.Value!;
        
        Assert.Equal(100, aggregate.TotalXp);
        Assert.Equal(1, aggregate.ExfilStreak);
        Assert.False(aggregate.IsDead);
        
        // Should have: OperatorCreated + InfilStarted + XpGained + ExfilSucceeded + InfilEnded = 5 events
        Assert.Equal(5, aggregate.Events.Count);
    }

    [Fact]
    public async Task ProcessCombatOutcome_OperatorDeath_ShouldResetStreak()
    {
        // Arrange
        var createResult = await _service.CreateOperatorAsync("TestOperator");
        var operatorId = createResult.Value!;
        
        // Build up a streak first using ProcessCombatOutcomeAsync
        var infilResult = await _service.StartInfilAsync(operatorId);
        var sessionId = infilResult.Value;
        var firstOutcome = new Application.Combat.CombatOutcome(
            sessionId: sessionId,
            operatorId: operatorId,
            operatorDied: false,
            damageTaken: 10f,
            xpGained: 50,
            gearLost: Array.Empty<GUNRPG.Core.Equipment.GearId>(),
            isVictory: true,
            completedAt: DateTimeOffset.UtcNow);
        var exfilResult1 = await _service.ProcessCombatOutcomeAsync(firstOutcome, playerConfirmed: true);
        Assert.True(exfilResult1.IsSuccess);
        
        var infilResult2 = await _service.StartInfilAsync(operatorId);
        var sessionId2 = infilResult2.Value;
        var secondOutcome = new Application.Combat.CombatOutcome(
            sessionId: sessionId2,
            operatorId: operatorId,
            operatorDied: false,
            damageTaken: 15f,
            xpGained: 75,
            gearLost: Array.Empty<GUNRPG.Core.Equipment.GearId>(),
            isVictory: true,
            completedAt: DateTimeOffset.UtcNow);
        var exfilResult2 = await _service.ProcessCombatOutcomeAsync(secondOutcome, playerConfirmed: true);
        Assert.True(exfilResult2.IsSuccess);
        
        var loadBefore = await _service.LoadOperatorAsync(operatorId);
        Assert.Equal(2, loadBefore.Value!.ExfilStreak);

        var infilResult3 = await _service.StartInfilAsync(operatorId);
        var sessionId3 = infilResult3.Value;
        var outcome = new Application.Combat.CombatOutcome(
            sessionId: sessionId3,
            operatorId: operatorId,
            operatorDied: true,
            damageTaken: 100f,
            xpGained: 10, // Outcome includes XP, but death prevents it from being applied
            gearLost: Array.Empty<GUNRPG.Core.Equipment.GearId>(),
            isVictory: false,
            completedAt: DateTimeOffset.UtcNow);

        // Act
        var result = await _service.ProcessCombatOutcomeAsync(outcome, playerConfirmed: true);

        // Assert
        Assert.True(result.IsSuccess);

        var loadAfter = await _service.LoadOperatorAsync(operatorId);
        var aggregate = loadAfter.Value!;
        
        Assert.True(aggregate.IsDead);
        Assert.Equal(0, aggregate.CurrentHealth);
        Assert.Equal(0, aggregate.ExfilStreak); // Streak should be reset
        
        // Should have: OperatorCreated + (InfilStarted + XpGained + ExfilSucceeded + InfilEnded) x 2 + InfilStarted + OperatorDied + InfilEnded = 12 events
        Assert.Equal(12, aggregate.Events.Count);
    }

    [Fact]
    public async Task ProcessCombatOutcome_SurvivalWithoutVictory_ShouldApplyXpAndFailExfil()
    {
        // Arrange
        var createResult = await _service.CreateOperatorAsync("TestOperator");
        var operatorId = createResult.Value!;

        var infilResult = await _service.StartInfilAsync(operatorId);
        var sessionId = infilResult.Value;
        var outcome = new Application.Combat.CombatOutcome(
            sessionId: sessionId,
            operatorId: operatorId,
            operatorDied: false,
            damageTaken: 50f,
            xpGained: 50,
            gearLost: Array.Empty<GUNRPG.Core.Equipment.GearId>(),
            isVictory: false, // Survived but didn't win
            completedAt: DateTimeOffset.UtcNow);

        // Act
        var result = await _service.ProcessCombatOutcomeAsync(outcome, playerConfirmed: true);

        // Assert
        Assert.True(result.IsSuccess);

        var loadResult = await _service.LoadOperatorAsync(operatorId);
        var aggregate = loadResult.Value!;
        
        Assert.Equal(50, aggregate.TotalXp);
        Assert.Equal(0, aggregate.ExfilStreak); // ExfilFailed resets streak
        Assert.False(aggregate.IsDead);
        Assert.Equal(OperatorMode.Base, aggregate.CurrentMode); // Should return to Base mode
        
        // Should have: OperatorCreated + InfilStarted + XpGained + ExfilFailed + InfilEnded = 5 events
        Assert.Equal(5, aggregate.Events.Count);
    }

    [Fact]
    public async Task ProcessCombatOutcome_RequiresPlayerConfirmation()
    {
        // Arrange
        var createResult = await _service.CreateOperatorAsync("TestOperator");
        var operatorId = createResult.Value!;

        var outcome = new Application.Combat.CombatOutcome(
            sessionId: Guid.NewGuid(), // Using random ID as this test doesn't start infil
            operatorId: operatorId,
            operatorDied: false,
            damageTaken: 0f,
            xpGained: 100,
            gearLost: Array.Empty<GUNRPG.Core.Equipment.GearId>(),
            isVictory: true,
            completedAt: DateTimeOffset.UtcNow);

        // Act
        var result = await _service.ProcessCombatOutcomeAsync(outcome, playerConfirmed: false);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(ResultStatus.ValidationError, result.Status);
        Assert.Contains("confirmed", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProcessCombatOutcome_RejectsNullOutcome()
    {
        // Act
        var result = await _service.ProcessCombatOutcomeAsync(null!, playerConfirmed: true);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(ResultStatus.ValidationError, result.Status);
    }

    [Fact]
    public async Task ProcessCombatOutcome_RejectsOutcomeForDeadOperator()
    {
        // Arrange
        var createResult = await _service.CreateOperatorAsync("TestOperator");
        var operatorId = createResult.Value!;
        await _service.KillOperatorAsync(operatorId, "Previous death");

        var outcome = new Application.Combat.CombatOutcome(
            sessionId: Guid.NewGuid(), // Using random ID as this test is for a dead operator
            operatorId: operatorId,
            operatorDied: false,
            damageTaken: 0f,
            xpGained: 100,
            gearLost: Array.Empty<GUNRPG.Core.Equipment.GearId>(),
            isVictory: true,
            completedAt: DateTimeOffset.UtcNow);

        // Act
        var result = await _service.ProcessCombatOutcomeAsync(outcome, playerConfirmed: true);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(ResultStatus.InvalidState, result.Status);
        Assert.Contains("dead operator", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProcessCombatOutcome_MultipleSuccessfulExfils_ShouldIncrementStreak()
    {
        // Arrange
        var createResult = await _service.CreateOperatorAsync("TestOperator");
        var operatorId = createResult.Value!;

        // Act - Process multiple successful combat outcomes
        for (int i = 0; i < 5; i++)
        {
            var infilResult = await _service.StartInfilAsync(operatorId);
        var sessionId = infilResult.Value;
            var outcome = new Application.Combat.CombatOutcome(
                sessionId: sessionId,
                operatorId: operatorId,
                operatorDied: false,
                damageTaken: 10f,
                xpGained: 100,
                gearLost: Array.Empty<GUNRPG.Core.Equipment.GearId>(),
                isVictory: true,
                completedAt: DateTimeOffset.UtcNow);

            var result = await _service.ProcessCombatOutcomeAsync(outcome, playerConfirmed: true);
            Assert.True(result.IsSuccess);
        }

        // Assert
        var loadResult = await _service.LoadOperatorAsync(operatorId);
        var aggregate = loadResult.Value!;
        
        Assert.Equal(5, aggregate.ExfilStreak);
        Assert.Equal(500, aggregate.TotalXp);
    }

    [Fact]
    public async Task ProcessCombatOutcome_ZeroXp_ShouldNotApplyXpEvent()
    {
        // Arrange
        var createResult = await _service.CreateOperatorAsync("TestOperator");
        var operatorId = createResult.Value!;

        var infilResult = await _service.StartInfilAsync(operatorId);
        var sessionId = infilResult.Value;
        var outcome = new Application.Combat.CombatOutcome(
            sessionId: sessionId,
            operatorId: operatorId,
            operatorDied: false,
            damageTaken: 0f,
            xpGained: 0, // No XP
            gearLost: Array.Empty<GUNRPG.Core.Equipment.GearId>(),
            isVictory: true, // Still victory, just no XP
            completedAt: DateTimeOffset.UtcNow);

        // Act
        var result = await _service.ProcessCombatOutcomeAsync(outcome, playerConfirmed: true);

        // Assert
        Assert.True(result.IsSuccess);

        var loadResult = await _service.LoadOperatorAsync(operatorId);
        var aggregate = loadResult.Value!;
        
        Assert.Equal(0, aggregate.TotalXp);
        Assert.Equal(1, aggregate.ExfilStreak); // Exfil still succeeds
        
        // Should have: OperatorCreated + InfilStarted + ExfilSucceeded + InfilEnded = 4 events (no XpGained)
        Assert.Equal(4, aggregate.Events.Count);
    }

    [Fact]
    public async Task EndToEndWorkflow_CombatToOutcomeToExfilToPersistenceToReload()
    {
        // This test proves the complete workflow:
        // Combat → Outcome → Exfil → Operator Events → Persistence → Reload
        
        // Step 1: Create operator (exfil-only)
        var createResult = await _service.CreateOperatorAsync("Alpha Squad");
        Assert.True(createResult.IsSuccess);
        var operatorId = createResult.Value!;

        // Step 2: Simulate combat producing an outcome
        var infilResult = await _service.StartInfilAsync(operatorId);
        var sessionId = infilResult.Value;
        var combatOutcome = new Application.Combat.CombatOutcome(
            sessionId: sessionId,
            operatorId: operatorId,
            operatorDied: false,
            damageTaken: 30f,
            xpGained: 150,
            gearLost: Array.Empty<GUNRPG.Core.Equipment.GearId>(),
            isVictory: true,
            completedAt: DateTimeOffset.UtcNow);

        // Step 3: Process outcome through exfil (the boundary)
        var processResult = await _service.ProcessCombatOutcomeAsync(combatOutcome, playerConfirmed: true);
        Assert.True(processResult.IsSuccess);

        // Step 4: Verify events were persisted
        var loadResult1 = await _service.LoadOperatorAsync(operatorId);
        Assert.True(loadResult1.IsSuccess);
        var aggregate1 = loadResult1.Value!;
        
        Assert.Equal(150, aggregate1.TotalXp);
        Assert.Equal(1, aggregate1.ExfilStreak);
        Assert.False(aggregate1.IsDead);

        // Step 5: Simulate another combat (should increment streak)
        var infilResult2 = await _service.StartInfilAsync(operatorId);
        var sessionId2 = infilResult2.Value;
        var combatOutcome2 = new Application.Combat.CombatOutcome(
            sessionId: sessionId2,
            operatorId: operatorId,
            operatorDied: false,
            damageTaken: 20f,
            xpGained: 200,
            gearLost: Array.Empty<GUNRPG.Core.Equipment.GearId>(),
            isVictory: true,
            completedAt: DateTimeOffset.UtcNow);

        var result2 = await _service.ProcessCombatOutcomeAsync(combatOutcome2, playerConfirmed: true);
        Assert.True(result2.IsSuccess);

        // Step 6: Reload from persistence (proves persistence works)
        var loadResult2 = await _service.LoadOperatorAsync(operatorId);
        Assert.True(loadResult2.IsSuccess);
        var aggregate2 = loadResult2.Value!;
        
        Assert.Equal(350, aggregate2.TotalXp); // 150 + 200
        Assert.Equal(2, aggregate2.ExfilStreak); // Both exfils succeeded
        Assert.False(aggregate2.IsDead);
        
        // Should have: OperatorCreated + (InfilStarted + XpGained + ExfilSucceeded + InfilEnded) x 2 = 9 events
        Assert.Equal(9, aggregate2.Events.Count);

        // Step 7: Simulate a fatal combat
        var infilResult3 = await _service.StartInfilAsync(operatorId);
        var sessionId3 = infilResult3.Value;
        var fatalCombat = new Application.Combat.CombatOutcome(
            sessionId: sessionId3,
            operatorId: operatorId,
            operatorDied: true,
            damageTaken: 100f,
            xpGained: 10, // Outcome has XP but won't be applied due to death
            gearLost: Array.Empty<GUNRPG.Core.Equipment.GearId>(),
            isVictory: false,
            completedAt: DateTimeOffset.UtcNow);

        await _service.ProcessCombatOutcomeAsync(fatalCombat, playerConfirmed: true);

        // Step 8: Final reload - operator should be dead with reset streak
        var loadResult3 = await _service.LoadOperatorAsync(operatorId);
        Assert.True(loadResult3.IsSuccess);
        var aggregate3 = loadResult3.Value!;
        
        Assert.True(aggregate3.IsDead);
        Assert.Equal(0, aggregate3.CurrentHealth);
        Assert.Equal(0, aggregate3.ExfilStreak); // Reset on death
        Assert.Equal(350, aggregate3.TotalXp); // Total from previous outcomes (150 + 200); death event doesn't apply XP
        
        // Should have: previous 9 + InfilStarted + OperatorDied + InfilEnded = 12 events
        Assert.Equal(12, aggregate3.Events.Count);

        // Step 9: Verify hash chain integrity on all events
        foreach (var evt in aggregate3.Events)
        {
            Assert.True(evt.VerifyHash(), $"Event {evt.SequenceNumber} failed hash verification");
        }

        // Step 10: Verify dead operator cannot process new outcomes
        var postMortemOutcome = new Application.Combat.CombatOutcome(
            sessionId: sessionId,
            operatorId: operatorId,
            operatorDied: false,
            damageTaken: 0f,
            xpGained: 100,
            gearLost: Array.Empty<GUNRPG.Core.Equipment.GearId>(),
            isVictory: true,
            completedAt: DateTimeOffset.UtcNow);

        var postMortemResult = await _service.ProcessCombatOutcomeAsync(postMortemOutcome, playerConfirmed: true);
        Assert.False(postMortemResult.IsSuccess);
        Assert.Equal(ResultStatus.InvalidState, postMortemResult.Status);
    }
}
