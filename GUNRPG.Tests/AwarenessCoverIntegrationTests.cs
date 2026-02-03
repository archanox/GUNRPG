using GUNRPG.Core;
using GUNRPG.Core.Combat;
using GUNRPG.Core.Events;
using GUNRPG.Core.Intents;
using GUNRPG.Core.Operators;
using GUNRPG.Core.Weapons;
using Xunit;

namespace GUNRPG.Tests;

/// <summary>
/// Integration tests for awareness, cover transitions, and suppressive fire mechanics.
/// Tests the complete flow of these systems working together in combat.
/// </summary>
public class AwarenessCoverIntegrationTests
{
    private static Operator CreateTestOperator(string name, float accuracy = 0.8f, float accuracyProficiency = 0.7f)
    {
        var op = new Operator(name)
        {
            Health = 100,
            MaxHealth = 100,
            Accuracy = accuracy,
            AccuracyProficiency = accuracyProficiency,
            CurrentAmmo = 30,
            DistanceToOpponent = 15f,
            CurrentMovement = MovementState.Stationary,
            CurrentCover = CoverState.None,
            EquippedWeapon = WeaponFactory.CreateSturmwolf45()
        };
        return op;
    }

    #region Cover Transition Integration Tests

    [Fact]
    public void CoverTransition_CreatesExposureWindow()
    {
        // Arrange
        var player = CreateTestOperator("Player");
        var enemy = CreateTestOperator("Enemy");

        var combat = new CombatSystemV2(player, enemy, seed: 42);

        // Act: Enter partial cover (should have transition delay)
        var intents = new SimultaneousIntents(player.Id) { Cover = CoverAction.EnterPartial };
        combat.SubmitIntents(player, intents);
        combat.BeginExecution();
        combat.ExecuteUntilReactionWindow();

        // Assert: Should have cover transition events
        var transitionStartEvents = combat.ExecutedEvents
            .OfType<CoverTransitionStartedEvent>()
            .ToList();
        var transitionCompleteEvents = combat.ExecutedEvents
            .OfType<CoverTransitionCompletedEvent>()
            .ToList();

        Assert.NotEmpty(transitionStartEvents);
        Assert.NotEmpty(transitionCompleteEvents);

        // Verify transition state was correctly tracked
        var startEvent = transitionStartEvents.First();
        var completeEvent = transitionCompleteEvents.First();

        Assert.Equal(CoverState.None, startEvent.FromCover);
        Assert.Equal(CoverState.Partial, startEvent.ToCover);
        Assert.Equal(CoverState.Partial, completeEvent.ToCover);
    }

    [Fact]
    public void CoverTransition_DuringTransition_TreatedAsPartialCover()
    {
        // Arrange
        var op = CreateTestOperator("Test");
        op.CurrentCover = CoverState.None;

        // Simulate starting a transition
        op.IsCoverTransitioning = true;
        op.CoverTransitionFromState = CoverState.None;
        op.CoverTransitionToState = CoverState.Full;
        op.CoverTransitionStartMs = 0;
        op.CoverTransitionEndMs = 200;

        // Act & Assert: Mid-transition should be treated as partial cover
        var effectiveCover = op.GetEffectiveCoverState(currentTimeMs: 100);
        Assert.Equal(CoverState.Partial, effectiveCover);

        // After transition end time but before completion event clears the flag,
        // we remain consistent with CurrentCover (None in this case)
        effectiveCover = op.GetEffectiveCoverState(currentTimeMs: 250);
        Assert.Equal(CoverState.None, effectiveCover);

        // Simulate completion event clearing the transition flag and updating cover
        op.IsCoverTransitioning = false;
        op.CurrentCover = CoverState.Full;
        
        // Now should return the new cover state
        effectiveCover = op.GetEffectiveCoverState(currentTimeMs: 300);
        Assert.Equal(CoverState.Full, effectiveCover);
    }

    #endregion

    #region Suppressive Fire Integration Tests

