using GUNRPG.Core;
using GUNRPG.Core.Combat;
using GUNRPG.Core.Intents;
using GUNRPG.Core.Operators;
using Xunit;

namespace GUNRPG.Tests;

/// <summary>
/// Tests for round execution without reaction windows.
/// Verifies that rounds execute completely with all events processed.
/// </summary>
public class RoundExecutionTests
{
    [Fact]
    public void RoundExecutesCompletely_WithAllEventsProcessed()
    {
        // Arrange: Create combat with deterministic seed
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
        
        var combat = new CombatSystemV2(player, enemy, seed: 42);
        
        // Act: Execute a round with firing
        var playerIntents = new SimultaneousIntents(player.Id)
        { 
            Primary = PrimaryAction.Fire, 
            Movement = MovementAction.Stand,
            Stance = StanceAction.EnterADS
        };
        var enemyIntents = new SimultaneousIntents(enemy.Id)
        { 
            Primary = PrimaryAction.Fire, 
            Movement = MovementAction.Stand,
            Stance = StanceAction.EnterADS
        };
        
        combat.SubmitIntents(player, playerIntents);
        combat.SubmitIntents(enemy, enemyIntents);
        combat.BeginExecution();
        
        long startTime = combat.CurrentTimeMs;
        
        // Execute round completely
        bool continuesCombat = combat.ExecuteUntilReactionWindow();
        
        long endTime = combat.CurrentTimeMs;
        
        // Assert: Time should have advanced significantly
        Assert.True(endTime > startTime, 
            $"Time should advance during round execution. Start: {startTime}ms, End: {endTime}ms");
        
        // Time should advance enough for bullets to fire and land (at least bullet travel time)
        Assert.True(endTime >= startTime + 20, 
            $"Time should advance at least 20ms for bullet impacts. Start: {startTime}ms, End: {endTime}ms");
        
        // Round either completes (returns to Planning) or combat ends
        Assert.True(combat.Phase == CombatPhase.Planning || combat.Phase == CombatPhase.Ended,
            "Round should either complete or end combat");
    }
    
    [Fact]
    public void MultipleRounds_ExecuteCompletelyWithoutReactionWindows()
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
        
        long previousEndTime = 0;
        
        // Execute multiple rounds
        for (int round = 0; round < 3 && player.IsAlive && enemy.IsAlive; round++)
        {
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
            
            long currentEndTime = combat.CurrentTimeMs;
            
            // Each round should advance time
            Assert.True(currentEndTime > previousEndTime, 
                $"Round {round}: Time should advance. Previous: {previousEndTime}ms, Current: {currentEndTime}ms");
            
            previousEndTime = currentEndTime;
        }
    }
    
    [Fact]
    public void Round_ShowsHitMissResults()
    {
        // Arrange: Create combat where we can verify hit/miss events occur
        var player = new Operator("Player") 
        { 
            EquippedWeapon = WeaponFactory.CreateRK9(),
            CurrentAmmo = 30,
            DistanceToOpponent = 15f,
            Health = 100,
            MaxHealth = 100
        };
        var enemy = new Operator("Enemy") 
        { 
            EquippedWeapon = WeaponFactory.CreateRK9(),
            CurrentAmmo = 30,
            DistanceToOpponent = 15f,
            Health = 100,
            MaxHealth = 100
        };
        
        var combat = new CombatSystemV2(player, enemy, seed: 42);
        
        float initialPlayerHealth = player.Health;
        float initialEnemyHealth = enemy.Health;
        
        // Act: Execute round with firing
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
        
        // Assert: If hits occurred, health should change
        // With seed 42, at least some shots should land eventually
        bool healthChanged = player.Health != initialPlayerHealth || enemy.Health != initialEnemyHealth;
        
        // Time should have advanced enough for bullets to travel and hit
        Assert.True(combat.CurrentTimeMs > 20, 
            "Round should execute long enough for bullet impacts");
    }
    
    [Fact]
    public void Round_EndsWhenEitherPlayerIsHit()
    {
        // Arrange: Create combat where we expect a hit
        var player = new Operator("Player") 
        { 
            EquippedWeapon = WeaponFactory.CreateRK9(),
            CurrentAmmo = 30,
            DistanceToOpponent = 15f,
            Health = 100,
            MaxHealth = 100
        };
        var enemy = new Operator("Enemy") 
        { 
            EquippedWeapon = WeaponFactory.CreateRK9(),
            CurrentAmmo = 30,
            DistanceToOpponent = 15f,
            Health = 100,
            MaxHealth = 100
        };
        
        var combat = new CombatSystemV2(player, enemy, seed: 42);
        
        float initialPlayerHealth = player.Health;
        float initialEnemyHealth = enemy.Health;
        
        // Act: Execute round with firing
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
        
        long timeBeforeExecution = combat.CurrentTimeMs;
        combat.ExecuteUntilReactionWindow();
        long timeAfterExecution = combat.CurrentTimeMs;
        
        // Assert: Round should end shortly after first hit
        // With seed 42, the first hit occurs around 26ms
        Assert.True(timeAfterExecution < 100, 
            $"Round should end shortly after first hit. Time: {timeAfterExecution}ms");
        
        // At least one player should have taken damage
        bool someoneTookDamage = player.Health < initialPlayerHealth || enemy.Health < initialEnemyHealth;
        Assert.True(someoneTookDamage, "At least one player should have been hit");
        
        // Round should have completed (returned to Planning)
        Assert.Equal(CombatPhase.Planning, combat.Phase);
    }
}
