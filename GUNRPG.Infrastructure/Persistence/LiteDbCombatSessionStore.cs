using GUNRPG.Application.Sessions;
using LiteDB;

namespace GUNRPG.Infrastructure.Persistence;

/// <summary>
/// LiteDB-backed implementation of ICombatSessionStore.
/// Persists combat session snapshots in an embedded document database.
/// Thread-safe for concurrent requests.
/// </summary>
public sealed class LiteDbCombatSessionStore : ICombatSessionStore
{
    private readonly ILiteCollection<CombatSessionSnapshot> _sessions;

    public LiteDbCombatSessionStore(LiteDatabase database)
    {
        _sessions = (database ?? throw new ArgumentNullException(nameof(database)))
            .GetCollection<CombatSessionSnapshot>("combat_sessions");
        
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

    public Task<CombatSessionSnapshot?> LoadAsync(Guid id)
    {
        var snapshot = _sessions.FindById(id);

        // Reject completed sessions whose FinalHash does not match recomputed replay data.
        if (snapshot != null && !IsHashValid(snapshot))
        {
            return Task.FromResult<CombatSessionSnapshot?>(null);
        }

        return Task.FromResult<CombatSessionSnapshot?>(snapshot);
    }

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
