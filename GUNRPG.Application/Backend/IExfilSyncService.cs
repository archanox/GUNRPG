namespace GUNRPG.Application.Backend;

/// <summary>
/// Responsible for synchronizing pending offline mission envelopes to the server for a given operator.
/// Enforces chain-of-trust: sequence continuity and hash-chain integrity before any envelope is sent.
/// </summary>
public interface IExfilSyncService
{
    /// <summary>
    /// Synchronises all unsynced offline mission envelopes for the specified operator.
    /// Validates sequence continuity and InitialStateHash ↔ ResultStateHash chain before each upload.
    /// Returns a failed <see cref="SyncResult"/> on the first integrity violation or server rejection.
    /// </summary>
    Task<SyncResult> SyncAsync(string operatorId, CancellationToken cancellationToken = default);
}
