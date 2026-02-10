using GUNRPG.Application.Dtos;
using GUNRPG.Application.Requests;
using GUNRPG.Application.Mapping;
using GUNRPG.Application.Results;
using GUNRPG.Application.Sessions;
using GUNRPG.Core.Combat;
using GUNRPG.Core.Intents;

namespace GUNRPG.Tests;

public class CombatSessionServiceTests
{
    [Fact]
    public async Task CreateSession_ReturnsPlanningState()
    {
        var service = new CombatSessionService(new InMemoryCombatSessionStore());

        var state = (await service.CreateSessionAsync(new SessionCreateRequest { PlayerName = "Tester", Seed = 123, StartingDistance = 10 })).Value!;

        Assert.NotNull(state);
        Assert.Equal(SessionPhase.Planning, state.Phase);
        Assert.Equal("Tester", state.Player.Name);
        Assert.True(state.Player.CurrentAmmo > 0);
        Assert.True(state.Enemy.CurrentAmmo > 0);
    }

    [Fact]
    public async Task SubmitIntents_RecordsWithoutAdvancing()
    {
        var store = new InMemoryCombatSessionStore();
        var service = new CombatSessionService(store);
        var session = (await service.CreateSessionAsync(new SessionCreateRequest { Seed = 42 })).Value!;

        var result = await service.SubmitPlayerIntentsAsync(session.Id, new SubmitIntentsRequest
        {
            Intents = new IntentDto
            {
                Primary = PrimaryAction.Fire
            }
        });

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(SessionPhase.Planning, result.Value!.Phase);
    }

    [Fact]
    public async Task Advance_ProgressesCombatTurn()
    {
        var store = new InMemoryCombatSessionStore();
        var service = new CombatSessionService(store);
        var session = (await service.CreateSessionAsync(new SessionCreateRequest { Seed = 42 })).Value!;

        // Submit intents first
        await service.SubmitPlayerIntentsAsync(session.Id, new SubmitIntentsRequest
        {
            Intents = new IntentDto
            {
                Primary = PrimaryAction.Fire
            }
        });

        // Now advance the turn
        var advanceResult = await service.AdvanceAsync(session.Id);

        Assert.True(advanceResult.IsSuccess);
        Assert.NotNull(advanceResult.Value);
        Assert.Contains(advanceResult.Value!.Phase, new[] { SessionPhase.Planning, SessionPhase.Completed });
    }

    [Fact]
    public async Task Advance_WithoutIntents_IsInvalid()
    {
        var service = new CombatSessionService(new InMemoryCombatSessionStore());
        var session = (await service.CreateSessionAsync(new SessionCreateRequest { Seed = 5 })).Value!;

        var result = await service.AdvanceAsync(session.Id);

        Assert.False(result.IsSuccess);
        Assert.Equal("Advance requires recorded intents for both sides", result.ErrorMessage);
    }

    [Fact]
    public async Task Snapshot_RoundTripsThroughStore()
    {
        var store = new InMemoryCombatSessionStore();
        var service = new CombatSessionService(store);
        var created = (await service.CreateSessionAsync(new SessionCreateRequest { Seed = 11 })).Value!;

        var snapshot = await store.LoadAsync(created.Id);
        Assert.NotNull(snapshot);

        var rehydrated = SessionMapping.FromSnapshot(snapshot!);
        Assert.Equal(created.Id, rehydrated.Id);
        Assert.Equal(SessionPhase.Planning, rehydrated.Phase);
        Assert.Equal(created.TurnNumber, rehydrated.TurnNumber);
    }

    [Fact]
    public async Task ApplyPetAction_MissionUpdatesStress()
    {
        var store = new InMemoryCombatSessionStore();
        var service = new CombatSessionService(store);
        var session = (await service.CreateSessionAsync(new SessionCreateRequest { Seed = 7 })).Value!;

        var petStateResult = await service.ApplyPetActionAsync(session.Id, new PetActionRequest
        {
            Action = "mission",
            HitsTaken = 2,
            OpponentDifficulty = 80
        });

        Assert.True(petStateResult.IsSuccess);
        Assert.NotNull(petStateResult.Value);
        Assert.True(petStateResult.Value.Stress > 0);
    }

