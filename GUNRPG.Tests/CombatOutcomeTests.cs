using GUNRPG.Application.Dtos;
using GUNRPG.Application.Mapping;
using GUNRPG.Application.Requests;
using GUNRPG.Application.Results;
using GUNRPG.Application.Sessions;
using GUNRPG.Core.Combat;
using GUNRPG.Core.Equipment;
using GUNRPG.Core.Intents;
using GUNRPG.Core.Operators;
using GUNRPG.Tests.Stubs;

namespace GUNRPG.Tests;

public class CombatOutcomeTests
{
    [Fact]
    public async Task GetOutcome_ThrowsException_WhenSessionNotCompleted()
    {
        // Arrange
        var store = new InMemoryCombatSessionStore();
        var service = new CombatSessionService(store);
        var session = (await service.CreateSessionAsync(new SessionCreateRequest { Seed = 42 })).Value!;
        
        var loadedSession = SessionMapping.FromSnapshot(await store.LoadAsync(session.Id));

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => loadedSession!.GetOutcome());
        Assert.Contains("not completed", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetOutcome_ProducesOutcome_WhenSessionCompleted()
    {
        // Arrange
        var store = new InMemoryCombatSessionStore();
        var service = new CombatSessionService(store);
        var session = (await service.CreateSessionAsync(new SessionCreateRequest { Seed = 42 })).Value!;

        // Advance session until completion
        for (int i = 0; i < 100; i++)
        {
            var state = await service.GetStateAsync(session.Id);
            if (state.Value!.Phase == SessionPhase.Completed)
                break;

            await service.SubmitPlayerIntentsAsync(session.Id, new SubmitIntentsRequest
            {
                Intents = new IntentDto { Primary = PrimaryAction.Fire }
            });

            await service.AdvanceAsync(session.Id);
        }

        var loadedSession = SessionMapping.FromSnapshot(await store.LoadAsync(session.Id));
        Assert.Equal(SessionPhase.Completed, loadedSession!.Phase);

        // Act
        var outcome = loadedSession.GetOutcome();

        // Assert
        Assert.NotNull(outcome);
        Assert.Equal(session.Id, outcome.SessionId);
        Assert.NotNull(outcome.GearLost);
    }

    [Fact]
    public async Task GetOutcome_ReportsOperatorDeathCorrectly()
    {
        // Arrange
        var store = new InMemoryCombatSessionStore();
        var service = new CombatSessionService(store);
        
        // Create session and force combat until completion
        var session = (await service.CreateSessionAsync(new SessionCreateRequest { Seed = 123 })).Value!;

        // Advance session until completion
        for (int i = 0; i < 100; i++)
        {
            var state = await service.GetStateAsync(session.Id);
            if (state.Value!.Phase == SessionPhase.Completed)
                break;

            await service.SubmitPlayerIntentsAsync(session.Id, new SubmitIntentsRequest
            {
                Intents = new IntentDto { Primary = PrimaryAction.Fire }
            });

            await service.AdvanceAsync(session.Id);
        }

        var loadedSession = SessionMapping.FromSnapshot(await store.LoadAsync(session.Id));
        Assert.Equal(SessionPhase.Completed, loadedSession!.Phase);

        // Act
        var outcome = loadedSession.GetOutcome();

        // Assert
        Assert.NotNull(outcome);
        Assert.Equal(!loadedSession.Player.IsAlive, outcome.OperatorDied);
    }

    [Fact]
    public async Task GetOutcome_CalculatesXpCorrectly_ForAllOutcomes()
    {
        // Arrange
        var store = new InMemoryCombatSessionStore();
        var service = new CombatSessionService(store);
        var session = (await service.CreateSessionAsync(new SessionCreateRequest { Seed = 50 })).Value!;

        // Advance session until completion
        for (int i = 0; i < 100; i++)
        {
            var state = await service.GetStateAsync(session.Id);
            if (state.Value!.Phase == SessionPhase.Completed)
                break;

            await service.SubmitPlayerIntentsAsync(session.Id, new SubmitIntentsRequest
            {
                Intents = new IntentDto { Primary = PrimaryAction.Fire }
            });

            await service.AdvanceAsync(session.Id);
        }

        var loadedSession = SessionMapping.FromSnapshot(await store.LoadAsync(session.Id));
        Assert.Equal(SessionPhase.Completed, loadedSession!.Phase);

        // Act
        var outcome = loadedSession.GetOutcome();

        // Assert
        Assert.NotNull(outcome);
        
        // XP calculation rules:
        // - Victory: 100 XP
        // - Survival (no victory): 50 XP
        // - Death: 0 XP
        if (outcome.IsVictory)
        {
            Assert.Equal(100, outcome.XpGained);
        }
        else if (!outcome.OperatorDied)
        {
            Assert.Equal(50, outcome.XpGained);
        }
        else
        {
            Assert.Equal(0, outcome.XpGained);
        }
    }

    [Fact]
    public async Task GetOutcome_IsDeterministic_WhenCalledMultipleTimes()
    {
        // Arrange
        var store = new InMemoryCombatSessionStore();
        var service = new CombatSessionService(store);
        var session = (await service.CreateSessionAsync(new SessionCreateRequest { Seed = 999 })).Value!;

        // Advance session until completion
        for (int i = 0; i < 100; i++)
        {
            var state = await service.GetStateAsync(session.Id);
            if (state.Value!.Phase == SessionPhase.Completed)
                break;

            await service.SubmitPlayerIntentsAsync(session.Id, new SubmitIntentsRequest
            {
                Intents = new IntentDto { Primary = PrimaryAction.Fire }
            });

            await service.AdvanceAsync(session.Id);
        }

        var loadedSession = SessionMapping.FromSnapshot(await store.LoadAsync(session.Id));
        Assert.Equal(SessionPhase.Completed, loadedSession!.Phase);

        // Act
        var outcome1 = loadedSession.GetOutcome();
        var outcome2 = loadedSession.GetOutcome();

        // Assert
        Assert.Equal(outcome1.SessionId, outcome2.SessionId);
        Assert.Equal(outcome1.OperatorId.Value, outcome2.OperatorId.Value);
        Assert.Equal(outcome1.OperatorDied, outcome2.OperatorDied);
        Assert.Equal(outcome1.XpGained, outcome2.XpGained);
        Assert.Equal(outcome1.IsVictory, outcome2.IsVictory);
        Assert.Equal(outcome1.DamageTaken, outcome2.DamageTaken);
        Assert.Equal(outcome1.TurnsSurvived, outcome2.TurnsSurvived);
        Assert.Equal(outcome1.CompletedAt, outcome2.CompletedAt);
    }

    [Fact]
    public void CombatOutcome_Constructor_ValidatesXpGained()
    {
        // Arrange
        var operatorId = OperatorId.NewId();

        // Act & Assert
        Assert.Throws<ArgumentException>(() => new Application.Combat.CombatOutcome(
            sessionId: Guid.NewGuid(),
            operatorId: operatorId,
            operatorDied: false,
            xpGained: -100, // Invalid: negative XP
            gearLost: Array.Empty<GearId>(),
            isVictory: false,
            turnsSurvived: 0,
            damageTaken: 0f));
    }

    [Fact]
    public void CombatOutcome_Constructor_ValidatesTurnsSurvived()
    {
        // Arrange
        var operatorId = OperatorId.NewId();

        // Act & Assert
        Assert.Throws<ArgumentException>(() => new Application.Combat.CombatOutcome(
            sessionId: Guid.NewGuid(),
            operatorId: operatorId,
            operatorDied: false,
            xpGained: 100,
            gearLost: Array.Empty<GearId>(),
            isVictory: false,
            turnsSurvived: -5, // Invalid: negative turns
            damageTaken: 0f));
    }

    [Fact]
    public void CombatOutcome_Constructor_ValidatesDamageTaken()
    {
        // Arrange
        var operatorId = OperatorId.NewId();

        // Act & Assert
        Assert.Throws<ArgumentException>(() => new Application.Combat.CombatOutcome(
            sessionId: Guid.NewGuid(),
            operatorId: operatorId,
            operatorDied: false,
            xpGained: 100,
            gearLost: Array.Empty<GearId>(),
            isVictory: false,
            turnsSurvived: 0,
            damageTaken: -10f)); // Invalid: negative damage
    }

    [Fact]
    public void CombatOutcome_Constructor_RequiresGearLost()
    {
        // Arrange
        var operatorId = OperatorId.NewId();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new Application.Combat.CombatOutcome(
            sessionId: Guid.NewGuid(),
            operatorId: operatorId,
            operatorDied: false,
            xpGained: 100,
            gearLost: null!, // Invalid: null
            isVictory: false,
            turnsSurvived: 0,
            damageTaken: 0f));
    }

