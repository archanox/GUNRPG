using GUNRPG.Core.Operators;

namespace GUNRPG.Application.Operators;

/// <summary>
/// Repository for persisting and retrieving operator events.
/// Implementations must ensure events are stored in sequence order and hash chains are preserved.
/// </summary>
public interface IOperatorEventStore
{
    /// <summary>
    /// Appends a new event to an operator's event stream.
    /// </summary>
    Task AppendEventAsync(OperatorEvent @event);

    /// <summary>
    /// Loads all events for an operator, verifying the hash chain.
    /// If verification fails, returns only events up to the last valid one.
    /// </summary>
    /// <param name="operatorId">The operator's unique identifier.</param>
    /// <returns>A tuple containing the valid events and whether rollback occurred.</returns>
    Task<(IReadOnlyList<OperatorEvent> Events, bool RolledBack)> LoadEventsAsync(Guid operatorId);

    /// <summary>
    /// Gets the current sequence number for an operator (last event).
    /// </summary>
    Task<long> GetCurrentSequenceNumberAsync(Guid operatorId);

    /// <summary>
    /// Checks if an operator exists (has at least one event).
    /// </summary>
    Task<bool> ExistsAsync(Guid operatorId);
}
