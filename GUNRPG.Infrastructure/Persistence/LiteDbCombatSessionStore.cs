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
        return Task.FromResult<CombatSessionSnapshot?>(snapshot);
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
