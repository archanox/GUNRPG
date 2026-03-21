using System.Collections.Concurrent;

namespace GUNRPG.Application.Sessions;

public sealed class InMemoryCombatSessionStore : ICombatSessionStore
{
    private readonly ConcurrentDictionary<Guid, CombatSessionSnapshot> _sessions = new();

    public Task SaveAsync(CombatSessionSnapshot snapshot)
    {
        // Store a defensive copy so that subsequent external mutations of the snapshot
        // (e.g. to ReplayTurns or FinalHash) do not corrupt the in-memory store.
        _sessions[snapshot.Id] = CopySnapshot(snapshot);
        return Task.CompletedTask;
    }

    public Task<CombatSessionSnapshot?> LoadAsync(Guid id)
    {
        _sessions.TryGetValue(id, out var snapshot);

        // Validate before copying: we check the stored data directly so any in-flight
        // mutation of the stored reference would be caught. This is intentional; the
        // defensive copy returned to the caller is produced only after the check passes.
        if (snapshot != null && !IsHashValid(snapshot))
        {
            return Task.FromResult<CombatSessionSnapshot?>(null);
        }

        // Return a defensive copy to prevent callers from mutating the stored snapshot.
        return Task.FromResult(snapshot == null ? null : CopySnapshot(snapshot));
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
    /// Returns <c>false</c> if the snapshot is a completed session whose stored
    /// <see cref="CombatSessionSnapshot.FinalHash"/> does not match the hash recomputed from
    /// its replay-critical fields; <c>true</c> in all other cases (in-progress, no hash, or valid).
    /// </summary>
    private static bool IsHashValid(CombatSessionSnapshot snapshot)
    {
        if (snapshot.Phase != SessionPhase.Completed || snapshot.FinalHash == null)
        {
            return true;
        }

        var computed = CombatSessionHasher.ComputeHash(
            snapshot.Id,
            snapshot.Seed,
            snapshot.Version > 0 ? snapshot.Version : CombatSession.CurrentVersion,
            snapshot.TurnNumber,
            snapshot.ReplayTurns);

        return computed.AsSpan().SequenceEqual(snapshot.FinalHash);
    }
}
