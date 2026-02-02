using GUNRPG.Core;
using GUNRPG.Core.Combat;
using GUNRPG.Core.Intents;
using GUNRPG.Core.Operators;
using Xunit;

namespace GUNRPG.Tests;

/// <summary>
/// Tests to ensure distance between operators is synchronized at round boundaries.
/// </summary>
public class DistanceSynchronizationTests
{
    [Fact]
    public void Distance_IsSynchronizedAtEndOfRound()
    {
        // Arrange - Create two operators at same distance
        var player = new Operator("Player")
        {
            EquippedWeapon = WeaponFactory.CreateM15Mod0(),
            DistanceToOpponent = 15f
        };
        player.CurrentAmmo = player.EquippedWeapon!.MagazineSize;

        var enemy = new Operator("Enemy")
        {
            EquippedWeapon = WeaponFactory.CreateM15Mod0(),
            DistanceToOpponent = 15f
        };
        enemy.CurrentAmmo = enemy.EquippedWeapon!.MagazineSize;

        var combat = new CombatSystemV2(player, enemy, seed: 42);

        // Act - Player moves forward, enemy stands still
        var playerIntents = new SimultaneousIntents(player.Id)
        {
            Movement = MovementAction.SprintToward,
            Primary = PrimaryAction.Fire
        };
        
        var enemyIntents = new SimultaneousIntents(enemy.Id)
        {
            Movement = MovementAction.Stand,
            Primary = PrimaryAction.Fire
        };

        combat.SubmitIntents(player, playerIntents);
        combat.SubmitIntents(enemy, enemyIntents);

        // Execute until round completes
        while (combat.Phase == CombatPhase.Executing)
        {
            combat.ExecuteUntilReactionWindow();
        }

        // Assert - Both operators should have the same distance after round completes
        Assert.Equal(player.DistanceToOpponent, enemy.DistanceToOpponent);
    }

    [Fact]
    public void Distance_IsSynchronizedWhenCombatEnds()
    {
        // Arrange - Create operators where one will die
        var player = new Operator("Player")
        {
            EquippedWeapon = WeaponFactory.CreateSokol545(),
            DistanceToOpponent = 10f,
            Health = 100f
        };
        player.CurrentAmmo = player.EquippedWeapon!.MagazineSize;

        var enemy = new Operator("Enemy")
        {
            EquippedWeapon = WeaponFactory.CreateSokol545(),
            DistanceToOpponent = 10f,
            Health = 1f // Very low health, likely to die
        };
        enemy.CurrentAmmo = enemy.EquippedWeapon!.MagazineSize;

        var combat = new CombatSystemV2(player, enemy, seed: 42);

        // Act - Both fire
        var playerIntents = new SimultaneousIntents(player.Id)
        {
            Primary = PrimaryAction.Fire
        };
        
        var enemyIntents = new SimultaneousIntents(enemy.Id)
        {
            Primary = PrimaryAction.Fire
        };

        combat.SubmitIntents(player, playerIntents);
        combat.SubmitIntents(enemy, enemyIntents);
        
        // Execute until round completes
        while (combat.Phase == CombatPhase.Executing)
        {
            combat.ExecuteUntilReactionWindow();
        }

        // Assert - Even if combat ended, distances should be synchronized
        Assert.Equal(player.DistanceToOpponent, enemy.DistanceToOpponent);
    }

    [Fact]
    public void Distance_RemainsConsistentAcrossMultipleRounds()
    {
        // Arrange
        var player = new Operator("Player")
        {
            EquippedWeapon = WeaponFactory.CreateM15Mod0(),
            DistanceToOpponent = 20f
        };
        player.CurrentAmmo = player.EquippedWeapon!.MagazineSize;

        var enemy = new Operator("Enemy")
        {
            EquippedWeapon = WeaponFactory.CreateM15Mod0(),
            DistanceToOpponent = 20f
        };
        enemy.CurrentAmmo = enemy.EquippedWeapon!.MagazineSize;

        var combat = new CombatSystemV2(player, enemy, seed: 42);

        // Act - Execute multiple rounds with various movement
        for (int i = 0; i < 5; i++)
        {
            var playerIntents = new SimultaneousIntents(player.Id)
            {
                Movement = i % 2 == 0 ? MovementAction.WalkToward : MovementAction.WalkAway,
                Primary = PrimaryAction.Fire
            };
            
            var enemyIntents = new SimultaneousIntents(enemy.Id)
            {
                Movement = i % 2 == 0 ? MovementAction.WalkAway : MovementAction.Stand,
                Primary = PrimaryAction.Fire
            };

            combat.SubmitIntents(player, playerIntents);
            combat.SubmitIntents(enemy, enemyIntents);
            
            // Execute until round completes
            while (combat.Phase == CombatPhase.Executing)
            {
                combat.ExecuteUntilReactionWindow();
            }

            // Assert - After each round, distances should match
            Assert.Equal(player.DistanceToOpponent, enemy.DistanceToOpponent);
            
            // Break if combat ended
            if (combat.Phase == CombatPhase.Ended)
                break;
        }
    }
}