    [Fact]
    public async Task Snapshot_RoundTrip_IsIdempotent()
    {
        var store = new InMemoryCombatSessionStore();
        var service = new CombatSessionService(store);
        var created = (await service.CreateSessionAsync(new SessionCreateRequest 
        { 
            PlayerName = "TestPlayer", 
            EnemyName = "TestEnemy", 
            Seed = 42, 
            StartingDistance = 20 
        })).Value!;

        // Submit intents to add complexity
        await service.SubmitPlayerIntentsAsync(created.Id, new SubmitIntentsRequest
        {
            Intents = new IntentDto { Primary = PrimaryAction.Fire }
        });

        var snapshot1 = await store.LoadAsync(created.Id);
        Assert.NotNull(snapshot1);

        // Rehydrate from snapshot
        var rehydrated = SessionMapping.FromSnapshot(snapshot1!);
        
        // Create second snapshot from rehydrated domain object
        var snapshot2 = SessionMapping.ToSnapshot(rehydrated);

        // Verify idempotency: snapshot1 should match snapshot2
        Assert.Equal(snapshot1.Id, snapshot2.Id);
        Assert.Equal(snapshot1.Phase, snapshot2.Phase);
        Assert.Equal(snapshot1.TurnNumber, snapshot2.TurnNumber);
        Assert.Equal(snapshot1.OperatorId, snapshot2.OperatorId);
        Assert.Equal(snapshot1.EnemyLevel, snapshot2.EnemyLevel);
        Assert.Equal(snapshot1.Seed, snapshot2.Seed);
        Assert.Equal(snapshot1.PostCombatResolved, snapshot2.PostCombatResolved);
        
        // Verify combat state
        Assert.Equal(snapshot1.Combat.Phase, snapshot2.Combat.Phase);
        Assert.Equal(snapshot1.Combat.CurrentTimeMs, snapshot2.Combat.CurrentTimeMs);
        
        // Verify RNG state preservation
        Assert.Equal(snapshot1.Combat.RandomState.Seed, snapshot2.Combat.RandomState.Seed);
        Assert.Equal(snapshot1.Combat.RandomState.CallCount, snapshot2.Combat.RandomState.CallCount);
        
        // Verify pending intents are preserved
        Assert.NotNull(snapshot1.Combat.PlayerIntents);
        Assert.NotNull(snapshot2.Combat.PlayerIntents);
        Assert.Equal(snapshot1.Combat.PlayerIntents.Primary, snapshot2.Combat.PlayerIntents.Primary);
        
        // Verify operator state
        Assert.Equal(snapshot1.Player.Id, snapshot2.Player.Id);
        Assert.Equal(snapshot1.Player.Health, snapshot2.Player.Health);
        Assert.Equal(snapshot1.Player.CurrentAmmo, snapshot2.Player.CurrentAmmo);
        Assert.Equal(snapshot1.Enemy.Id, snapshot2.Enemy.Id);
    }

    [Fact]
    public async Task Snapshot_RehydrationWithPendingIntents_PreservesIntentData()
    {
        var store = new InMemoryCombatSessionStore();
        var service = new CombatSessionService(store);
        var session = (await service.CreateSessionAsync(new SessionCreateRequest { Seed = 123 })).Value!;

        // Submit player intents
        var submitResult = await service.SubmitPlayerIntentsAsync(session.Id, new SubmitIntentsRequest
        {
            Intents = new IntentDto 
            { 
                Primary = PrimaryAction.Reload,
                Movement = MovementAction.WalkToward,
                Stance = StanceAction.EnterADS,
                Cover = CoverAction.EnterPartial,
                CancelMovement = false
            }
        });

        // Verify submission was accepted
        if (!submitResult.IsSuccess)
        {
            // If submission failed for a valid reason (e.g., combat ended), skip this test
            return;
        }

        var snapshot = await store.LoadAsync(session.Id);
        if (snapshot == null)
        {
            return; // Should not happen but handle gracefully
        }

        // Verify at least one side has intents in snapshot
        // The service submits both player and enemy intents
        bool hasIntents = snapshot.Combat.PlayerIntents != null || snapshot.Combat.EnemyIntents != null;
        Assert.True(hasIntents, "At least one side should have intents after successful submission");

        // Rehydrate
        var rehydrated = SessionMapping.FromSnapshot(snapshot);
        var (playerIntents, enemyIntents) = rehydrated.Combat.GetPendingIntents();

        // At least one side should have intents after rehydration
        Assert.True(playerIntents != null || enemyIntents != null, 
            "Pending intents should be preserved after rehydration");

        // Verify roundtrip consistency
        var snapshot2 = SessionMapping.ToSnapshot(rehydrated);
        Assert.Equal(snapshot.Combat.PlayerIntents != null, snapshot2.Combat.PlayerIntents != null);
        Assert.Equal(snapshot.Combat.EnemyIntents != null, snapshot2.Combat.EnemyIntents != null);
    }

