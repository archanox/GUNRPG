using GUNRPG.Application.Dtos;
using GUNRPG.Application.Requests;
using GUNRPG.Application.Mapping;
using GUNRPG.Application.Sessions;
using GUNRPG.Core.Combat;
using GUNRPG.Core.Intents;

namespace GUNRPG.Tests;

public class CombatSessionServiceTests
{
    [Fact]
    public void CreateSession_ReturnsPlanningState()
    {
        var service = new CombatSessionService(new InMemoryCombatSessionStore());

        var state = service.CreateSession(new SessionCreateRequest { PlayerName = "Tester", Seed = 123, StartingDistance = 10 });

        Assert.NotNull(state);
        Assert.Equal(SessionPhase.Planning, state.Phase);
        Assert.Equal(CombatPhase.Planning, state.CombatPhase);
        Assert.Equal("Tester", state.Player.Name);
        Assert.True(state.Player.CurrentAmmo > 0);
        Assert.True(state.Enemy.CurrentAmmo > 0);
    }

    [Fact]
    public void SubmitIntents_RecordsWithoutAdvancing()
    {
        var store = new InMemoryCombatSessionStore();
        var service = new CombatSessionService(store);
        var session = service.CreateSession(new SessionCreateRequest { Seed = 42 });

        var result = service.SubmitPlayerIntents(session.Id, new SubmitIntentsRequest
        {
            Intents = new IntentDto
            {
                Primary = PrimaryAction.Fire
            }
        });

        Assert.True(result.Accepted);
        Assert.NotNull(result.State);
        Assert.Equal(SessionPhase.Planning, result.State!.Phase);
        Assert.Equal(CombatPhase.Planning, result.State.CombatPhase);
    }

    [Fact]
    public void Advance_ProgressesCombatTurn()
    {
        var store = new InMemoryCombatSessionStore();
        var service = new CombatSessionService(store);
        var session = service.CreateSession(new SessionCreateRequest { Seed = 42 });

        // Submit intents first
        service.SubmitPlayerIntents(session.Id, new SubmitIntentsRequest
        {
            Intents = new IntentDto
            {
                Primary = PrimaryAction.Fire
            }
        });

        // Now advance the turn
        var advanceResult = service.Advance(session.Id);

        Assert.True(advanceResult.IsSuccess);
        Assert.NotNull(advanceResult.Value);
        Assert.NotEqual(CombatPhase.Executing, advanceResult.Value!.CombatPhase);
        Assert.Contains(advanceResult.Value!.Phase, new[] { SessionPhase.Planning, SessionPhase.Completed });
    }

    [Fact]
    public void Advance_WithoutIntents_IsInvalid()
    {
        var service = new CombatSessionService(new InMemoryCombatSessionStore());
        var session = service.CreateSession(new SessionCreateRequest { Seed = 5 });

        var result = service.Advance(session.Id);

        Assert.False(result.IsSuccess);
        Assert.Equal("Advance requires recorded intents for both sides", result.ErrorMessage);
    }

    [Fact]
    public void Snapshot_RoundTripsThroughStore()
    {
        var store = new InMemoryCombatSessionStore();
        var service = new CombatSessionService(store);
        var created = service.CreateSession(new SessionCreateRequest { Seed = 11 });

        var snapshot = store.Get(created.Id);
        Assert.NotNull(snapshot);

        var rehydrated = SessionMapping.FromSnapshot(snapshot!);
        Assert.Equal(created.Id, rehydrated.Id);
        Assert.Equal(SessionPhase.Planning, rehydrated.Phase);
        Assert.Equal(created.TurnNumber, rehydrated.TurnNumber);
    }

    [Fact]
    public void ApplyPetAction_MissionUpdatesStress()
    {
        var store = new InMemoryCombatSessionStore();
        var service = new CombatSessionService(store);
        var session = service.CreateSession(new SessionCreateRequest { Seed = 7 });

        var petStateResult = service.ApplyPetAction(session.Id, new PetActionRequest
        {
            Action = "mission",
            HitsTaken = 2,
            OpponentDifficulty = 80
        });

        Assert.True(petStateResult.IsSuccess);
        Assert.NotNull(petStateResult.Value);
        Assert.True(petStateResult.Value.Stress > 0);
    }
}
