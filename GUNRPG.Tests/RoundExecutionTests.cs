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
            EquippedWeapon = WeaponFactory.CreateSturmwolf45(),
            CurrentAmmo = 30,
            DistanceToOpponent = 15f,
            AccuracyProficiency = 0.5f  // Explicit proficiency
        };
        var enemy = new Operator("Enemy") 
        { 
            EquippedWeapon = WeaponFactory.CreateSturmwolf45(),
            CurrentAmmo = 30,
            DistanceToOpponent = 15f,
            AccuracyProficiency = 0.5f  // Explicit proficiency
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
        _ = combat.ExecuteUntilReactionWindow();
        
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
            EquippedWeapon = WeaponFactory.CreateSturmwolf45(),
            CurrentAmmo = 30,
            DistanceToOpponent = 15f,
            Accuracy = 0.9f,  // High accuracy to ensure hits
            AccuracyProficiency = 0.5f  // Explicit proficiency
        };
        var enemy = new Operator("Enemy") 
        { 
            EquippedWeapon = WeaponFactory.CreateSturmwolf45(),
            CurrentAmmo = 30,
            DistanceToOpponent = 15f,
            Accuracy = 0.9f,  // High accuracy to ensure hits
            AccuracyProficiency = 0.5f  // Explicit proficiency
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
            
            // Each round should advance time OR both players should be alive
            // (if both miss at the same timestamp, round ends without time advancing)
            Assert.True(currentEndTime >= previousEndTime, 
                $"Round {round}: Time should not go backwards. Previous: {previousEndTime}ms, Current: {currentEndTime}ms");
            
            previousEndTime = currentEndTime;
        }
    }
    
    [Fact]
    public void Round_ShowsHitMissResults()
    {
        // Arrange: Create combat where we can verify hit/miss events occur
        var player = new Operator("Player") 
        { 
            EquippedWeapon = WeaponFactory.CreateSturmwolf45(),
            CurrentAmmo = 30,
            DistanceToOpponent = 15f,
            Health = 100,
            MaxHealth = 100,
            AccuracyProficiency = 0.5f  // Explicit proficiency
        };
        var enemy = new Operator("Enemy") 
        { 
            EquippedWeapon = WeaponFactory.CreateSturmwolf45(),
            CurrentAmmo = 30,
            DistanceToOpponent = 15f,
            Health = 100,
            MaxHealth = 100,
            AccuracyProficiency = 0.5f  // Explicit proficiency
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
        
        // Assert: Health should change when hits occur
        // With seed 42, at least some shots should land
        const float healthEpsilon = 0.001f;
        bool healthChanged = Math.Abs(player.Health - initialPlayerHealth) > healthEpsilon
            || Math.Abs(enemy.Health - initialEnemyHealth) > healthEpsilon;
        
        // Time should have advanced enough for bullets to travel and hit
        Assert.True(combat.CurrentTimeMs > 20, 
            "Round should execute long enough for bullet impacts");
        
        // At least one player should have taken damage (validates hit/miss logic)
        Assert.True(healthChanged, 
            "At least one player should have been hit with seed 42");
    }
    
    [Fact]
    public void Round_EndsWhenEitherPlayerIsHit()
    {
        // Arrange: Create combat where we expect a hit
        var player = new Operator("Player") 
        { 
            EquippedWeapon = WeaponFactory.CreateSturmwolf45(),
            CurrentAmmo = 30,
            DistanceToOpponent = 15f,
            Health = 100,
            MaxHealth = 100,
            AccuracyProficiency = 0.5f  // Explicit proficiency
        };
        var enemy = new Operator("Enemy") 
        { 
            EquippedWeapon = WeaponFactory.CreateSturmwolf45(),
            CurrentAmmo = 30,
            DistanceToOpponent = 15f,
            Health = 100,
            MaxHealth = 100,
            AccuracyProficiency = 0.5f  // Explicit proficiency
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
        
        // Assert: Time should not go backwards and should advance during execution
        Assert.True(timeAfterExecution >= timeBeforeExecution,
            $"Combat time should advance during execution. Before: {timeBeforeExecution}ms, After: {timeAfterExecution}ms");
        
        // Round should end shortly after first hit
        // With seed 42, the first hit occurs around 26ms
        Assert.True(timeAfterExecution < 100, 
            $"Round should end shortly after first hit. Time: {timeAfterExecution}ms");
        
        // At least one player should have taken damage
        const float healthEpsilon = 0.001f;
        bool someoneTookDamage = (initialPlayerHealth - player.Health) > healthEpsilon 
            || (initialEnemyHealth - enemy.Health) > healthEpsilon;
        Assert.True(someoneTookDamage, "At least one player should have been hit");
        
        // Round should have completed (returned to Planning)
        Assert.Equal(CombatPhase.Planning, combat.Phase);
    }

    [Fact]
    public void Round_ResolvesPendingEnemyHit_AfterEnemyDies()
    {
        var player = new Operator("Player", Guid.Parse("00000000-0000-0000-0000-000000000001"))
        {
            EquippedWeapon = WeaponFactory.CreateSturmwolf45(),
            CurrentAmmo = 30,
            DistanceToOpponent = 15f,
            Health = 10f,
            MaxHealth = 100f,
            Accuracy = 1f,
            AccuracyProficiency = 1f
        };
        player.EquippedWeapon!.VerticalRecoil = 0f;

        var enemy = new Operator("Enemy", Guid.Parse("00000000-0000-0000-0000-000000000002"))
        {
            EquippedWeapon = WeaponFactory.CreateSturmwolf45(),
            CurrentAmmo = 30,
            DistanceToOpponent = 15f,
            Health = 10f,
            MaxHealth = 100f,
            Accuracy = 1f,
            AccuracyProficiency = 1f
        };
        enemy.EquippedWeapon!.VerticalRecoil = 0f;

        var combat = new CombatSystemV2(player, enemy, seed: 42);

        combat.SubmitIntents(player, new SimultaneousIntents(player.Id)
        {
            Primary = PrimaryAction.Fire,
            Movement = MovementAction.Stand
        });
        combat.SubmitIntents(enemy, new SimultaneousIntents(enemy.Id)
        {
            Primary = PrimaryAction.Fire,
            Movement = MovementAction.Stand
        });

        combat.BeginExecution();
        combat.ExecuteUntilReactionWindow();

        Assert.False(enemy.IsAlive);
        Assert.False(player.IsAlive);
        Assert.Equal(CombatPhase.Ended, combat.Phase);
    }
}
