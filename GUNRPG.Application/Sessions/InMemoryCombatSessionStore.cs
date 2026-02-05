using System.Collections.Concurrent;

namespace GUNRPG.Application.Sessions;

public sealed class InMemoryCombatSessionStore : ICombatSessionStore
{
    private readonly ConcurrentDictionary<Guid, CombatSessionSnapshot> _sessions = new();

    public CombatSessionSnapshot Create(CombatSessionSnapshot snapshot)
    {
        if (!_sessions.TryAdd(snapshot.Id, snapshot))
            throw new InvalidOperationException($"Session {snapshot.Id} already exists.");

        return snapshot;
    }

    public CombatSessionSnapshot? Get(Guid id)
    {
        _sessions.TryGetValue(id, out var snapshot);
        return snapshot;
    }

    public void Upsert(CombatSessionSnapshot snapshot)
    {
        _sessions[snapshot.Id] = snapshot;
    }

    public IReadOnlyCollection<CombatSessionSnapshot> List() => _sessions.Values.ToArray();
}
