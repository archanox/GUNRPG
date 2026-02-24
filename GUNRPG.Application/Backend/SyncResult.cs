namespace GUNRPG.Application.Backend;

/// <summary>
/// Represents the outcome of an offline-to-online exfil synchronization attempt.
/// </summary>
public sealed class SyncResult
{
    public bool Success { get; init; }
    public int EnvelopesSynced { get; init; }
    public string? FailureReason { get; init; }

    public static SyncResult Ok(int envelopesSynced) =>
        new() { Success = true, EnvelopesSynced = envelopesSynced };

    public static SyncResult Fail(string reason) =>
        new() { Success = false, FailureReason = reason };
}
