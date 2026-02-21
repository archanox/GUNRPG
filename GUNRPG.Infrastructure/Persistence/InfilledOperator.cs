namespace GUNRPG.Infrastructure.Persistence;

/// <summary>
/// LiteDB document representing an operator that has been infilled (snapshot taken)
/// for offline play. Stored in the "infilled_operators" collection.
/// </summary>
public class InfilledOperator
{
    /// <summary>
    /// The operator ID (same as the server-side operator ID).
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// JSON snapshot of the full operator state at infill time.
    /// </summary>
    public string SnapshotJson { get; set; } = string.Empty;

    /// <summary>
    /// UTC timestamp of when the infill occurred.
    /// </summary>
    public DateTime InfilledUtc { get; set; }

    /// <summary>
    /// Whether this is the currently active infilled operator.
    /// </summary>
    public bool IsActive { get; set; }
}