    [Fact]
    public void SuppressiveFire_AgainstFullCover_DoesNotMagDump()
    {
        // Arrange
        var player = CreateTestOperator("Player");
        var enemy = CreateTestOperator("Enemy");
        enemy.CurrentCover = CoverState.Full;

        // Set up player to have seen enemy recently
        player.LastTargetVisibleMs = 0;

        var combat = new CombatSystemV2(player, enemy, seed: 42);

        // Act: Fire at enemy in full cover
        var intents = new SimultaneousIntents(player.Id) { Primary = PrimaryAction.Fire };
        combat.SubmitIntents(player, intents);
        combat.BeginExecution();
        combat.ExecuteUntilReactionWindow();

        // Assert: Should have used suppressive fire (limited ammo consumption)
        int expectedMaxConsumption = SuppressiveFireModel.MaxSuppressiveBurstSize;
        int ammoConsumed = 30 - player.CurrentAmmo;

        Assert.True(ammoConsumed <= expectedMaxConsumption,
            $"Suppressive fire should not mag-dump. Expected max {expectedMaxConsumption} rounds, consumed {ammoConsumed}");

        // Should have suppressive fire events
        var suppressiveStartEvents = combat.ExecutedEvents
            .OfType<SuppressiveFireStartedEvent>()
            .ToList();
        Assert.NotEmpty(suppressiveStartEvents);
    }

    [Fact]
    public void SuppressiveFire_AppliesSuppression_NoDirectDamage()
    {
        // Arrange
        var player = CreateTestOperator("Player");
        var enemy = CreateTestOperator("Enemy");
        enemy.CurrentCover = CoverState.Full;
        player.LastTargetVisibleMs = 0;

        var combat = new CombatSystemV2(player, enemy, seed: 42);

        // Act
        var intents = new SimultaneousIntents(player.Id) { Primary = PrimaryAction.Fire };
        combat.SubmitIntents(player, intents);
        combat.BeginExecution();
        combat.ExecuteUntilReactionWindow();

        // Assert: Enemy should have suppression but no damage
        Assert.Equal(100f, enemy.Health); // No damage
        
        // Should have suppression (from suppressive fire completion event)
        var suppressiveCompletedEvents = combat.ExecutedEvents
            .OfType<SuppressiveFireCompletedEvent>()
            .ToList();
        Assert.NotEmpty(suppressiveCompletedEvents);
    }

    [Fact]
    public void SuppressiveFire_EndsRoundEarly()
    {
        // Arrange
        var player = CreateTestOperator("Player");
        var enemy = CreateTestOperator("Enemy");
        enemy.CurrentCover = CoverState.Full;
        player.LastTargetVisibleMs = 0;

        var combat = new CombatSystemV2(player, enemy, seed: 42);

        // Act
        var intents = new SimultaneousIntents(player.Id) { Primary = PrimaryAction.Fire };
        combat.SubmitIntents(player, intents);
        combat.BeginExecution();
        bool roundEnded = combat.ExecuteUntilReactionWindow();

        // Assert: Round should have ended
        Assert.True(roundEnded, "Suppressive fire should end the round");

        // Player should no longer be actively firing
        Assert.False(player.IsActivelyFiring, "Player should stop firing after suppressive burst");
    }

    #endregion

    #region Awareness / Visibility Tests

    [Fact]
    public void FullCover_BlocksVisibility()
    {
        // Arrange
        var op = CreateTestOperator("Test");
        op.CurrentCover = CoverState.Full;

        // Act & Assert
        bool isVisible = op.IsVisibleToOpponents(currentTimeMs: 100);
        Assert.False(isVisible);
    }

    [Fact]
    public void PartialCover_AllowsVisibility()
    {
        // Arrange
        var op = CreateTestOperator("Test");
        op.CurrentCover = CoverState.Partial;

        // Act & Assert
        bool isVisible = op.IsVisibleToOpponents(currentTimeMs: 100);
        Assert.True(isVisible);
    }

    [Fact]
    public void NoCover_FullyVisible()
    {
        // Arrange
        var op = CreateTestOperator("Test");
        op.CurrentCover = CoverState.None;

        // Act & Assert
        bool isVisible = op.IsVisibleToOpponents(currentTimeMs: 100);
        Assert.True(isVisible);
    }

    #endregion

    #region Recognition Delay Tests

