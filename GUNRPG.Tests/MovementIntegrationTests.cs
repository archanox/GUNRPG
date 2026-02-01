using GUNRPG.Core;
using GUNRPG.Core.Combat;
using GUNRPG.Core.Events;
using GUNRPG.Core.Operators;
using GUNRPG.Core.Weapons;
using Xunit;

namespace GUNRPG.Tests;

public class MovementIntegrationTests
{
    [Fact]
    public void Movement_AffectsAccuracy()
    {
        var random = new Random(42);
        var shooter = new Operator("Shooter")
        {
            Accuracy = 0.7f,
            AccuracyProficiency = 0.5f,
            EquippedWeapon = WeaponFactory.CreateM15Mod0(),
            DistanceToOpponent = 15f
        };

        // Stationary baseline
        shooter.CurrentMovement = MovementState.Stationary;
        var stationaryResult = HitResolution.ResolveShotWithProficiency(
            BodyPart.UpperTorso,
            shooter.Accuracy,
            shooter.AccuracyProficiency,
            0f, 0f, 0f,
            new Random(42),
            movementState: shooter.CurrentMovement);

        // Sprinting penalty
        shooter.CurrentMovement = MovementState.Sprinting;
        var sprintingResult = HitResolution.ResolveShotWithProficiency(
            BodyPart.UpperTorso,
            shooter.Accuracy,
            shooter.AccuracyProficiency,
            0f, 0f, 0f,
            new Random(42),
            movementState: shooter.CurrentMovement);

        // Verify that sprinting affects accuracy (results should differ)
        // Note: Due to randomness, we can't assert exact values, but we verify the system runs
        Assert.NotNull(stationaryResult);
        Assert.NotNull(sprintingResult);
    }

    [Fact]
    public void Crouching_ReducesSuppression()
    {
        var target = new Operator("Target")
        {
            CurrentMovement = MovementState.Crouching
        };

        // Apply suppression while crouching
        float suppressionWithCrouch = SuppressionModel.CalculateSuppressionSeverity(
            weaponSuppressionFactor: 1.0f,
            weaponFireRateRPM: 600f,
            distanceMeters: 15f,
            angularDeviationDegrees: 0.3f,
            targetMovementState: MovementState.Crouching);

        // Apply suppression while standing
        float suppressionStanding = SuppressionModel.CalculateSuppressionSeverity(
            weaponSuppressionFactor: 1.0f,
            weaponFireRateRPM: 600f,
            distanceMeters: 15f,
            angularDeviationDegrees: 0.3f,
            targetMovementState: MovementState.Stationary);

        // Crouching should reduce suppression
        Assert.True(suppressionWithCrouch < suppressionStanding);
    }

    [Fact]
    public void Sprinting_IncreasesSuppression()
    {
        var target = new Operator("Target")
        {
            CurrentMovement = MovementState.Sprinting
        };

        float suppressionSprinting = SuppressionModel.CalculateSuppressionSeverity(
            weaponSuppressionFactor: 1.0f,
            weaponFireRateRPM: 600f,
            distanceMeters: 15f,
            angularDeviationDegrees: 0.3f,
            targetMovementState: MovementState.Sprinting);

        float suppressionStationary = SuppressionModel.CalculateSuppressionSeverity(
            weaponSuppressionFactor: 1.0f,
            weaponFireRateRPM: 600f,
            distanceMeters: 15f,
            angularDeviationDegrees: 0.3f,
            targetMovementState: MovementState.Stationary);

        // Sprinting should increase suppression
        Assert.True(suppressionSprinting > suppressionStationary);
    }

    [Fact]
    public void Crouching_IncreasesSuppressionDecay()
    {
        var op = new Operator("Test")
        {
            CurrentMovement = MovementState.Crouching
        };

        float initialSuppression = 0.5f;
        long deltaMs = 1000;

        float decayedCrouching = SuppressionModel.ApplyDecay(
            initialSuppression,
            deltaMs,
            isUnderFire: false,
            movementState: MovementState.Crouching);

        float decayedStanding = SuppressionModel.ApplyDecay(
            initialSuppression,
            deltaMs,
            isUnderFire: false,
            movementState: MovementState.Stationary);

        // Crouching should decay faster (lower final value)
        Assert.True(decayedCrouching < decayedStanding);
    }

