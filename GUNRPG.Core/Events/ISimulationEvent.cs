namespace GUNRPG.Core.Events;

/// <summary>
/// Base interface for all simulation events.
/// Events are atomic, timestamped actions that execute at a specific time.
/// </summary>
public interface ISimulationEvent
{
    /// <summary>
    /// The time at which this event should execute.
    /// </summary>
    long EventTimeMs { get; }

    /// <summary>
    /// Unique identifier for the operator that owns this event.
    /// </summary>
    Guid OperatorId { get; }

    /// <summary>
    /// Sequence number for deterministic ordering of events at the same timestamp.
    /// </summary>
    int SequenceNumber { get; }

    /// <summary>
    /// Executes the event and returns whether it triggered a reaction window.
    /// </summary>
    /// <returns>True if this event should trigger a reaction window.</returns>
    bool Execute();
}
