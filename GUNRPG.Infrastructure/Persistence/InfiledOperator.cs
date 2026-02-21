namespace GUNRPG.Infrastructure.Persistence;

/// <summary>
/// LiteDB document representing an operator that has been infiled (snapshot taken)
/// for offline play. Stored in the "infiled_operators" collection.
/// </summary>
public class InfiledOperator
{
    /// <summary>
    /// The operator ID (same as the server-side operator ID).
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// JSON snapshot of the full operator state at infil time.
    /// </summary>
    public string SnapshotJson { get; set; } = string.Empty;

    /// <summary>
    /// UTC timestamp of when the infil occurred.
    /// </summary>
    public DateTime InfiledUtc { get; set; }

    /// <summary>
    /// Whether this is the currently active infiled operator.
    /// </summary>
    public bool IsActive { get; set; }
}
