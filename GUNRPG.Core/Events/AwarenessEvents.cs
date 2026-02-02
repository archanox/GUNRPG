using GUNRPG.Core.Combat;
using GUNRPG.Core.Operators;

namespace GUNRPG.Core.Events;

/// <summary>
/// Event emitted when a cover transition starts.
/// During the transition, the operator is treated as being in partial cover.
/// </summary>
public sealed class CoverTransitionStartedEvent : ISimulationEvent
{
    public const int MinEventDurationMs = 2;
    public long EventTimeMs { get; }
    public Guid OperatorId { get; }
    public int SequenceNumber { get; }

    /// <summary>
    /// The operator transitioning cover.
    /// </summary>
    public Operator Operator { get; }

    /// <summary>
    /// The cover state being transitioned from.
    /// </summary>
    public CoverState FromCover { get; }

    /// <summary>
    /// The cover state being transitioned to.
    /// </summary>
    public CoverState ToCover { get; }

    /// <summary>
    /// When the transition will complete.
    /// </summary>
    public long CompletionTimeMs { get; }

    /// <summary>
    /// Duration of the transition in milliseconds.
    /// </summary>
    public long DurationMs => CompletionTimeMs - EventTimeMs;

    public CoverTransitionStartedEvent(
        long eventTimeMs,
        Operator op,
        CoverState fromCover,
        CoverState toCover,
        long completionTimeMs,
        int sequenceNumber)
    {
        EventTimeMs = eventTimeMs;
        OperatorId = op.Id;
        Operator = op;
        FromCover = fromCover;
        ToCover = toCover;
        CompletionTimeMs = completionTimeMs;
        SequenceNumber = sequenceNumber;
    }

    public bool Execute()
    {
        // Mark operator as transitioning - during transition they are treated as partial cover
        Operator.IsCoverTransitioning = true;
        Operator.CoverTransitionFromState = FromCover;
        Operator.CoverTransitionToState = ToCover;
        Operator.CoverTransitionStartMs = EventTimeMs;
        Operator.CoverTransitionEndMs = CompletionTimeMs;

        Console.WriteLine($"[{EventTimeMs}ms] {Operator.Name} started transitioning from {FromCover} cover to {ToCover} cover (will complete at {CompletionTimeMs}ms)");
        return false; // Does not trigger reaction window
    }
}

/// <summary>
/// Event emitted when a cover transition completes.
/// </summary>
public sealed class CoverTransitionCompletedEvent : ISimulationEvent
{
    public const int MinEventDurationMs = 2;
    public long EventTimeMs { get; }
    public Guid OperatorId { get; }
    public int SequenceNumber { get; }

    /// <summary>
    /// The operator who completed the transition.
    /// </summary>
    public Operator Operator { get; }

    /// <summary>
    /// The cover state transitioned from.
    /// </summary>
    public CoverState FromCover { get; }

    /// <summary>
    /// The final cover state.
    /// </summary>
    public CoverState ToCover { get; }

    public CoverTransitionCompletedEvent(
        long eventTimeMs,
        Operator op,
        CoverState fromCover,
        CoverState toCover,
        int sequenceNumber)
    {
        EventTimeMs = eventTimeMs;
        OperatorId = op.Id;
        Operator = op;
        FromCover = fromCover;
        ToCover = toCover;
        SequenceNumber = sequenceNumber;
    }

    public bool Execute()
    {
        // Complete the transition
        Operator.IsCoverTransitioning = false;
        Operator.CurrentCover = ToCover;
        Operator.CoverTransitionStartMs = null;
        Operator.CoverTransitionEndMs = null;

        Console.WriteLine($"[{EventTimeMs}ms] {Operator.Name} completed cover transition to {ToCover}");
        return false; // Does not trigger reaction window
    }
}

/// <summary>
/// Event emitted when suppressive fire is started against a concealed target.
/// </summary>
public sealed class SuppressiveFireStartedEvent : ISimulationEvent
{
    public const int MinEventDurationMs = 2;
    public long EventTimeMs { get; }
    public Guid OperatorId { get; }
    public int SequenceNumber { get; }

    /// <summary>
    /// The operator firing suppressive fire.
    /// </summary>
    public Operator Shooter { get; }

    /// <summary>
    /// The target of suppressive fire.
    /// </summary>
    public Operator Target { get; }

    /// <summary>
    /// Number of rounds in the suppressive burst.
    /// </summary>
    public int BurstSize { get; }

    /// <summary>
    /// Name of the weapon being used.
    /// </summary>
    public string WeaponName { get; }

    public SuppressiveFireStartedEvent(
        long eventTimeMs,
        Operator shooter,
        Operator target,
        int burstSize,
        string weaponName,
        int sequenceNumber)
    {
        EventTimeMs = eventTimeMs;
        OperatorId = shooter.Id;
        Shooter = shooter;
        Target = target;
        BurstSize = burstSize;
        WeaponName = weaponName;
        SequenceNumber = sequenceNumber;
    }