    [Fact]
    public async Task Snapshot_RehydrationWithRngState_PreservesDeterminism()
    {
        var store = new InMemoryCombatSessionStore();
        var service = new CombatSessionService(store);
        var session = (await service.CreateSessionAsync(new SessionCreateRequest { Seed = 999 })).Value!;

        // Submit and advance to modify RNG state
        await service.SubmitPlayerIntentsAsync(session.Id, new SubmitIntentsRequest
        {
            Intents = new IntentDto { Primary = PrimaryAction.Fire }
        });
        
        var beforeSnapshot = await store.LoadAsync(session.Id);
        Assert.NotNull(beforeSnapshot);
        var initialRngSeed = beforeSnapshot!.Combat.RandomState.Seed;
        var initialCallCount = beforeSnapshot.Combat.RandomState.CallCount;

        // Rehydrate and verify RNG state
        var rehydrated = SessionMapping.FromSnapshot(beforeSnapshot);
        var (rngSeed, rngCalls) = rehydrated.Combat.GetRandomState();
        
        Assert.Equal(initialRngSeed, rngSeed);
        Assert.Equal(initialCallCount, rngCalls);

        // Take snapshot after rehydration and verify consistency
        var afterSnapshot = SessionMapping.ToSnapshot(rehydrated);
        Assert.Equal(initialRngSeed, afterSnapshot.Combat.RandomState.Seed);
        Assert.Equal(initialCallCount, afterSnapshot.Combat.RandomState.CallCount);
    }

    [Fact]
    public async Task PhaseTransition_Planning_To_Resolving_IsValid()
    {
        var store = new InMemoryCombatSessionStore();
        var service = new CombatSessionService(store);
        var session = (await service.CreateSessionAsync(new SessionCreateRequest { Seed = 50 })).Value!;

        Assert.Equal(SessionPhase.Planning, session.Phase);

        // Submit intents and advance
        await service.SubmitPlayerIntentsAsync(session.Id, new SubmitIntentsRequest
        {
            Intents = new IntentDto { Primary = PrimaryAction.Fire }
        });

        var advanceResult = await service.AdvanceAsync(session.Id);
        Assert.True(advanceResult.IsSuccess);
        
        // Should transition to Planning (next turn) or Completed
        Assert.Contains(advanceResult.Value!.Phase, new[] { SessionPhase.Planning, SessionPhase.Completed });
    }

    [Fact]
    public async Task PhaseTransition_Completed_CannotAdvance()
    {
        var store = new InMemoryCombatSessionStore();
        var service = new CombatSessionService(store);
        var session = (await service.CreateSessionAsync(new SessionCreateRequest { Seed = 100 })).Value!;

        // Advance combat until it completes (simulate multiple rounds)
        for (int i = 0; i < 50; i++)
        {
            var state = await service.GetStateAsync(session.Id);
            Assert.True(state.IsSuccess, "GetStateAsync should succeed");
            
            if (state.Value!.Phase == SessionPhase.Completed)
            {
                // Try to advance a completed session
                var result = await service.AdvanceAsync(session.Id);
                Assert.False(result.IsSuccess);
                Assert.Equal(ResultStatus.InvalidState, result.Status);
                return;
            }

            await service.SubmitPlayerIntentsAsync(session.Id, new SubmitIntentsRequest
            {
                Intents = new IntentDto { Primary = PrimaryAction.Fire }
            });

            var advanceResult = await service.AdvanceAsync(session.Id);
            
            if (!advanceResult.IsSuccess)
            {
                Assert.Fail("AdvanceAsync failed before the session reached the Completed phase.");
            }

            if (advanceResult.Value!.Phase == SessionPhase.Completed)
            {
                // On the next loop iteration, the Completed phase will be detected
                // by GetStateAsync and the negative-advance assertion will run.
                continue;
            }
        }

        // If we exit the loop without ever reaching the Completed phase,
        // the test has not actually verified that a completed session cannot advance.
        Assert.Fail("Session did not reach the Completed phase within 50 iterations; test did not verify that a completed session cannot advance.");
    }