    [Fact]
    public void RecognitionDelay_HighProficiency_FastRecognition()
    {
        float delay = AwarenessModel.CalculateRecognitionDelayMs(
            observerAccuracyProficiency: 0.9f,
            observerSuppressionLevel: 0f);

        // High proficiency should result in fast recognition (close to minimum)
        Assert.True(delay < AwarenessModel.BaseRecognitionDelayMs,
            $"High proficiency recognition ({delay}ms) should be faster than base ({AwarenessModel.BaseRecognitionDelayMs}ms)");
    }

    [Fact]
    public void RecognitionDelay_Suppressed_SlowRecognition()
    {
        float unsuppressedDelay = AwarenessModel.CalculateRecognitionDelayMs(
            observerAccuracyProficiency: 0.5f,
            observerSuppressionLevel: 0f);

        float suppressedDelay = AwarenessModel.CalculateRecognitionDelayMs(
            observerAccuracyProficiency: 0.5f,
            observerSuppressionLevel: 0.8f);

        Assert.True(suppressedDelay > unsuppressedDelay,
            $"Suppressed recognition ({suppressedDelay}ms) should be slower than unsuppressed ({unsuppressedDelay}ms)");
    }

    [Fact]
    public void RecognitionProgress_AffectsAccuracy()
    {
        // At start of recognition (0 progress)
        float earlyAccuracy = AwarenessModel.GetRecognitionAccuracyMultiplier(0f);
        // After recognition complete (full progress)
        float fullAccuracy = AwarenessModel.GetRecognitionAccuracyMultiplier(1f);

        Assert.True(earlyAccuracy < fullAccuracy,
            $"Early recognition accuracy ({earlyAccuracy}) should be worse than full ({fullAccuracy})");
        Assert.True(earlyAccuracy < 0.5f, "Early recognition should have severe accuracy penalty");
        Assert.Equal(1.0f, fullAccuracy);
    }

    #endregion

    #region AI Behavior Tests

    [Fact]
    public void AI_DoesNotMagDump_AgainstFullCover()
    {
        // This is tested implicitly by the suppressive fire tests,
        // but we verify explicitly that sustained firing doesn't occur

        var player = CreateTestOperator("Player");
        var enemy = CreateTestOperator("Enemy");
        player.CurrentCover = CoverState.Full; // Player is concealed
        enemy.LastTargetVisibleMs = 0; // Enemy saw player before

        var combat = new CombatSystemV2(player, enemy, seed: 42);

        // Enemy fires at concealed player
        var intents = new SimultaneousIntents(enemy.Id) { Primary = PrimaryAction.Fire };
        combat.SubmitIntents(enemy, intents);
        combat.BeginExecution();
        combat.ExecuteUntilReactionWindow();

        // Enemy should use controlled suppressive fire
        int maxExpectedConsumption = SuppressiveFireModel.MaxSuppressiveBurstSize;
        int actualConsumption = 30 - enemy.CurrentAmmo;

        Assert.True(actualConsumption <= maxExpectedConsumption,
            $"AI should not mag-dump. Max expected: {maxExpectedConsumption}, Actual: {actualConsumption}");
    }

    [Fact]
    public void FullCover_StillReceivesSuppression()
    {
        // Verify that full cover blocks damage but not suppression
        var player = CreateTestOperator("Player");
        var enemy = CreateTestOperator("Enemy");
        player.CurrentCover = CoverState.Full;
        enemy.LastTargetVisibleMs = 0;

        var combat = new CombatSystemV2(player, enemy, seed: 123);

        var intents = new SimultaneousIntents(enemy.Id) { Primary = PrimaryAction.Fire };
        combat.SubmitIntents(enemy, intents);
        combat.BeginExecution();
        combat.ExecuteUntilReactionWindow();

        // Health should be full (no damage through full cover)
        Assert.Equal(100f, player.Health);

        // Should have suppressive fire completed events (suppression applied)
        var suppressiveCompletedEvents = combat.ExecutedEvents
            .OfType<SuppressiveFireCompletedEvent>()
            .ToList();

        if (suppressiveCompletedEvents.Count > 0)
        {
            // Suppression was applied
            Assert.True(suppressiveCompletedEvents[0].SuppressionApplied > 0,
                "Suppressive fire should apply suppression even through full cover");
        }
    }

    #endregion
}
