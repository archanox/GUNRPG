using GUNRPG.Core.Operators;

namespace GUNRPG.Application.Operators;

/// <summary>
/// Abstraction for storing and retrieving operator events.
/// Events are append-only and must maintain hash chain integrity.
/// Implementations must ensure atomic writes and proper ordering.
/// </summary>
public interface IOperatorEventStore
{
    /// <summary>
    /// Appends a new event to an operator's event stream.
    /// Events must be appended in sequence order with no gaps.
    /// </summary>
    /// <param name="evt">The event to append</param>
    /// <exception cref="InvalidOperationException">If sequence number is invalid or hash chain is broken</exception>
    Task AppendEventAsync(OperatorEvent evt);

    /// <summary>
    /// Loads all events for a specific operator, ordered by sequence number.
    /// Returns empty list if operator has no events.
    /// Verifies hash chain integrity during load - fails fast if tampering detected.
    /// </summary>
    /// <param name="operatorId">The operator to load events for</param>
    /// <returns>Ordered list of events</returns>
    /// <exception cref="InvalidOperationException">If hash chain verification fails</exception>
    Task<IReadOnlyList<OperatorEvent>> LoadEventsAsync(OperatorId operatorId);

    /// <summary>
    /// Checks if an operator exists (has at least one event).
    /// </summary>
    Task<bool> OperatorExistsAsync(OperatorId operatorId);

    /// <summary>
    /// Gets the current sequence number for an operator.
    /// Returns -1 if operator doesn't exist.
    /// </summary>
    Task<long> GetCurrentSequenceAsync(OperatorId operatorId);

    /// <summary>
    /// Lists all known operator IDs that have events.
    /// For production use, consider adding pagination.
    /// </summary>
    Task<IReadOnlyList<OperatorId>> ListOperatorIdsAsync();
}
