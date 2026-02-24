namespace GUNRPG.Application.Backend;

/// <summary>
/// Represents the outcome of an offline-to-online exfil synchronization attempt.
/// </summary>
public sealed class SyncResult
{
    public bool Success { get; init; }
    public int EnvelopesSynced { get; init; }
    public string? FailureReason { get; init; }

    /// <summary>
    /// True when failure is due to a permanent integrity violation (sequence gap or hash chain mismatch).
    /// Integrity failures require re-infil to recover; transient HTTP failures allow retry.
    /// </summary>
    public bool IsIntegrityFailure { get; init; }

    public static SyncResult Ok(int envelopesSynced) =>
        new() { Success = true, EnvelopesSynced = envelopesSynced };

    public static SyncResult Fail(string reason, bool isIntegrityFailure = false) =>
        new() { Success = false, FailureReason = reason, IsIntegrityFailure = isIntegrityFailure };
}
