using GUNRPG.Application.Operators;
using GUNRPG.Core.Operators;
using GUNRPG.Infrastructure.Persistence;
using LiteDB;
using Xunit;

namespace GUNRPG.Tests;

/// <summary>
/// Tests for Operator Mode enforcement and Infil/Exfil workflow.
/// Validates the boundary between Base mode (operator management) and Infil mode (combat).
/// </summary>
public class OperatorModeTests : IDisposable
{
    private readonly LiteDatabase _database;
    private readonly IOperatorEventStore _eventStore;
    private readonly OperatorExfilService _service;

    public OperatorModeTests()
    {
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
    public async Task NewOperator_StartsInBaseMode()
    {
        // Arrange
        var createResult = await _service.CreateOperatorAsync("TestOp");
        var operatorId = createResult.Value!;

        // Act
        var loadResult = await _service.LoadOperatorAsync(operatorId);

        // Assert
        Assert.True(loadResult.IsSuccess);
        var aggregate = loadResult.Value!;
        Assert.Equal(OperatorMode.Base, aggregate.CurrentMode);
        Assert.Null(aggregate.InfilStartTime);
        Assert.Null(aggregate.ActiveCombatSessionId);
    }

    [Fact]
    public async Task StartInfil_TransitionsToInfilMode()
    {
        // Arrange
        var createResult = await _service.CreateOperatorAsync("TestOp");
        var operatorId = createResult.Value!;

        // Act
        var infilResult = await _service.StartInfilAsync(operatorId);

        // Assert
        Assert.True(infilResult.IsSuccess);
        var sessionId = infilResult.Value;
        Assert.NotEqual(Guid.Empty, sessionId);

        var loadResult = await _service.LoadOperatorAsync(operatorId);
        var aggregate = loadResult.Value!;
        Assert.Equal(OperatorMode.Infil, aggregate.CurrentMode);
        Assert.NotNull(aggregate.InfilStartTime);
        Assert.Equal(sessionId, aggregate.ActiveCombatSessionId);
    }

    [Fact]
    public async Task StartInfil_WhenAlreadyInInfil_Fails()
    {
        // Arrange
        var createResult = await _service.CreateOperatorAsync("TestOp");
        var operatorId = createResult.Value!;
        await _service.StartInfilAsync(operatorId);

        // Act
        var secondInfilResult = await _service.StartInfilAsync(operatorId);

        // Assert
        Assert.False(secondInfilResult.IsSuccess);
        Assert.Contains("already in Infil mode", secondInfilResult.ErrorMessage);
    }

    [Fact]
    public async Task ChangeLoadout_InBaseMode_Succeeds()
    {
        // Arrange
        var createResult = await _service.CreateOperatorAsync("TestOp");
        var operatorId = createResult.Value!;

        // Act
        var result = await _service.ChangeLoadoutAsync(operatorId, "SOKOL 545");

        // Assert
        Assert.True(result.IsSuccess);

        var loadResult = await _service.LoadOperatorAsync(operatorId);
        Assert.Equal("SOKOL 545", loadResult.Value!.EquippedWeaponName);
    }

    [Fact]
    public async Task ChangeLoadout_InInfilMode_Fails()
    {
        // Arrange
        var createResult = await _service.CreateOperatorAsync("TestOp");
        var operatorId = createResult.Value!;
        await _service.StartInfilAsync(operatorId);

        // Act
        var result = await _service.ChangeLoadoutAsync(operatorId, "SOKOL 545");

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("loadout is locked", result.ErrorMessage);
    }

    [Fact]
    public async Task TreatWounds_InBaseMode_Succeeds()
    {
        // Arrange
        var createResult = await _service.CreateOperatorAsync("TestOp");
        var operatorId = createResult.Value!;

        // Act
        var result = await _service.TreatWoundsAsync(operatorId, 10f);

        // Assert
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task TreatWounds_InInfilMode_Fails()
    {
        // Arrange
        var createResult = await _service.CreateOperatorAsync("TestOp");
        var operatorId = createResult.Value!;
        await _service.StartInfilAsync(operatorId);

        // Act
        var result = await _service.TreatWoundsAsync(operatorId, 10f);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("Cannot treat wounds while in Infil mode", result.ErrorMessage);
    }

    [Fact]
    public async Task UnlockPerk_InBaseMode_Succeeds()
    {
        // Arrange
        var createResult = await _service.CreateOperatorAsync("TestOp");
        var operatorId = createResult.Value!;

        // Act
        var result = await _service.UnlockPerkAsync(operatorId, "TestPerk");

        // Assert
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task UnlockPerk_InInfilMode_Fails()
    {
        // Arrange
        var createResult = await _service.CreateOperatorAsync("TestOp");
        var operatorId = createResult.Value!;
        await _service.StartInfilAsync(operatorId);

        // Act
        var result = await _service.UnlockPerkAsync(operatorId, "TestPerk");

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("Cannot unlock perk while in Infil mode", result.ErrorMessage);
    }

    [Fact]
    public async Task ApplyXp_InBaseMode_Succeeds()
    {
        // Arrange
        var createResult = await _service.CreateOperatorAsync("TestOp");
        var operatorId = createResult.Value!;

        // Act
        var result = await _service.ApplyXpAsync(operatorId, 100, "Test");

        // Assert
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task ApplyXp_InInfilMode_Fails()
    {
        // Arrange
        var createResult = await _service.CreateOperatorAsync("TestOp");
        var operatorId = createResult.Value!;
        await _service.StartInfilAsync(operatorId);

        // Act
        var result = await _service.ApplyXpAsync(operatorId, 100, "Test");

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("Cannot apply XP while in Infil mode", result.ErrorMessage);
    }

    [Fact]
    public async Task FailInfil_ResetsStreakAndReturnsToBase()
    {
        // Arrange
        var createResult = await _service.CreateOperatorAsync("TestOp");
        var operatorId = createResult.Value!;

        // Start infil first
        await _service.StartInfilAsync(operatorId);

        // Act
        var failResult = await _service.FailInfilAsync(operatorId, "Timeout");

        // Assert
        Assert.True(failResult.IsSuccess);

        var loadResult = await _service.LoadOperatorAsync(operatorId);
        var aggregate = loadResult.Value!;
        Assert.Equal(OperatorMode.Base, aggregate.CurrentMode);
        Assert.Equal(0, aggregate.ExfilStreak);
        Assert.Null(aggregate.InfilStartTime);
        Assert.Null(aggregate.ActiveCombatSessionId);
    }

    [Fact]
    public async Task FailInfil_WhenInBaseMode_Fails()
    {
        // Arrange
        var createResult = await _service.CreateOperatorAsync("TestOp");
        var operatorId = createResult.Value!;

        // Act
        var result = await _service.FailInfilAsync(operatorId, "Timeout");

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("not in Infil mode", result.ErrorMessage);
    }

    [Fact]
    public async Task StartInfil_LocksLoadout()
    {
        // Arrange
        var createResult = await _service.CreateOperatorAsync("TestOp");
        var operatorId = createResult.Value!;
        await _service.ChangeLoadoutAsync(operatorId, "SOKOL 545");

        // Act
        await _service.StartInfilAsync(operatorId);

        // Assert
        var loadResult = await _service.LoadOperatorAsync(operatorId);
        var aggregate = loadResult.Value!;
        Assert.Equal("SOKOL 545", aggregate.LockedLoadout);
    }

    [Fact]
    public async Task InfilSuccess_PreservesLoadout()
    {
        // Arrange
        var createResult = await _service.CreateOperatorAsync("TestOp");
        var operatorId = createResult.Value!;
        await _service.ChangeLoadoutAsync(operatorId, "SOKOL 545");
        var infilResult = await _service.StartInfilAsync(operatorId);
        Assert.True(infilResult.IsSuccess);

        // Act - Complete exfil which should preserve loadout
        var exfilResult = await _service.CompleteExfilAsync(operatorId);

        // Assert
        Assert.True(exfilResult.IsSuccess);
        
        // After CompleteExfilAsync, operator is still in Infil mode (legacy behavior)
        // ProcessCombatOutcomeAsync handles the full workflow including mode transition
        var loadResult = await _service.LoadOperatorAsync(operatorId);
        var aggregate = loadResult.Value!;
        Assert.Equal("SOKOL 545", aggregate.EquippedWeaponName); // Loadout preserved
        Assert.Equal(OperatorMode.Infil, aggregate.CurrentMode); // Still in Infil (not ended yet)
        Assert.Equal(1, aggregate.ExfilStreak); // Streak incremented
    }

    [Fact]
    public async Task InfilFailure_ClearsLoadout()
    {
        // Arrange
        var createResult = await _service.CreateOperatorAsync("TestOp");
        var operatorId = createResult.Value!;
        await _service.ChangeLoadoutAsync(operatorId, "SOKOL 545");
        await _service.StartInfilAsync(operatorId);

        // Act
        await _service.FailInfilAsync(operatorId, "Timeout");

        // Assert
        var loadResult = await _service.LoadOperatorAsync(operatorId);
        var aggregate = loadResult.Value!;
        Assert.Equal(string.Empty, aggregate.EquippedWeaponName); // Loadout cleared on failure
        Assert.Equal(OperatorMode.Base, aggregate.CurrentMode);
    }

    [Fact]
    public async Task ProcessCombatOutcome_WhenNotInInfil_Fails()
    {
        // Arrange
        var mapper = new BsonMapper();
        mapper.Entity<OperatorEventDocument>().Id(x => x.Id);
        using var db = new LiteDatabase(":memory:", mapper);
        var eventStore = new LiteDbOperatorEventStore(db);
        var service = new OperatorExfilService(eventStore);
        
        var createResult = await service.CreateOperatorAsync("TestOp");
        var operatorId = createResult.Value!;

        var outcome = new GUNRPG.Application.Combat.CombatOutcome(
            Guid.NewGuid(),
            operatorId,
            operatorDied: false,
            xpGained: 100,
            gearLost: Array.Empty<GUNRPG.Core.Equipment.GearId>(),
            isVictory: true);

        // Act
        var result = await service.ProcessCombatOutcomeAsync(outcome, playerConfirmed: true);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("not in Infil mode", result.ErrorMessage);
    }

    [Fact]
    public async Task ProcessCombatOutcome_Victory_StaysInInfilMode()
    {
        // Arrange
        var mapper = new BsonMapper();
        mapper.Entity<OperatorEventDocument>().Id(x => x.Id);
        using var db = new LiteDatabase(":memory:", mapper);
        var eventStore = new LiteDbOperatorEventStore(db);
        var service = new OperatorExfilService(eventStore);
        
        var createResult = await service.CreateOperatorAsync("TestOp");
        var operatorId = createResult.Value!;
        var infilResult = await service.StartInfilAsync(operatorId);
        var sessionId = infilResult.Value; // Use the actual session ID

        var outcome = new GUNRPG.Application.Combat.CombatOutcome(
            sessionId, // Use the session ID returned by StartInfilAsync
            operatorId,
            operatorDied: false,
            xpGained: 100,
            gearLost: Array.Empty<GUNRPG.Core.Equipment.GearId>(),
            isVictory: true);

        // Act
        var result = await service.ProcessCombatOutcomeAsync(outcome, playerConfirmed: true);

        // Assert
        Assert.True(result.IsSuccess);
        
        var loadResult = await service.LoadOperatorAsync(operatorId);
        var aggregate = loadResult.Value!;
        Assert.Equal(OperatorMode.Infil, aggregate.CurrentMode); // Should stay in Infil mode
        Assert.Equal(1, aggregate.ExfilStreak);
        Assert.Equal(100, aggregate.TotalXp);
        Assert.Null(aggregate.ActiveCombatSessionId); // Should clear ActiveSessionId after victory to prevent auto-resume
    }

    [Fact]
    public async Task ProcessCombatOutcome_Death_ReturnsToBaseModeAndResetsStreak()
    {
        // Arrange
        var mapper = new BsonMapper();
        mapper.Entity<OperatorEventDocument>().Id(x => x.Id);
        using var db = new LiteDatabase(":memory:", mapper);
        var eventStore = new LiteDbOperatorEventStore(db);
        var service = new OperatorExfilService(eventStore);
        
        var createResult = await service.CreateOperatorAsync("TestOp");
        var operatorId = createResult.Value!;
        var infilResult = await service.StartInfilAsync(operatorId);
        var sessionId = infilResult.Value; // Use the actual session ID

        var outcome = new GUNRPG.Application.Combat.CombatOutcome(
            sessionId, // Use the session ID returned by StartInfilAsync
            operatorId,
            operatorDied: true,
            xpGained: 0,
            gearLost: Array.Empty<GUNRPG.Core.Equipment.GearId>(),
            isVictory: false);

        // Act
        var result = await service.ProcessCombatOutcomeAsync(outcome, playerConfirmed: true);

        // Assert
        Assert.True(result.IsSuccess);
        
        var loadResult = await service.LoadOperatorAsync(operatorId);
        var aggregate = loadResult.Value!;
        Assert.Equal(OperatorMode.Base, aggregate.CurrentMode);
        Assert.Equal(0, aggregate.ExfilStreak);
        Assert.False(aggregate.IsDead); // Operator respawns after death
        Assert.Equal(aggregate.MaxHealth, aggregate.CurrentHealth); // Health restored after respawn
    }

    [Fact]
    public async Task IsInfilTimedOut_AfterThirtyMinutes_ReturnsTrue()
    {
        // Arrange
        var mapper = new BsonMapper();
        mapper.Entity<OperatorEventDocument>().Id(x => x.Id);
        using var db = new LiteDatabase(":memory:", mapper);
        var eventStore = new LiteDbOperatorEventStore(db);
        var service = new OperatorExfilService(eventStore);
        
        var createResult = await service.CreateOperatorAsync("TestOp");
        var operatorId = createResult.Value!;
        
        // Load to get the correct previous hash
        var loadResult = await service.LoadOperatorAsync(operatorId);
        var aggregate = loadResult.Value!;
        var previousHash = aggregate.GetLastEventHash();
        
        // Start infil with a timestamp 31 minutes ago
        var oldTimestamp = DateTimeOffset.UtcNow.AddMinutes(-31);
        var infilEvent = new InfilStartedEvent(
            operatorId,
            1,
            Guid.NewGuid(),
            "TestLoadout",
            oldTimestamp,
            previousHash);
        await eventStore.AppendEventAsync(infilEvent);

        // Act
        var result = await service.IsInfilTimedOutAsync(operatorId);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    [Fact]
    public async Task IsInfilTimedOut_BeforeThirtyMinutes_ReturnsFalse()
    {
        // Arrange  
        var createResult = await _service.CreateOperatorAsync("TestOp");
        var operatorId = createResult.Value!;
        await _service.StartInfilAsync(operatorId);

        // Act
        var result = await _service.IsInfilTimedOutAsync(operatorId);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.False(result.Value);
    }

    [Fact]
    public async Task ProcessCombatOutcome_NonVictorySurvival_ReturnsToBaseMode()
    {
        // Arrange
        var mapper = new BsonMapper();
        mapper.Entity<OperatorEventDocument>().Id(x => x.Id);
        using var db = new LiteDatabase(":memory:", mapper);
        var eventStore = new LiteDbOperatorEventStore(db);
        var service = new OperatorExfilService(eventStore);
        
        var createResult = await service.CreateOperatorAsync("TestOp");
        var operatorId = createResult.Value!;
        var infilResult = await service.StartInfilAsync(operatorId);
        var sessionId = infilResult.Value;

        // Operator survived but did not win
        var outcome = new GUNRPG.Application.Combat.CombatOutcome(
            sessionId,
            operatorId,
            operatorDied: false,
            xpGained: 0,
            gearLost: Array.Empty<GUNRPG.Core.Equipment.GearId>(),
            isVictory: false);

        // Act
        var result = await service.ProcessCombatOutcomeAsync(outcome, playerConfirmed: true);

        // Assert
        Assert.True(result.IsSuccess);
        
        var loadResult = await service.LoadOperatorAsync(operatorId);
        var aggregate = loadResult.Value!;
        Assert.Equal(OperatorMode.Base, aggregate.CurrentMode);
        Assert.Equal(0, aggregate.ExfilStreak);
        Assert.Null(aggregate.ActiveCombatSessionId);
    }
}
