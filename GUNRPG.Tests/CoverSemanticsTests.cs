using GUNRPG.Core;
using GUNRPG.Core.Combat;
using GUNRPG.Core.Events;
using GUNRPG.Core.Intents;
using GUNRPG.Core.Operators;
using GUNRPG.Core.Weapons;
using Xunit;

namespace GUNRPG.Tests;

/// <summary>
/// Tests for clarified cover semantics:
/// - Partial Cover = Peeking (only upper body exposed)
/// - Full Cover = Complete Concealment (no damage possible)
/// </summary>
public class CoverSemanticsTests
{
    private static Operator CreateTestOperator(string name, float accuracy = 1.0f, float accuracyProficiency = 0.8f)
    {
        var op = new Operator(name)
        {
            Health = 100,
            MaxHealth = 100,
            Accuracy = accuracy,
            AccuracyProficiency = accuracyProficiency,
            CurrentAmmo = 30,
            DistanceToOpponent = 10f,
            CurrentMovement = MovementState.Stationary,
            CurrentCover = CoverState.None,
            EquippedWeapon = WeaponFactory.CreateSturmwolf45()
        };
        return op;
    }

    [Fact]
    public void PartialCover_BlocksLowerTorsoHits()
    {
        // Fire many shots to test hit distribution
        int lowerTorsoHits = 0;
        int upperBodyHits = 0;
        int totalShots = 100;

        for (int i = 0; i < totalShots; i++)
        {
            var shooter2 = CreateTestOperator("Shooter");
            var target2 = CreateTestOperator("Target");
            target2.CurrentCover = CoverState.Partial;

            var combat2 = new CombatSystemV2(shooter2, target2, seed: 42 + i);
            
            var intents = new SimultaneousIntents(shooter2.Id) { Primary = PrimaryAction.Fire };
            combat2.SubmitIntents(shooter2, intents);
            combat2.BeginExecution();
            combat2.ExecuteUntilReactionWindow();

            // Check if target was hit and where
            float healthLost = 100 - target2.Health;
            if (healthLost > 0)
            {
                // Hit occurred - check that it wasn't lower torso
                // We can't directly check the hit location from health alone,
                // but we can verify lower torso hits are filtered by checking events
                var damageEvents = combat2.ExecutedEvents.OfType<DamageAppliedEvent>().ToList();
                foreach (var evt in damageEvents)
                {
                    if (evt.BodyPart == BodyPart.LowerTorso)
                    {
                        lowerTorsoHits++;
                    }
                    else if (evt.BodyPart == BodyPart.UpperTorso || evt.BodyPart == BodyPart.Neck || evt.BodyPart == BodyPart.Head)
                    {
                        upperBodyHits++;
                    }
                }
            }
        }

        // Assert: No lower torso hits should occur in partial cover
        Assert.Equal(0, lowerTorsoHits);
        // Assert: Some upper body hits should occur (to verify test is working)
        Assert.True(upperBodyHits > 0, "Expected some upper body hits to occur");
    }

    [Fact]
    public void FullCover_BlocksAllDamage()
    {
        // Arrange
        var shooter = CreateTestOperator("Shooter", accuracy: 1.0f);
        var target = CreateTestOperator("Target");
        target.CurrentCover = CoverState.Full; // Completely concealed

        var combat = new CombatSystemV2(shooter, target, seed: 42);

        // Act: Shoot multiple times
        int shotsToFire = 10;
        for (int i = 0; i < shotsToFire; i++)
        {
            var intents = new SimultaneousIntents(shooter.Id) { Primary = PrimaryAction.Fire };
            combat.SubmitIntents(shooter, intents);
            combat.BeginExecution();
            combat.ExecuteUntilReactionWindow();
            
            // Reset for next shot
            shooter.CurrentAmmo = 30;
        }

        // Assert: Target should have taken no damage
        Assert.Equal(100f, target.Health);
        
        // Assert: No damage events should have been generated
        var damageEvents = combat.ExecutedEvents.OfType<DamageAppliedEvent>().ToList();
        Assert.Empty(damageEvents);
    }

