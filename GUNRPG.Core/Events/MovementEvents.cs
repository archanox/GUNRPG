using GUNRPG.Core.Operators;

namespace GUNRPG.Core.Events;

/// <summary>
/// Event emitted when an operator starts moving.
/// </summary>
public sealed class MovementStartedEvent : ISimulationEvent
{
    /// <summary>
    /// Minimum event duration for timeline rendering (milliseconds).
    /// </summary>
    public const int MinEventDurationMs = 2;
    public long EventTimeMs { get; }
    public Guid OperatorId { get; }
    public int SequenceNumber { get; }

    /// <summary>
    /// The operator who started moving.
    /// </summary>
    public Operator Operator { get; }

    /// <summary>
    /// The movement type (Walking, Sprinting, Crouching).
    /// </summary>
    public MovementState MovementType { get; }

    /// <summary>
    /// When the movement will end.
    /// </summary>
    public long EndTimeMs { get; }

    /// <summary>
    /// Duration of the movement in milliseconds.
    /// </summary>
    public long DurationMs => EndTimeMs - EventTimeMs;

    public MovementStartedEvent(
        long eventTimeMs,
        Operator op,
        MovementState movementType,
        long endTimeMs,
        int sequenceNumber)
    {
        EventTimeMs = eventTimeMs;
        OperatorId = op.Id;
        Operator = op;
        MovementType = movementType;
        EndTimeMs = endTimeMs;
        SequenceNumber = sequenceNumber;
    }

    public bool Execute()
    {
        Console.WriteLine($"[{EventTimeMs}ms] {Operator.Name} started {MovementType} (ends at {EndTimeMs}ms)");
        return false; // Does not trigger reaction window
    }
}

/// <summary>
/// Event emitted when movement is cancelled before completion.
/// </summary>
public sealed class MovementCancelledEvent : ISimulationEvent
{
    /// <summary>
    /// Minimum event duration for timeline rendering (milliseconds).
    /// </summary>
    public const int MinEventDurationMs = 2;
    public long EventTimeMs { get; }
    public Guid OperatorId { get; }
    public int SequenceNumber { get; }

    /// <summary>
    /// The operator whose movement was cancelled.
    /// </summary>
    public Operator Operator { get; }

    /// <summary>
    /// The movement type that was cancelled.
    /// </summary>
    public MovementState CancelledMovementType { get; }

    /// <summary>
    /// How much time was remaining when cancelled.
    /// </summary>
    public long RemainingDurationMs { get; }

    public MovementCancelledEvent(
        long eventTimeMs,
        Operator op,
        MovementState cancelledMovementType,
        long remainingDurationMs,
        int sequenceNumber)
    {
        EventTimeMs = eventTimeMs;
        OperatorId = op.Id;
        Operator = op;
        CancelledMovementType = cancelledMovementType;
        RemainingDurationMs = remainingDurationMs;
        SequenceNumber = sequenceNumber;
    }

    public bool Execute()
    {
        Console.WriteLine($"[{EventTimeMs}ms] {Operator.Name} cancelled {CancelledMovementType} ({RemainingDurationMs}ms remaining)");
        return false; // Does not trigger reaction window
    }
}

/// <summary>
/// Event emitted when movement completes naturally.
/// </summary>
public sealed class MovementEndedEvent : ISimulationEvent
{
    /// <summary>
    /// Minimum event duration for timeline rendering (milliseconds).
    /// </summary>
    public const int MinEventDurationMs = 2;
    public long EventTimeMs { get; }
    public Guid OperatorId { get; }
    public int SequenceNumber { get; }

    /// <summary>
    /// The operator whose movement ended.
    /// </summary>
    public Operator Operator { get; }

    /// <summary>
    /// The movement type that ended.
    /// </summary>
    public MovementState EndedMovementType { get; }

    public MovementEndedEvent(
        long eventTimeMs,
        Operator op,
        MovementState endedMovementType,
        int sequenceNumber)
    {
        EventTimeMs = eventTimeMs;
        OperatorId = op.Id;
        Operator = op;
        EndedMovementType = endedMovementType;
        SequenceNumber = sequenceNumber;
    }

    public bool Execute()
    {
        Operator.MovementEndTimeMs = null;
        Operator.CurrentMovement = MovementState.Stationary;
        Console.WriteLine($"[{EventTimeMs}ms] {Operator.Name} finished {EndedMovementType}");
        return false; // Does not trigger reaction window
    }
}

/// <summary>
/// Event emitted when an operator enters cover.
/// </summary>
public sealed class CoverEnteredEvent : ISimulationEvent
{
    /// <summary>
    /// Minimum event duration for timeline rendering (milliseconds).
    /// </summary>
    public const int MinEventDurationMs = 2;
    public long EventTimeMs { get; }
    public Guid OperatorId { get; }
    public int SequenceNumber { get; }

    /// <summary>
    /// The operator who entered cover.
    /// </summary>
    public Operator Operator { get; }

    /// <summary>
    /// The type of cover entered.
    /// </summary>
    public CoverState CoverType { get; }

    public CoverEnteredEvent(
        long eventTimeMs,
        Operator op,
        CoverState coverType,
        int sequenceNumber)
    {
        EventTimeMs = eventTimeMs;
        OperatorId = op.Id;
        Operator = op;
        CoverType = coverType;
        SequenceNumber = sequenceNumber;
    }

    public bool Execute()
    {
        Operator.CurrentCover = CoverType;
        Console.WriteLine($"[{EventTimeMs}ms] {Operator.Name} entered {CoverType} cover");
        return false; // Does not trigger reaction window
    }
}

/// <summary>
/// Event emitted when an operator exits cover.
/// </summary>
public sealed class CoverExitedEvent : ISimulationEvent
{
    /// <summary>
    /// Minimum event duration for timeline rendering (milliseconds).
    /// </summary>
    public const int MinEventDurationMs = 2;
    public long EventTimeMs { get; }
    public Guid OperatorId { get; }
    public int SequenceNumber { get; }

    /// <summary>
    /// The operator who exited cover.
    /// </summary>
    public Operator Operator { get; }

    /// <summary>
    /// The type of cover that was exited.
    /// </summary>
    public CoverState ExitedCoverType { get; }

    public CoverExitedEvent(
        long eventTimeMs,
        Operator op,
        CoverState exitedCoverType,
        int sequenceNumber)
    {
        EventTimeMs = eventTimeMs;
        OperatorId = op.Id;
        Operator = op;
        ExitedCoverType = exitedCoverType;
        SequenceNumber = sequenceNumber;
    }

    public bool Execute()
    {
        Operator.CurrentCover = CoverState.None;
        Console.WriteLine($"[{EventTimeMs}ms] {Operator.Name} exited {ExitedCoverType} cover");
        return false; // Does not trigger reaction window
    }
}
