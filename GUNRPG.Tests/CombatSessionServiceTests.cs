using GUNRPG.Application.Dtos;
using GUNRPG.Application.Requests;
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
        Assert.Equal(CombatPhase.Planning, state.Phase);
        Assert.Equal("Tester", state.Player.Name);
        Assert.True(state.Player.CurrentAmmo > 0);
        Assert.True(state.Enemy.CurrentAmmo > 0);
    }

    [Fact]
    public void SubmitIntents_AdvancesCombat()
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
        // After submitting intents, combat should begin execution but not auto-advance
        Assert.Equal(CombatPhase.Executing, result.State!.Phase);
        Assert.True(result.State.CurrentTimeMs >= 0);
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
        // After advancing, combat should be in Planning or Ended phase
        Assert.NotEqual(CombatPhase.Executing, advanceResult.Value!.Phase);
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