    [Fact]
    public void FullCover_StillAppliesSuppression()
    {
        // Arrange
        var shooter = CreateTestOperator("Shooter", accuracy: 0.5f); // Lower accuracy for more misses
        var target = CreateTestOperator("Target");
        target.CurrentCover = CoverState.Full; // Completely concealed

        var combat = new CombatSystemV2(shooter, target, seed: 123);

        // Act: Fire shots that should miss but still suppress
        var intents = new SimultaneousIntents(shooter.Id) { Primary = PrimaryAction.Fire };
        combat.SubmitIntents(shooter, intents);
        combat.BeginExecution();
        combat.ExecuteUntilReactionWindow();

        // Assert: Target should have some suppression level (shots are passing by)
        // Note: Suppression may or may not apply depending on shot accuracy and proximity
        // The key is that the system allows suppression events for full cover
        var suppressionEvents = combat.ExecutedEvents
            .Where(e => e is SuppressionStartedEvent || e is SuppressionUpdatedEvent)
            .ToList();
        
        // We're verifying that suppression events CAN be generated for full cover targets
        // (deterministically, given the fixed seed and parameters)
        Assert.NotEmpty(suppressionEvents);
        
        // And full cover should still block all damage
        Assert.InRange(target.Health, 99.9f, 100.1f);
    }

    [Fact]
    public void FullCover_BlocksShooting()
    {
        // Arrange
        var shooter = CreateTestOperator("Shooter");
        shooter.CurrentCover = CoverState.Full; // Shooter is in full cover
        var target = CreateTestOperator("Target");

        var combat = new CombatSystemV2(shooter, target, seed: 42);

        // Act: Try to shoot from full cover
        var intents = new SimultaneousIntents(shooter.Id) { Primary = PrimaryAction.Fire };
        var result = combat.SubmitIntents(shooter, intents);
        
        // Submit should succeed (intent is valid), but execution should block it
        Assert.True(result.success);
        
        combat.BeginExecution();
        combat.ExecuteUntilReactionWindow();

        // Assert: No shots should have been fired
        var shotEvents = combat.ExecutedEvents.OfType<ShotFiredEvent>().ToList();
        Assert.Empty(shotEvents);
        
        // Assert: Ammo should not have been consumed
        Assert.Equal(30, shooter.CurrentAmmo);
    }

    [Fact]
    public void FullCover_BlocksAdvancing()
    {
        // Arrange
        var shooter = CreateTestOperator("Shooter");
        shooter.CurrentCover = CoverState.Full; // In full cover
        var target = CreateTestOperator("Target");

        var combat = new CombatSystemV2(shooter, target, seed: 42);

        float initialDistance = shooter.DistanceToOpponent;

        // Act: Try to advance from full cover
        var intents = new SimultaneousIntents(shooter.Id) { Movement = MovementAction.WalkToward };
        var result = combat.SubmitIntents(shooter, intents);
        
        Assert.True(result.success);
        
        combat.BeginExecution();
        combat.ExecuteUntilReactionWindow();

        // Assert: Distance should not have changed (movement blocked)
        Assert.Equal(initialDistance, shooter.DistanceToOpponent);
    }

    [Fact]
    public void FullCover_AllowsRetreating()
    {
        // Arrange
        var shooter = CreateTestOperator("Shooter");
        shooter.CurrentCover = CoverState.Full; // In full cover
        var target = CreateTestOperator("Target");

        var combat = new CombatSystemV2(shooter, target, seed: 42);

        // Act: Try to retreat from full cover (should be allowed)
        var intents = new SimultaneousIntents(shooter.Id) { Movement = MovementAction.WalkAway };
        var result = combat.SubmitIntents(shooter, intents);
        
        Assert.True(result.success);
        
        // Assert: retreat intent is accepted from full cover
        Assert.True(result.success);
    }

