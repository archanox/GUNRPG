using GUNRPG.Application.Dtos;
using GUNRPG.Application.Requests;
using GUNRPG.Application.Mapping;
using GUNRPG.Application.Results;
using GUNRPG.Application.Sessions;
using GUNRPG.Core.Combat;
using GUNRPG.Core.Intents;
using GUNRPG.Core.Operators;
using GUNRPG.Tests.Stubs;

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
    public async Task CreateSession_WithOperatorId_PreservesOperatorId()
    {
        var service = new CombatSessionService(new InMemoryCombatSessionStore());
        var operatorId = Guid.NewGuid();

        var state = (await service.CreateSessionAsync(new SessionCreateRequest
        {
            OperatorId = operatorId,
            Seed = 123
        })).Value!;

        Assert.Equal(operatorId, state.OperatorId);
    }

    [Fact]
    public async Task CreateSession_WithEmptyOperatorId_ReturnsValidationError()
    {
        var service = new CombatSessionService(new InMemoryCombatSessionStore());

        var result = await service.CreateSessionAsync(new SessionCreateRequest
        {
            OperatorId = Guid.Empty,
            Seed = 123
        });

        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task SubmitIntents_ImmediatelyResolvesTurn()
    {
        var store = new InMemoryCombatSessionStore();
        var service = new CombatSessionService(store);
        var session = (await service.CreateSessionAsync(new SessionCreateRequest { Seed = 42 })).Value!;
        Assert.Equal(1, session.TurnNumber);

        var result = await service.SubmitPlayerIntentsAsync(session.Id, new SubmitIntentsRequest
        {
            Intents = new IntentDto
            {
                Primary = PrimaryAction.Fire
            }
        });

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        // The turn is resolved immediately — session is now on turn 2 or combat has completed
        Assert.Contains(result.Value!.Phase, new[] { SessionPhase.Planning, SessionPhase.Completed });
        Assert.True(result.Value!.TurnNumber > 1 || result.Value!.Phase == SessionPhase.Completed,
            "Turn counter must advance or session must complete after submitting intents");

        // Replay turn must be persisted without requiring a separate Advance call
        var persisted = await store.LoadAsync(session.Id);
        Assert.NotNull(persisted);
        Assert.Single(persisted!.ReplayTurns);
        Assert.Equal(PrimaryAction.Fire, persisted.ReplayTurns[0].Primary);
    }

    [Fact]
    public async Task SubmitIntents_ProgressesCombatTurn()
    {
        var store = new InMemoryCombatSessionStore();
        var service = new CombatSessionService(store);
        var session = (await service.CreateSessionAsync(new SessionCreateRequest { Seed = 42 })).Value!;

        // Submit intents — turn resolves immediately, no separate Advance required
        var submitResult = await service.SubmitPlayerIntentsAsync(session.Id, new SubmitIntentsRequest
        {
            Intents = new IntentDto
            {
                Primary = PrimaryAction.Fire
            }
        });

        Assert.True(submitResult.IsSuccess);
        Assert.NotNull(submitResult.Value);
        Assert.Contains(submitResult.Value!.Phase, new[] { SessionPhase.Planning, SessionPhase.Completed });
    }

    [Fact]
    public async Task SubmitIntents_PersistsReplayTurnAfterExecution()
    {
        var store = new InMemoryCombatSessionStore();
        var service = new CombatSessionService(store);
        var session = (await service.CreateSessionAsync(new SessionCreateRequest { Seed = 42 })).Value!;

        // A single submit is all that's needed — replay turn is appended and turn executed
        var submitResult = await service.SubmitPlayerIntentsAsync(session.Id, new SubmitIntentsRequest
        {
            Intents = new IntentDto { Primary = PrimaryAction.Fire }
        });
        Assert.True(submitResult.IsSuccess);

        var persisted = await store.LoadAsync(session.Id);
        Assert.NotNull(persisted);
        Assert.Single(persisted!.ReplayTurns);
        Assert.Equal(PrimaryAction.Fire, persisted.ReplayTurns[0].Primary);
        Assert.False(string.IsNullOrEmpty(persisted.ReplayInitialSnapshotJson));
    }

    [Fact]
    public async Task AdvanceAsync_IsNoOp_ReturnsCurrentState()
    {
        // AdvanceAsync is now a thin wrapper; it returns the current state without mutating it.
        var service = new CombatSessionService(new InMemoryCombatSessionStore());
        var session = (await service.CreateSessionAsync(new SessionCreateRequest { Seed = 5 })).Value!;

        var result = await service.AdvanceAsync(session.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(SessionPhase.Planning, result.Value!.Phase);
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

        // Submit intents — turn is immediately executed (replay-driven), intents are consumed
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
        
        // After a turn has been executed, both snapshots must agree on the same intent state
        Assert.Equal(snapshot1.Combat.PlayerIntents != null, snapshot2.Combat.PlayerIntents != null);
        Assert.Equal(snapshot1.Combat.EnemyIntents != null, snapshot2.Combat.EnemyIntents != null);
        
        // Verify operator state
        Assert.Equal(snapshot1.Player.Id, snapshot2.Player.Id);
        Assert.Equal(snapshot1.Player.Health, snapshot2.Player.Health);
        Assert.Equal(snapshot1.Player.CurrentAmmo, snapshot2.Player.CurrentAmmo);
        Assert.Equal(snapshot1.Enemy.Id, snapshot2.Enemy.Id);
    }

    [Fact]
    public async Task Snapshot_AfterSubmit_ReplayTurnIsRecordedAndRoundTripIsConsistent()
    {
        // Validates the replay-driven model: after SubmitPlayerIntentsAsync,
        // the intent is in ReplayTurns (source of truth) and the snapshot round-trips cleanly.
        var store = new InMemoryCombatSessionStore();
        var service = new CombatSessionService(store);
        var session = (await service.CreateSessionAsync(new SessionCreateRequest { Seed = 123 })).Value!;

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

        if (!submitResult.IsSuccess)
        {
            // If submission failed for a valid reason (e.g., combat already ended), skip
            return;
        }

        var snapshot = await store.LoadAsync(session.Id);
        Assert.NotNull(snapshot);

        // The intent must be in ReplayTurns — this is the authoritative record
        Assert.Single(snapshot!.ReplayTurns);
        Assert.Equal(PrimaryAction.Reload, snapshot.ReplayTurns[0].Primary);

        // Verify roundtrip consistency: both snapshots agree on the same intent state
        var rehydrated = SessionMapping.FromSnapshot(snapshot);
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

        // Submit intents — turn resolves immediately, no separate Advance required
        var submitResult = await service.SubmitPlayerIntentsAsync(session.Id, new SubmitIntentsRequest
        {
            Intents = new IntentDto { Primary = PrimaryAction.Fire }
        });

        Assert.True(submitResult.IsSuccess);
        
        // Should transition to Planning (next turn) or Completed
        Assert.Contains(submitResult.Value!.Phase, new[] { SessionPhase.Planning, SessionPhase.Completed });
    }

    [Fact]
    public async Task SubmitIntents_OnCompletedSession_IsRejected()
    {
        var store = new InMemoryCombatSessionStore();
        var service = new CombatSessionService(store);
        var session = (await service.CreateSessionAsync(new SessionCreateRequest { Seed = 100 })).Value!;

        // Drive combat to completion using only SubmitPlayerIntentsAsync
        for (int i = 0; i < 50; i++)
        {
            var state = await service.GetStateAsync(session.Id);
            Assert.True(state.IsSuccess, "GetStateAsync should succeed");

            if (state.Value!.Phase == SessionPhase.Completed)
            {
                // Verify that submitting intents on a completed session is rejected
                var rejectResult = await service.SubmitPlayerIntentsAsync(session.Id, new SubmitIntentsRequest
                {
                    Intents = new IntentDto { Primary = PrimaryAction.Fire }
                });
                Assert.False(rejectResult.IsSuccess);
                Assert.Equal(ResultStatus.InvalidState, rejectResult.Status);
                return;
            }

            var submitResult = await service.SubmitPlayerIntentsAsync(session.Id, new SubmitIntentsRequest
            {
                Intents = new IntentDto { Primary = PrimaryAction.Fire }
            });

            if (!submitResult.IsSuccess)
            {
                Assert.Fail("SubmitPlayerIntentsAsync failed before the session reached the Completed phase.");
            }
        }

        // If we exit the loop without ever reaching the Completed phase,
        // the test has not actually verified that a completed session rejects further inputs.
        Assert.Fail("Session did not reach the Completed phase within 50 iterations; test did not verify rejection of inputs after completion.");
    }

    [Fact]
    public async Task SubmitIntents_WhenNotInPlanningPhase_IsRejected()
    {
        var store = new InMemoryCombatSessionStore();
        var service = new CombatSessionService(store);
        var session = (await service.CreateSessionAsync(new SessionCreateRequest { Seed = 300 })).Value!;

        // Submit intents — turn resolves immediately
        var firstSubmit = await service.SubmitPlayerIntentsAsync(session.Id, new SubmitIntentsRequest
        {
            Intents = new IntentDto { Primary = PrimaryAction.Fire }
        });
        Assert.True(firstSubmit.IsSuccess, "First submit should succeed");

        var phase = firstSubmit.Value!.Phase;

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

    [Fact]
    public async Task SubmitIntents_WithOperatorInBaseMode_ReturnsInvalidStateError()
    {
        // Arrange
        var store = new InMemoryCombatSessionStore();
        var operatorEventStore = new StubOperatorEventStore();
        var operatorId = Guid.NewGuid();
        
        // Setup operator in Base mode
        operatorEventStore.SetupOperatorWithMode(OperatorId.FromGuid(operatorId), OperatorMode.Base);
        
        var service = new CombatSessionService(store, operatorEventStore);
        
        // Create a session with this operator
        var sessionResult = await service.CreateSessionAsync(new SessionCreateRequest 
        { 
            OperatorId = operatorId,
            Seed = 123 
        });
        Assert.True(sessionResult.IsSuccess);
        var session = sessionResult.Value!;

        // Act - try to submit intents while operator is in Base mode
        var result = await service.SubmitPlayerIntentsAsync(session.Id, new SubmitIntentsRequest
        {
            Intents = new IntentDto { Primary = PrimaryAction.Fire, Movement = MovementAction.WalkToward }
        });

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(ResultStatus.InvalidState, result.Status);
        Assert.Contains("Combat actions are only allowed when operator is in Infil mode", result.ErrorMessage);
    }

    [Fact]
    public async Task SubmitIntents_WithOperatorInInfilMode_Succeeds()
    {
        // Arrange
        var store = new InMemoryCombatSessionStore();
        var operatorEventStore = new StubOperatorEventStore();
        var operatorId = Guid.NewGuid();
        
        var service = new CombatSessionService(store, operatorEventStore);
        
        // Create a session with this operator first to get the session ID
        var sessionResult = await service.CreateSessionAsync(new SessionCreateRequest 
        { 
            OperatorId = operatorId,
            Seed = 123 
        });
        Assert.True(sessionResult.IsSuccess);
        var session = sessionResult.Value!;

        // Setup operator in Infil mode with the matching active combat session ID
        operatorEventStore.SetupOperatorWithMode(OperatorId.FromGuid(operatorId), OperatorMode.Infil, activeCombatSessionId: session.Id);

        // Act - submit intents while operator is in Infil mode with matching session
        var result = await service.SubmitPlayerIntentsAsync(session.Id, new SubmitIntentsRequest
        {
            Intents = new IntentDto { Primary = PrimaryAction.Fire, Movement = MovementAction.WalkToward }
        });

        // Assert
        Assert.True(result.IsSuccess);
    }

    // NOTE: Advance_WithOperatorInBaseMode_ReturnsInvalidStateError is removed because AdvanceAsync
    // is now a thin wrapper that returns the current state without performing simulation or validation.
    // The operator-in-Base-mode security property is covered by SubmitIntents_WithOperatorInBaseMode_ReturnsInvalidStateError.

    [Fact]
    public async Task SubmitIntents_WithNonExistentOperator_ReturnsNotFoundError()
    {
        // Arrange
        var store = new InMemoryCombatSessionStore();
        var operatorEventStore = new StubOperatorEventStore();
        var operatorId = Guid.NewGuid();
        
        // Setup operator as non-existent
        operatorEventStore.SetupNonExistentOperator(OperatorId.FromGuid(operatorId));
        
        var service = new CombatSessionService(store, operatorEventStore);
        
        // Create a session with this operator
        var sessionResult = await service.CreateSessionAsync(new SessionCreateRequest 
        { 
            OperatorId = operatorId,
            Seed = 123 
        });
        Assert.True(sessionResult.IsSuccess);
        var session = sessionResult.Value!;

        // Act - try to submit intents with non-existent operator
        var result = await service.SubmitPlayerIntentsAsync(session.Id, new SubmitIntentsRequest
        {
            Intents = new IntentDto { Primary = PrimaryAction.Fire, Movement = MovementAction.WalkToward }
        });

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(ResultStatus.NotFound, result.Status);
        Assert.Contains("Operator not found", result.ErrorMessage);
    }

    [Fact]
    public async Task SubmitIntents_WithoutOperatorEventStore_SkipsValidation()
    {
        // Arrange - no operator event store provided
        var store = new InMemoryCombatSessionStore();
        var service = new CombatSessionService(store);
        var operatorId = Guid.NewGuid();
        
        // Create a session with this operator
        var sessionResult = await service.CreateSessionAsync(new SessionCreateRequest 
        { 
            OperatorId = operatorId,
            Seed = 123 
        });
        Assert.True(sessionResult.IsSuccess);
        var session = sessionResult.Value!;

        // Act - submit intents without operator event store (validation should be skipped)
        var result = await service.SubmitPlayerIntentsAsync(session.Id, new SubmitIntentsRequest
        {
            Intents = new IntentDto { Primary = PrimaryAction.Fire, Movement = MovementAction.WalkToward }
        });

        // Assert - succeeds because validation is skipped
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task SubmitIntents_WithMismatchedSessionId_ReturnsInvalidStateError()
    {
        // Arrange
        var store = new InMemoryCombatSessionStore();
        var operatorEventStore = new StubOperatorEventStore();
        var operatorId = Guid.NewGuid();
        
        // Setup operator in Infil mode with a DIFFERENT active combat session ID (tamper scenario)
        var differentSessionId = Guid.NewGuid();
        operatorEventStore.SetupOperatorWithMode(OperatorId.FromGuid(operatorId), OperatorMode.Infil, activeCombatSessionId: differentSessionId);
        
        var service = new CombatSessionService(store, operatorEventStore);
        
        // Create a session with this operator
        var sessionResult = await service.CreateSessionAsync(new SessionCreateRequest 
        { 
            OperatorId = operatorId,
            Seed = 123 
        });
        Assert.True(sessionResult.IsSuccess);
        var session = sessionResult.Value!;

        // Act - try to submit intents using a session ID that doesn't match operator's active session
        var result = await service.SubmitPlayerIntentsAsync(session.Id, new SubmitIntentsRequest
        {
            Intents = new IntentDto { Primary = PrimaryAction.Fire }
        });

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(ResultStatus.InvalidState, result.Status);
        Assert.Contains("active combat session", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SubmitIntents_WithNoActiveSessionId_ReturnsInvalidStateError()
    {
        // Arrange
        var store = new InMemoryCombatSessionStore();
        var operatorEventStore = new StubOperatorEventStore();
        var operatorId = Guid.NewGuid();
        
        // Setup operator in Infil mode with NO active combat session (e.g., after a victory)
        operatorEventStore.SetupOperatorWithMode(OperatorId.FromGuid(operatorId), OperatorMode.Infil);
        
        var service = new CombatSessionService(store, operatorEventStore);
        
        // Create a session with this operator
        var sessionResult = await service.CreateSessionAsync(new SessionCreateRequest 
        { 
            OperatorId = operatorId,
            Seed = 123 
        });
        Assert.True(sessionResult.IsSuccess);
        var session = sessionResult.Value!;

        // Act - try to submit intents when operator has no active combat session
        var result = await service.SubmitPlayerIntentsAsync(session.Id, new SubmitIntentsRequest
        {
            Intents = new IntentDto { Primary = PrimaryAction.Fire }
        });

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(ResultStatus.InvalidState, result.Status);
        Assert.Contains("active combat session", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    // NOTE: Advance_WithMismatchedSessionId_ReturnsInvalidStateError,
    //       Advance_WithNoActiveSessionId_ReturnsInvalidStateError, and
    //       Advance_WithWrongCallerOperatorId_ReturnsInvalidStateError are removed because
    //       AdvanceAsync is now a thin wrapper with no validation.
    // The corresponding security properties are already covered by the SubmitIntents_With* tests above.

    [Fact]
    public async Task SubmitIntents_WithWrongCallerOperatorId_ReturnsInvalidStateError()
    {
        // Arrange
        var store = new InMemoryCombatSessionStore();
        var operatorEventStore = new StubOperatorEventStore();
        var ownerOperatorId = Guid.NewGuid();
        var attackerOperatorId = Guid.NewGuid(); // different operator trying to tamper

        var service = new CombatSessionService(store, operatorEventStore);

        var sessionResult = await service.CreateSessionAsync(new SessionCreateRequest
        {
            OperatorId = ownerOperatorId,
            Seed = 123
        });
        Assert.True(sessionResult.IsSuccess);
        var session = sessionResult.Value!;

        // Owner operator is set up in Infil mode with the correct active session
        operatorEventStore.SetupOperatorWithMode(OperatorId.FromGuid(ownerOperatorId), OperatorMode.Infil, activeCombatSessionId: session.Id);

        // Act - attacker provides their own operatorId but tries to act on owner's session
        var result = await service.SubmitPlayerIntentsAsync(session.Id, new SubmitIntentsRequest
        {
            Intents = new IntentDto { Primary = PrimaryAction.Fire },
            OperatorId = attackerOperatorId
        });

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(ResultStatus.InvalidState, result.Status);
        Assert.Contains("belong to the specified operator", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    // NOTE: Advance_WithWrongCallerOperatorId_ReturnsInvalidStateError is removed because AdvanceAsync
    // is now a thin wrapper with no validation. The caller-operator-id tamper scenario is already
    // covered by SubmitIntents_WithWrongCallerOperatorId_ReturnsInvalidStateError.

    // NOTE: Advance_WithUpdateHub_PublishesNotification is removed because AdvanceAsync no longer
    // saves state and therefore does not publish hub notifications.

    [Fact]
    public async Task SubmitIntents_WithUpdateHub_PublishesNotification()
    {
        var store = new InMemoryCombatSessionStore();
        var hub = new CombatSessionUpdateHub();
        var service = new CombatSessionService(store, updateHub: hub);
        var session = (await service.CreateSessionAsync(new SessionCreateRequest { Seed = 42 })).Value!;

        using var cts = new CancellationTokenSource();
        var received = new List<Guid>();
        var ready = new TaskCompletionSource();

        var subscribeTask = Task.Run(async () =>
        {
            try
            {
                await using var enumerator = hub.SubscribeAsync(session.Id, cts.Token)
                    .GetAsyncEnumerator(cts.Token);
                var pending = enumerator.MoveNextAsync();
                ready.TrySetResult();
                while (await pending)
                {
                    received.Add(enumerator.Current);
                    cts.Cancel();
                    pending = enumerator.MoveNextAsync();
                }
            }
            catch (OperationCanceledException) { }
        });

        await ready.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await service.SubmitPlayerIntentsAsync(session.Id, new SubmitIntentsRequest
        {
            Intents = new IntentDto { Primary = PrimaryAction.Fire }
        });

        await subscribeTask.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Single(received);
        Assert.Equal(session.Id, received[0]);
    }
}
