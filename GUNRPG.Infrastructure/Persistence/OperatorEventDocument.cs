namespace GUNRPG.Infrastructure.Persistence;

/// <summary>
/// LiteDB document for storing operator events.
/// This is the persistence model - the event objects from Core are mapped to/from this.
/// </summary>
public sealed class OperatorEventDocument
{
    /// <summary>
    /// Auto-incrementing primary key for LiteDB.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// The operator this event belongs to.
    /// Indexed for fast queries.
    /// </summary>
    public Guid OperatorId { get; set; }

    /// <summary>
    /// Sequential number within the operator's event stream.
    /// Indexed for ordering.
    /// </summary>
    public long SequenceNumber { get; set; }

    /// <summary>
    /// Event type discriminator (e.g., "XpGained", "WoundsTreated").
    /// </summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>
    /// JSON-serialized payload.
    /// </summary>
    public string Payload { get; set; } = string.Empty;

    /// <summary>
    /// Hash of the previous event in the chain.
    /// </summary>
    public string PreviousHash { get; set; } = string.Empty;

    /// <summary>
    /// Hash of this event.
    /// </summary>
    public string Hash { get; set; } = string.Empty;

    /// <summary>
    /// When this event was created.
    /// </summary>
    public DateTimeOffset Timestamp { get; set; }
}