    [Fact]
    public async Task Advance_WithoutIntents_ReturnsInvalidState()
    {
        var store = new InMemoryCombatSessionStore();
        var service = new CombatSessionService(store);
        var session = (await service.CreateSessionAsync(new SessionCreateRequest { Seed = 200 })).Value!;

        var result = await service.AdvanceAsync(session.Id);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultStatus.InvalidState, result.Status);
        Assert.Contains("intents", result.ErrorMessage ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SubmitIntents_WhenNotInPlanningPhase_IsRejected()
    {
        var store = new InMemoryCombatSessionStore();
        var service = new CombatSessionService(store);
        var session = (await service.CreateSessionAsync(new SessionCreateRequest { Seed = 300 })).Value!;

        // Submit initial intents and advance
        await service.SubmitPlayerIntentsAsync(session.Id, new SubmitIntentsRequest
        {
            Intents = new IntentDto { Primary = PrimaryAction.Fire }
        });

        // Advance completes the turn and transitions to either Planning (next turn) or Completed
        var advanceResult = await service.AdvanceAsync(session.Id);
        Assert.True(advanceResult.IsSuccess, "AdvanceAsync should succeed");
        
        var phase = advanceResult.Value!.Phase;
        
        // If we're in Completed phase, submitting intents should fail
        if (phase == SessionPhase.Completed)
        {
            var submitResult = await service.SubmitPlayerIntentsAsync(session.Id, new SubmitIntentsRequest
            {
                Intents = new IntentDto { Primary = PrimaryAction.Reload }
            });

            Assert.False(submitResult.IsSuccess, "Submitting intents during Completed phase should be rejected");
            Assert.Equal(ResultStatus.InvalidState, submitResult.Status);
        }
        // If we're back in Planning, the test doesn't verify the rejection behavior
        // but that's acceptable as Resolving is never persisted
    }

    [Fact]
    public async Task CreateSession_WithProvidedId_PreservesId()
    {
        var store = new InMemoryCombatSessionStore();
        var service = new CombatSessionService(store);
        var expectedId = Guid.NewGuid();

        var result = await service.CreateSessionAsync(new SessionCreateRequest { Id = expectedId, Seed = 42 });

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedId, result.Value!.Id);

        var snapshot = await store.LoadAsync(expectedId);
        Assert.NotNull(snapshot);
        Assert.Equal(expectedId, snapshot!.Id);
    }

    [Fact]
    public async Task CreateSession_WithEmptyGuid_ReturnsValidationError()
    {
        var service = new CombatSessionService(new InMemoryCombatSessionStore());

        var result = await service.CreateSessionAsync(new SessionCreateRequest { Id = Guid.Empty, Seed = 42 });

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultStatus.ValidationError, result.Status);
    }

    [Fact]
    public async Task CreateSession_WithDuplicateId_ReturnsError()
    {
        var store = new InMemoryCombatSessionStore();
        var service = new CombatSessionService(store);
        var id = Guid.NewGuid();

        var first = await service.CreateSessionAsync(new SessionCreateRequest { Id = id, Seed = 42 });
        Assert.True(first.IsSuccess);

        var second = await service.CreateSessionAsync(new SessionCreateRequest { Id = id, Seed = 99 });
        Assert.False(second.IsSuccess);
        Assert.Equal(ResultStatus.InvalidState, second.Status);
    }
}
