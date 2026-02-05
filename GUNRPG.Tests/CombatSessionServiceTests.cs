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
        Assert.NotEqual(CombatPhase.Executing, result.State!.Phase);
        Assert.True(result.State.CurrentTimeMs >= 0);
    }

    [Fact]
    public void ApplyPetAction_MissionUpdatesStress()
    {
        var store = new InMemoryCombatSessionStore();
        var service = new CombatSessionService(store);
        var session = service.CreateSession(new SessionCreateRequest { Seed = 7 });

        var petState = service.ApplyPetAction(session.Id, new PetActionRequest
        {
            Action = "mission",
            HitsTaken = 2,
            OpponentDifficulty = 80
        });

        Assert.True(petState.Stress > 0);
    }
}
