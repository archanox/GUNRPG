using GUNRPG.Core;
using GUNRPG.Core.Combat;
using GUNRPG.Core.Intents;
using GUNRPG.Core.Operators;
using Xunit;

namespace GUNRPG.Tests;

public class CombatSystemTests
{
    [Fact]
    public void CombatSystem_InitializesInPlanningPhase()
    {
        var player = new Operator("Player");
        var enemy = new Operator("Enemy");
        
        var combat = new CombatSystem(player, enemy);
        
        Assert.Equal(CombatPhase.Planning, combat.Phase);
        Assert.Equal(0, combat.CurrentTimeMs);
    }

    [Fact]
    public void SubmitIntent_RejectsInvalidIntent()
    {
        var player = new Operator("Player");
        var enemy = new Operator("Enemy");
        var combat = new CombatSystem(player, enemy);
        
        // Try to fire without ammo
        player.CurrentAmmo = 0;
        var result = combat.SubmitIntent(player, new FireWeaponIntent(player.Id));
        
        Assert.False(result.success);
        Assert.NotNull(result.errorMessage);
    }

    [Fact]
    public void SubmitIntent_AcceptsValidIntent()
    {
        var player = new Operator("Player")
        {
            EquippedWeapon = WeaponFactory.CreateM4A1(),
            CurrentAmmo = 30
        };
        var enemy = new Operator("Enemy");
        var combat = new CombatSystem(player, enemy);
        
        var result = combat.SubmitIntent(player, new FireWeaponIntent(player.Id));
        
        Assert.True(result.success);
    }

    [Fact]
    public void BeginExecution_ChangesPhase()
    {
        var player = new Operator("Player")
        {
            EquippedWeapon = WeaponFactory.CreateM4A1(),
            CurrentAmmo = 30
        };
        var enemy = new Operator("Enemy")
        {
            EquippedWeapon = WeaponFactory.CreateAK47(),
            CurrentAmmo = 30
        };
        enemy.DistanceToOpponent = 15f;
        player.DistanceToOpponent = 15f;
        
        var combat = new CombatSystem(player, enemy);
        
        combat.SubmitIntent(player, new StopIntent(player.Id));
        combat.SubmitIntent(enemy, new StopIntent(enemy.Id));
        
        combat.BeginExecution();
        
        Assert.Equal(CombatPhase.Executing, combat.Phase);
    }

    [Fact]
    public void CombatSystem_DeterministicWithSeed()
    {
        // Run same combat twice with same seed
        var results = new List<bool>();
        
        for (int i = 0; i < 2; i++)
        {
            var player = new Operator("Player")
            {
                EquippedWeapon = WeaponFactory.CreateM4A1(),
                CurrentAmmo = 30,
                DistanceToOpponent = 15f
            };
            var enemy = new Operator("Enemy")
            {
                EquippedWeapon = WeaponFactory.CreateAK47(),
                CurrentAmmo = 30,
                DistanceToOpponent = 15f
            };
            
            var combat = new CombatSystem(player, enemy, seed: 42);
            
            combat.SubmitIntent(player, new FireWeaponIntent(player.Id));
            combat.SubmitIntent(enemy, new StopIntent(enemy.Id));
            combat.BeginExecution();
            
            // Execute one reaction window
            combat.ExecuteUntilReactionWindow();
            
            results.Add(enemy.Health < enemy.MaxHealth);
        }
        
        // Both runs should have same result (deterministic)
        Assert.Equal(results[0], results[1]);
    }
}
