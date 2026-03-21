using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using GUNRPG.Application.Combat;

namespace GUNRPG.Application.Sessions;

public sealed class InMemoryCombatSessionStore : ICombatSessionStore
{
    private readonly ConcurrentDictionary<Guid, CombatSessionSnapshot> _sessions = new();
    private readonly ILogger<InMemoryCombatSessionStore>? _logger;

    public InMemoryCombatSessionStore(ILogger<InMemoryCombatSessionStore>? logger = null)
    {
        _logger = logger;
    }

    public Task SaveAsync(CombatSessionSnapshot snapshot)
    {
        // Store a defensive copy so that subsequent external mutations of the snapshot
        // (e.g. to ReplayTurns or FinalHash) do not corrupt the in-memory store.
        _sessions[snapshot.Id] = CopySnapshot(snapshot);
        return Task.CompletedTask;
    }

    public async Task<CombatSessionSnapshot?> LoadAsync(Guid id)
    {
        _sessions.TryGetValue(id, out var snapshot);

        // Validate before copying: we check the stored data directly so any in-flight
        // mutation of the stored reference would be caught. This is intentional; the
        // defensive copy returned to the caller is produced only after the check passes.
        if (snapshot != null && !await IsHashValidAsync(snapshot))
        {
            return null;
        }

        // Return a defensive copy to prevent callers from mutating the stored snapshot.
        return snapshot == null ? null : CopySnapshot(snapshot);
    }

    public Task DeleteAsync(Guid id)
    {
        _sessions.TryRemove(id, out _);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyCollection<CombatSessionSnapshot>> ListAsync()
    {
        IReadOnlyCollection<CombatSessionSnapshot> snapshots = _sessions.Values.Select(CopySnapshot).ToArray();
        return Task.FromResult(snapshots);
    }

    /// <summary>
    /// Creates a shallow copy of <paramref name="snapshot"/> where mutable members
    /// (ReplayTurns list, FinalHash array) are also copied to ensure full isolation.
    /// </summary>
    private static CombatSessionSnapshot CopySnapshot(CombatSessionSnapshot snapshot) =>
        new()
        {
            Id = snapshot.Id,
            OperatorId = snapshot.OperatorId,
            Phase = snapshot.Phase,
            TurnNumber = snapshot.TurnNumber,
            Combat = snapshot.Combat,
            Player = snapshot.Player,
            Enemy = snapshot.Enemy,
            Pet = snapshot.Pet,
            EnemyLevel = snapshot.EnemyLevel,
            Seed = snapshot.Seed,
            PostCombatResolved = snapshot.PostCombatResolved,
            CreatedAt = snapshot.CreatedAt,
            CompletedAt = snapshot.CompletedAt,
            LastActionTimestamp = snapshot.LastActionTimestamp,
            ReplayInitialSnapshotJson = snapshot.ReplayInitialSnapshotJson,
            // Shallow copy of the list is sufficient because IntentSnapshot is immutable.
            ReplayTurns = snapshot.ReplayTurns.ToList(),
            Version = snapshot.Version,
            FinalHash = snapshot.FinalHash != null ? (byte[])snapshot.FinalHash.Clone() : null,
        };

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
    /// without a recorded initial snapshot. Recursion is prevented naturally because the inner
    /// replay sessions start from the initial snapshot which has an empty
    /// <see cref="CombatSessionSnapshot.ReplayInitialSnapshotJson"/>.
    /// </para>
    /// </summary>
    /// <returns>
    /// <c>true</c> if the session is in-progress (not yet completed), has no stored hash, or the
    /// stored hash matches the recomputed value; <c>false</c> if the hash is invalid.
    /// </returns>
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
}