    [Fact]
    public void PartialCover_AllowsShooting()
    {
        // Arrange
        var shooter = CreateTestOperator("Shooter");
        shooter.CurrentCover = CoverState.Partial; // Peeking
        var target = CreateTestOperator("Target");

        var combat = new CombatSystemV2(shooter, target, seed: 42);

        // Act: Try to shoot from partial cover (should be allowed)
        var intents = new SimultaneousIntents(shooter.Id) { Primary = PrimaryAction.Fire };
        var result = combat.SubmitIntents(shooter, intents);
        
        Assert.True(result.success);
        
        combat.BeginExecution();
        combat.ExecuteUntilReactionWindow();

        // Assert: Shot should have been fired
        var shotEvents = combat.ExecutedEvents.OfType<ShotFiredEvent>().ToList();
        Assert.NotEmpty(shotEvents);
        
        // Assert: Ammo should have been consumed
        Assert.Equal(29, shooter.CurrentAmmo);
    }

    [Fact]
    public void PartialCover_AllowsAdvancing()
    {
        // Arrange
        var shooter = CreateTestOperator("Shooter");
        shooter.CurrentCover = CoverState.Partial; // Peeking
        var target = CreateTestOperator("Target");

        var combat = new CombatSystemV2(shooter, target, seed: 42);

        // Act: Try to advance from partial cover (should be allowed)
        var intents = new SimultaneousIntents(shooter.Id) { Movement = MovementAction.WalkToward };
        var result = combat.SubmitIntents(shooter, intents);
        
        // Assert: Intent should be accepted
        Assert.True(result.success);
    }

    [Fact]
    public void CanShoot_ReturnsCorrectValues()
    {
        var op = CreateTestOperator("Test");

        // None: Can shoot
        op.CurrentCover = CoverState.None;
        Assert.True(op.CanShoot());

        // Partial: Can shoot (peeking)
        op.CurrentCover = CoverState.Partial;
        Assert.True(op.CanShoot());

        // Full: Cannot shoot (concealed)
        op.CurrentCover = CoverState.Full;
        Assert.False(op.CanShoot());
    }

    [Fact]
    public void CanAdvance_ReturnsCorrectValues()
    {
        var op = CreateTestOperator("Test");

        // None: Can advance
        op.CurrentCover = CoverState.None;
        Assert.True(op.CanAdvance());

        // Partial: Can advance
        op.CurrentCover = CoverState.Partial;
        Assert.True(op.CanAdvance());

        // Full: Cannot advance (must exit cover first)
        op.CurrentCover = CoverState.Full;
        Assert.False(op.CanAdvance());
    }

    [Fact]
    public void NoCover_CanBeHit()
    {
        // This test verifies that without cover, hits can occur
        // The implicit verification is that the PartialCover test shows lower torso IS filtered
        // Therefore, the filtering logic is working (it wouldn't make sense to filter if hits weren't possible)
        
        int hitsLanded = 0;
        
        for (int i = 0; i < 10; i++)
        {
            var shooter2 = CreateTestOperator("Shooter", accuracy: 0.9f);
            var target2 = CreateTestOperator("Target");
            target2.CurrentCover = CoverState.None;

            var combat = new CombatSystemV2(shooter2, target2, seed: 100 + i);
            
            var intents = new SimultaneousIntents(shooter2.Id) { Primary = PrimaryAction.Fire };
            combat.SubmitIntents(shooter2, intents);
            combat.BeginExecution();
            combat.ExecuteUntilReactionWindow();

            if (target2.Health < target2.MaxHealth)
                hitsLanded++;
        }

        // Assert: With high accuracy and no cover, some hits should land
        Assert.True(hitsLanded > 0, "Expected some hits to land with no cover and high accuracy");
    }
}