    public bool Execute()
    {
        Console.WriteLine($"[{EventTimeMs}ms] {Shooter.Name} started suppressive fire on {Target.Name}'s position with {WeaponName} ({BurstSize} round burst)");
        return false;
    }
}

/// <summary>
/// Event emitted when suppressive fire burst completes and suppression is applied.
/// </summary>
public sealed class SuppressiveFireCompletedEvent : ISimulationEvent
{
    public const int MinEventDurationMs = 2;
    public long EventTimeMs { get; }
    public Guid OperatorId { get; }
    public int SequenceNumber { get; }

    /// <summary>
    /// The operator who fired.
    /// </summary>
    public Operator Shooter { get; }

    /// <summary>
    /// The target of suppressive fire.
    /// </summary>
    public Operator Target { get; }

    /// <summary>
    /// Rounds consumed in the burst.
    /// </summary>
    public int RoundsConsumed { get; }

    /// <summary>
    /// Suppression severity applied.
    /// </summary>
    public float SuppressionApplied { get; }

    /// <summary>
    /// Name of the weapon used.
    /// </summary>
    public string WeaponName { get; }

    private readonly EventQueue? _eventQueue;

    public SuppressiveFireCompletedEvent(
        long eventTimeMs,
        Operator shooter,
        Operator target,
        int roundsConsumed,
        float suppressionApplied,
        string weaponName,
        int sequenceNumber,
        EventQueue? eventQueue = null)
    {
        EventTimeMs = eventTimeMs;
        OperatorId = shooter.Id;
        Shooter = shooter;
        Target = target;
        RoundsConsumed = roundsConsumed;
        SuppressionApplied = suppressionApplied;
        WeaponName = weaponName;
        SequenceNumber = sequenceNumber;
        _eventQueue = eventQueue;
    }

    public bool Execute()
    {
        // Apply suppression to target
        float previousLevel = Target.SuppressionLevel;
        bool becameSuppressed = Target.ApplySuppression(SuppressionApplied, EventTimeMs);

        Console.WriteLine($"[{EventTimeMs}ms] {Shooter.Name}'s suppressive fire completed. {RoundsConsumed} rounds fired, {SuppressionApplied:F2} suppression applied to {Target.Name}");

        // Emit suppression events if applicable
        if (_eventQueue != null && SuppressionApplied > 0f)
        {
            if (becameSuppressed)
            {
                _eventQueue.Schedule(new SuppressionStartedEvent(
                    EventTimeMs,
                    Target,
                    Shooter,
                    Target.SuppressionLevel,
                    _eventQueue.GetNextSequenceNumber(),
                    WeaponName));
            }
            else if (previousLevel > 0f)
            {
                _eventQueue.Schedule(new SuppressionUpdatedEvent(
                    EventTimeMs,
                    Target,
                    Shooter,
                    previousLevel,
                    Target.SuppressionLevel,
                    _eventQueue.GetNextSequenceNumber(),
                    WeaponName));
            }
        }

        // Stop firing - suppressive fire ends the round early
        Shooter.IsActivelyFiring = false;

        return false; // Does not trigger reaction window
    }
}

/// <summary>
/// Event emitted when an operator's recognition of a target completes.
/// This happens after a target exits full cover and the observer recognizes them.
/// </summary>
public sealed class TargetRecognizedEvent : ISimulationEvent
{
    public const int MinEventDurationMs = 2;
    public long EventTimeMs { get; }
    public Guid OperatorId { get; }
    public int SequenceNumber { get; }

    /// <summary>
    /// The observer who recognized the target.
    /// </summary>
    public Operator Observer { get; }

    /// <summary>
    /// The target who was recognized.
    /// </summary>
    public Operator Target { get; }

    /// <summary>
    /// Recognition delay that was applied.
    /// </summary>
    public float RecognitionDelayMs { get; }

    public TargetRecognizedEvent(
        long eventTimeMs,
        Operator observer,
        Operator target,
        float recognitionDelayMs,
        int sequenceNumber)
    {
        EventTimeMs = eventTimeMs;
        OperatorId = observer.Id;
        Observer = observer;
        Target = target;
        RecognitionDelayMs = recognitionDelayMs;
        SequenceNumber = sequenceNumber;
    }

    public bool Execute()
    {
        // Clear recognition delay tracking
        Observer.RecognitionDelayEndMs = null;
        Observer.RecognitionTargetId = null;

        Console.WriteLine($"[{EventTimeMs}ms] {Observer.Name} recognized {Target.Name} (delay: {RecognitionDelayMs:F0}ms)");
        return false;
    }
}