    [Fact]
    public void CombatOutcome_Constructor_RejectsVictoryWhenOperatorDied()
    {
        // Arrange
        var operatorId = OperatorId.NewId();

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => new Application.Combat.CombatOutcome(
            sessionId: Guid.NewGuid(),
            operatorId: operatorId,
            operatorDied: true, // Operator died
            xpGained: 0,
            gearLost: Array.Empty<GearId>(),
            isVictory: true, // Invalid: can't have victory if operator died
            turnsSurvived: 5,
            damageTaken: 100f));
        
        Assert.Contains("victory", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("operator", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CombatSession_PreventsIntentSubmission_WhenCompleted()
    {
        // Arrange
        var store = new InMemoryCombatSessionStore();
        var service = new CombatSessionService(store);
        var session = (await service.CreateSessionAsync(new SessionCreateRequest { Seed = 200 })).Value!;

        // Advance session until completion
        for (int i = 0; i < 100; i++)
        {
            var state = await service.GetStateAsync(session.Id);
            if (state.Value!.Phase == SessionPhase.Completed)
                break;

            await service.SubmitPlayerIntentsAsync(session.Id, new SubmitIntentsRequest
            {
                Intents = new IntentDto { Primary = PrimaryAction.Fire }
            });

            await service.AdvanceAsync(session.Id);
        }

        var loadedSession = SessionMapping.FromSnapshot(await store.LoadAsync(session.Id));
        Assert.Equal(SessionPhase.Completed, loadedSession!.Phase);

        // Act
        var result = await service.SubmitPlayerIntentsAsync(session.Id, new SubmitIntentsRequest
        {
            Intents = new IntentDto { Primary = PrimaryAction.Fire }
        });

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(ResultStatus.InvalidState, result.Status);
    }

    [Fact]
    public async Task GetOutcome_UsesSessionOperatorId_NotPlayerId()
    {
        // Arrange - create session with an explicit operator ID
        var store = new InMemoryCombatSessionStore();
        var service = new CombatSessionService(store);
        var explicitOperatorId = Guid.NewGuid();

        var session = (await service.CreateSessionAsync(new SessionCreateRequest
        {
            Seed = 42,
            OperatorId = explicitOperatorId
        })).Value!;

        // Advance session until completion
        for (int i = 0; i < 100; i++)
        {
            var state = await service.GetStateAsync(session.Id);
            if (state.Value!.Phase == SessionPhase.Completed)
                break;

            await service.SubmitPlayerIntentsAsync(session.Id, new SubmitIntentsRequest
            {
                Intents = new IntentDto { Primary = PrimaryAction.Fire }
            });

            await service.AdvanceAsync(session.Id);
        }

        var loadedSession = SessionMapping.FromSnapshot(await store.LoadAsync(session.Id));
        Assert.Equal(SessionPhase.Completed, loadedSession!.Phase);

        // Act
        var outcome = loadedSession.GetOutcome();

        // Assert - outcome should use session's OperatorId, not Player.Id
        Assert.Equal(explicitOperatorId, outcome.OperatorId.Value);
        Assert.NotEqual(loadedSession.Player.Id, explicitOperatorId); // Verify they differ
    }
}
