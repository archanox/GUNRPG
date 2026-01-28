using GUNRPG.Core;
using GUNRPG.Core.Combat;
using GUNRPG.Core.Intents;
using GUNRPG.Core.Operators;
using Xunit;

namespace GUNRPG.Tests;

/// <summary>
/// Tests for the "weird second round" issue where shots fire with no visible results
/// </summary>
public class SecondRoundTests
{
    [Fact]
    public void SecondRound_ShowsResultsAfterShotsFired()
    {
        // Arrange: Create a combat with deterministic seed
        var playerWeapon = WeaponFactory.CreateRK9();
        var enemyWeapon = WeaponFactory.CreateRK9();
        
        var player = new Operator("Player") 
        { 
            EquippedWeapon = playerWeapon,
            CurrentAmmo = 30,
            DistanceToOpponent = 15f 
        };
        var enemy = new Operator("Enemy") 
        { 
            EquippedWeapon = enemyWeapon,
            CurrentAmmo = 30,
            DistanceToOpponent = 15f 
        };
        
        var combat = new CombatSystemV2(player, enemy, seed: 42);
        
        // Round 1: Both fire until reaction window
        var playerIntents1 = new SimultaneousIntents(player.Id)
        { 
            Primary = PrimaryAction.Fire, 
            Movement = MovementAction.Stand,
            Stance = StanceAction.EnterADS
        };
        var enemyIntents1 = new SimultaneousIntents(enemy.Id)
        { 
            Primary = PrimaryAction.Fire, 
            Movement = MovementAction.Stand,
            Stance = StanceAction.EnterADS
        };
        
        combat.SubmitIntents(player, playerIntents1);
        combat.SubmitIntents(enemy, enemyIntents1);
        combat.BeginExecution();
        
        // Execute round 1 until reaction window
        bool hadReaction = combat.ExecuteUntilReactionWindow();
        Assert.True(hadReaction);
        Assert.Equal(CombatPhase.Planning, combat.Phase);
        
        long timeAfterReaction1 = combat.CurrentTimeMs;
        
        // Round 2: Both fire again
        var playerIntents2 = new SimultaneousIntents(player.Id)
        { 
            Primary = PrimaryAction.Fire, 
            Movement = MovementAction.Stand
        };
        var enemyIntents2 = new SimultaneousIntents(enemy.Id)
        { 
            Primary = PrimaryAction.Fire, 
            Movement = MovementAction.Stand
        };
        
        combat.SubmitIntents(player, playerIntents2);
        combat.SubmitIntents(enemy, enemyIntents2);
        combat.BeginExecution();
        
        // Execute round 2
        bool hadReaction2 = combat.ExecuteUntilReactionWindow();
        Assert.True(hadReaction2 || combat.Phase == CombatPhase.Ended);
        
        long timeAfterRound2 = combat.CurrentTimeMs;
        
        // Assert: Round 2 should have taken some time (not instant)
        // At minimum, bullet travel time should pass, meaning time after round > time after reaction
        Assert.True(timeAfterRound2 > timeAfterReaction1, 
            $"Round 2 should take time to execute. After reaction 1: {timeAfterReaction1}ms, After round 2: {timeAfterRound2}ms");
    }
    
    [Fact]
    public void MultipleRounds_TimeAlwaysAdvances()
    {
        // Arrange
        var player = new Operator("Player") 
        { 
            EquippedWeapon = WeaponFactory.CreateRK9(),
            CurrentAmmo = 30,
            DistanceToOpponent = 15f 
        };
        var enemy = new Operator("Enemy") 
        { 
            EquippedWeapon = WeaponFactory.CreateRK9(),
            CurrentAmmo = 30,
            DistanceToOpponent = 15f 
        };
        
        var combat = new CombatSystemV2(player, enemy, seed: 123);
        
        // Execute multiple rounds and verify time always advances
        for (int round = 0; round < 5 && player.IsAlive && enemy.IsAlive; round++)
        {
            long timeBeforeRound = combat.CurrentTimeMs;
            
            var playerIntents = new SimultaneousIntents(player.Id)
            { 
                Primary = PrimaryAction.Fire, 
                Movement = MovementAction.Stand 
            };
            var enemyIntents = new SimultaneousIntents(enemy.Id)
            { 
                Primary = PrimaryAction.Fire, 
                Movement = MovementAction.Stand 
            };
            
            combat.SubmitIntents(player, playerIntents);
            combat.SubmitIntents(enemy, enemyIntents);
            combat.BeginExecution();
            
            combat.ExecuteUntilReactionWindow();
            
            long timeAfterExecution = combat.CurrentTimeMs;
            
            // Time should advance during execution (each round should process events)
            Assert.True(timeAfterExecution > timeBeforeRound, 
                $"Round {round}: Time should advance during round. Before: {timeBeforeRound}ms, After execution: {timeAfterExecution}ms");
        }
    }
}
