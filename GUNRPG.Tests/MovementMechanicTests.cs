using GUNRPG.Core.Events;
using GUNRPG.Core.Operators;
using Xunit;

namespace GUNRPG.Tests;

public class MovementMechanicTests
{
    [Fact]
    public void StartMovement_SetsMovementState()
    {
        var op = new Operator("Test");
        long currentTime = 1000;
        long duration = 500;

        bool started = op.StartMovement(MovementState.Walking, duration, currentTime);

        Assert.True(started);
        Assert.Equal(MovementState.Walking, op.CurrentMovement);
        Assert.True(op.IsMoving);
        Assert.Equal(1500, op.MovementEndTimeMs);
    }

    [Fact]
    public void StartMovement_CancelsExistingMovement()
    {
        var op = new Operator("Test");
        var eventQueue = new EventQueue();
        long currentTime = 1000;

        // Start first movement
        op.StartMovement(MovementState.Walking, 500, currentTime, eventQueue);
        Assert.Equal(MovementState.Walking, op.CurrentMovement);

        // Start second movement (should cancel first)
        currentTime = 1200;
        op.StartMovement(MovementState.Sprinting, 300, currentTime, eventQueue);

        Assert.Equal(MovementState.Sprinting, op.CurrentMovement);
        Assert.Equal(1500, op.MovementEndTimeMs);

        // Check that cancellation event was emitted
        var events = new List<ISimulationEvent>();
        while (eventQueue.Count > 0)
        {
            events.Add(eventQueue.DequeueNext()!);
        }

        Assert.Contains(events, e => e is MovementCancelledEvent);
    }

    [Fact]
    public void CancelMovement_StopsMovement()
    {
        var op = new Operator("Test");
        long currentTime = 1000;

        op.StartMovement(MovementState.Walking, 500, currentTime);
        Assert.True(op.IsMoving);

        currentTime = 1200;
        bool cancelled = op.CancelMovement(currentTime);

        Assert.True(cancelled);
        Assert.False(op.IsMoving);
        Assert.Equal(MovementState.Stationary, op.CurrentMovement);
        Assert.Null(op.MovementEndTimeMs);
    }

    [Fact]
    public void CancelMovement_WhenNotMoving_ReturnsFalse()
    {
        var op = new Operator("Test");
        
        bool cancelled = op.CancelMovement(1000);

        Assert.False(cancelled);
    }

    [Fact]
    public void UpdateMovement_CompletesMovementAtEndTime()
    {
        var op = new Operator("Test");
        long currentTime = 1000;

        op.StartMovement(MovementState.Walking, 500, currentTime);
        Assert.True(op.IsMoving);

        // Update before end time
        op.UpdateMovement(1400);
        Assert.True(op.IsMoving);

        // Update at end time
        op.UpdateMovement(1500);
        Assert.False(op.IsMoving);
        Assert.Equal(MovementState.Stationary, op.CurrentMovement);
    }

    [Fact]
    public void EnterCover_WhenStationary_Succeeds()
    {
        var op = new Operator("Test");
        op.CurrentMovement = MovementState.Stationary;

        bool entered = op.EnterCover(CoverState.Partial, 1000);

        Assert.True(entered);
        Assert.Equal(CoverState.Partial, op.CurrentCover);
    }

    [Fact]
    public void EnterCover_WhenCrouching_Succeeds()
    {
        var op = new Operator("Test");
        op.CurrentMovement = MovementState.Crouching;

        bool entered = op.EnterCover(CoverState.Full, 1000);

        Assert.True(entered);
        Assert.Equal(CoverState.Full, op.CurrentCover);
    }

    [Fact]
    public void EnterCover_WhenMoving_Fails()
    {
        var op = new Operator("Test");
        op.StartMovement(MovementState.Walking, 500, 1000);

        bool entered = op.EnterCover(CoverState.Partial, 1000);

        Assert.False(entered);
        Assert.Equal(CoverState.None, op.CurrentCover);
    }

    [Fact]
    public void ExitCover_RemovesCover()
    {
        var op = new Operator("Test");
        op.CurrentMovement = MovementState.Stationary;
        op.EnterCover(CoverState.Partial, 1000);

        bool exited = op.ExitCover(1100);

        Assert.True(exited);
        Assert.Equal(CoverState.None, op.CurrentCover);
    }

    [Fact]
    public void MovementEvents_EmittedCorrectly()
    {
        var op = new Operator("Test");
        var eventQueue = new EventQueue();
        long currentTime = 1000;
        long duration = 500;

        op.StartMovement(MovementState.Walking, duration, currentTime, eventQueue);

        var events = new List<ISimulationEvent>();
        while (eventQueue.Count > 0)
        {
            events.Add(eventQueue.DequeueNext()!);
        }

        // Should have started and ended events
        Assert.Contains(events, e => e is MovementStartedEvent);
        Assert.Contains(events, e => e is MovementEndedEvent);

        var startedEvent = events.OfType<MovementStartedEvent>().First();
        Assert.Equal(MovementState.Walking, startedEvent.MovementType);
        Assert.Equal(1000, startedEvent.EventTimeMs);
        Assert.Equal(1500, startedEvent.EndTimeMs);

        var endedEvent = events.OfType<MovementEndedEvent>().First();
        Assert.Equal(1500, endedEvent.EventTimeMs);
    }

    [Fact]
    public void CoverEvents_EmittedCorrectly()
    {
        var op = new Operator("Test");
        var eventQueue = new EventQueue();
        op.CurrentMovement = MovementState.Stationary;

        op.EnterCover(CoverState.Partial, 1000, eventQueue);
        op.ExitCover(2000, eventQueue);

        var events = new List<ISimulationEvent>();
        while (eventQueue.Count > 0)
        {
            events.Add(eventQueue.DequeueNext()!);
        }

        Assert.Contains(events, e => e is CoverEnteredEvent);
        Assert.Contains(events, e => e is CoverExitedEvent);
    }
}