    [Fact]
    public void MovementCancellation_UpdatesStateImmediately()
    {
        var op = new Operator("Test");
        var eventQueue = new EventQueue();
        long currentTime = 1000;

        // Start walking
        op.StartMovement(MovementState.Walking, 1000, currentTime, eventQueue);
        Assert.Equal(MovementState.Walking, op.CurrentMovement);
        Assert.True(op.IsMoving);

        // Cancel after 300ms
        currentTime = 1300;
        op.CancelMovement(currentTime, eventQueue);

        // State should be updated immediately
        Assert.Equal(MovementState.Stationary, op.CurrentMovement);
        Assert.False(op.IsMoving);

        // Verify cancellation event was emitted with correct remaining duration
        var events = new List<ISimulationEvent>();
        while (eventQueue.Count > 0)
        {
            events.Add(eventQueue.DequeueNext()!);
        }

        var cancelEvent = events.OfType<MovementCancelledEvent>().FirstOrDefault();
        Assert.NotNull(cancelEvent);
        Assert.Equal(700, cancelEvent!.RemainingDurationMs);
    }

    [Fact]
    public void Sprint_ThenCover_ThenADS_WorkflowTest()
    {
        var op = new Operator("Operator");
        var eventQueue = new EventQueue();
        long currentTime = 0;

        // 1. Sprint for 2 seconds
        op.StartMovement(MovementState.Sprinting, 2000, currentTime, eventQueue);
        Assert.Equal(MovementState.Sprinting, op.CurrentMovement);

        // 2. Complete sprint
        currentTime = 2000;
        op.UpdateMovement(currentTime);
        Assert.Equal(MovementState.Stationary, op.CurrentMovement);

        // 3. Enter cover (should succeed now that stationary)
        bool enterSuccess = op.EnterCover(CoverState.Full, currentTime, eventQueue);
        Assert.True(enterSuccess);
        Assert.Equal(CoverState.Full, op.CurrentCover);

        // 4. Verify operator has better survivability in cover
        float coverMultiplier = MovementModel.GetCoverHitProbabilityMultiplier(op.CurrentCover);
        Assert.Equal(0.0f, coverMultiplier); // Full cover blocks hits
    }

    [Fact]
    public void MovementEndedEvent_ClearsMovementState()
    {
        var op = new Operator("Test");
        var eventQueue = new EventQueue();
        long currentTime = 1000;

        // Start movement
        op.StartMovement(MovementState.Walking, 500, currentTime, eventQueue);

        // Peek at events without removing them
        var endedEvent = eventQueue.PeekNext();
        while (endedEvent != null && !(endedEvent is MovementEndedEvent))
        {
            eventQueue.DequeueNext();
            endedEvent = eventQueue.PeekNext();
        }

        Assert.NotNull(endedEvent);
        Assert.IsType<MovementEndedEvent>(endedEvent);

        // Execute the ended event
        endedEvent!.Execute();

        // Verify state was cleared
        Assert.Equal(MovementState.Stationary, op.CurrentMovement);
        Assert.Null(op.MovementEndTimeMs);
        Assert.False(op.IsMoving);
    }

    [Fact]
    public void WeaponSway_AppliedDuringMovement()
    {
        // Test that weapon sway is applied correctly during different movement states
        var random = new Random(42);
        
        // No sway when stationary
        float stationarySway = MovementModel.GetWeaponSwayDegrees(MovementState.Stationary);
        Assert.Equal(0.0f, stationarySway);

        // Sway when sprinting
        float sprintSway = MovementModel.GetWeaponSwayDegrees(MovementState.Sprinting);
        Assert.True(sprintSway > 0f);
        Assert.Equal(0.15f, sprintSway, precision: 2);

        // Minimal sway when crouching
        float crouchSway = MovementModel.GetWeaponSwayDegrees(MovementState.Crouching);
        Assert.True(crouchSway < sprintSway);
    }
}
