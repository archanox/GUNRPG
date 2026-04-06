using GUNRPG.Application.Combat;
using GUNRPG.Application.Sessions;
using LiteDB;
using Microsoft.Extensions.Logging;

namespace GUNRPG.Infrastructure.Persistence;

/// <summary>
/// LiteDB-backed implementation of ICombatSessionStore.
/// Persists combat session snapshots in an embedded document database.
/// Thread-safe for concurrent requests.
/// </summary>
public sealed class LiteDbCombatSessionStore : ICombatSessionStore
{
    private readonly ILiteCollection<CombatSessionSnapshot> _sessions;
    private readonly ILogger<LiteDbCombatSessionStore>? _logger;

    public LiteDbCombatSessionStore(LiteDatabase database, ILogger<LiteDbCombatSessionStore>? logger = null)
    {
        _sessions = (database ?? throw new ArgumentNullException(nameof(database)))
            .GetCollection<CombatSessionSnapshot>("combat_sessions");
        _logger = logger;
        
        // Ensure index on Id for fast lookups
        _sessions.EnsureIndex(x => x.Id);
    }

    public Task SaveAsync(CombatSessionSnapshot snapshot)
    {
        if (snapshot == null)
            throw new ArgumentNullException(nameof(snapshot));

        _sessions.Upsert(snapshot.Id, snapshot);
        return Task.CompletedTask;
    }

    public async Task<CombatSessionSnapshot?> LoadAsync(Guid id)
    {
        var snapshot = _sessions.FindById(id);

        // Reject completed sessions whose FinalHash does not match the recomputed replay hash.
        if (snapshot != null && !await IsHashValidAsync(snapshot))
        {
            return null;
        }

        return snapshot;
    }

    /// <summary>
    /// Validates the <see cref="CombatSessionSnapshot.FinalHash"/> of a completed session.
    /// <para>
    /// For sessions with a <see cref="CombatSessionSnapshot.ReplayInitialSnapshotJson"/>, the full
    /// replay is executed and the resulting simulation state is hashed via
    /// <see cref="CombatSessionHasher.ComputeStateHash"/>. This confirms that the simulation is
    /// deterministic and the stored state was not tampered with.
    /// </para>
    /// <para>
    /// Falls back to input-based <see cref="CombatSessionHasher.ComputeHash"/> for legacy sessions
    /// without a recorded initial snapshot.
    /// </para>
    /// </summary>
    private async Task<bool> IsHashValidAsync(CombatSessionSnapshot snapshot)
    {
        if (snapshot.Phase != SessionPhase.Completed || snapshot.FinalHash == null)
        {
            return true;
        }

        if (!string.IsNullOrEmpty(snapshot.ReplayInitialSnapshotJson))
        {
            // State-based validation: replay the session and hash the resulting simulation output.
            try
            {
                var result = await OfflineCombatReplay.ReplayAsync(
                    snapshot.ReplayInitialSnapshotJson, snapshot.ReplayTurns);
                var computed = CombatSessionHasher.ComputeStateHash(result.FinalSnapshot);

                if (!computed.AsSpan().SequenceEqual(snapshot.FinalHash))
                {
                    _logger?.LogError(
                        "Session {SessionId} failed state-based FinalHash validation. " +
                        "Stored: {Stored}, Computed: {Computed}. Session rejected.",
                        snapshot.Id,
                        Convert.ToHexString(snapshot.FinalHash),
                        Convert.ToHexString(computed));
                    return false;
                }

                // Additional safeguard: ensure the cached snapshot state also matches FinalHash.
                // This catches tampering where someone modifies Player/Enemy fields in the stored
                // snapshot while leaving ReplayTurns+FinalHash unchanged.
                var storedStateHash = CombatSessionHasher.ComputeStateHash(snapshot);
                if (!storedStateHash.AsSpan().SequenceEqual(snapshot.FinalHash))
                {
                    _logger?.LogError(
                        "Session {SessionId} failed cached-state FinalHash validation. " +
                        "Stored snapshot state does not match replay-derived state. " +
                        "Stored: {Stored}, ComputedFromSnapshot: {ComputedFromSnapshot}. Session rejected.",
                        snapshot.Id,
                        Convert.ToHexString(snapshot.FinalHash),
                        Convert.ToHexString(storedStateHash));
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(
                    ex,
                    "Session {SessionId} replay failed during FinalHash validation. Session rejected.",
                    snapshot.Id);
                return false;
            }
        }

        // Input-based fallback for sessions without a recorded initial snapshot (legacy/test sessions).
        // No version fallback: use the stored version directly to avoid silent upgrades.
        var fallback = CombatSessionHasher.ComputeHash(
            snapshot.Id,
            snapshot.Seed,
            snapshot.Version,
            snapshot.BalanceSnapshotVersion,
            snapshot.BalanceSnapshotHash,
            snapshot.TurnNumber,
            snapshot.ReplayTurns);

        if (!fallback.AsSpan().SequenceEqual(snapshot.FinalHash))
        {
            _logger?.LogError(
                "Session {SessionId} failed input-based FinalHash validation. Session rejected.",
                snapshot.Id);
            return false;
        }

        return true;
    }

    public Task DeleteAsync(Guid id)
    {
        _sessions.Delete(id);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyCollection<CombatSessionSnapshot>> ListAsync()
    {
        var snapshots = _sessions.FindAll().ToArray();
        IReadOnlyCollection<CombatSessionSnapshot> result = snapshots;
        return Task.FromResult(result);
    }
}
