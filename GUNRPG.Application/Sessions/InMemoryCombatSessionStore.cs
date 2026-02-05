using System.Collections.Concurrent;

namespace GUNRPG.Application.Sessions;

public sealed class InMemoryCombatSessionStore : ICombatSessionStore
{
    private readonly ConcurrentDictionary<Guid, CombatSession> _sessions = new();

    public CombatSession Create(CombatSession session)
    {
        if (!_sessions.TryAdd(session.Id, session))
            throw new InvalidOperationException($"Session {session.Id} already exists.");

        return session;
    }

    public CombatSession? Get(Guid id)
    {
        _sessions.TryGetValue(id, out var session);
        return session;
    }

    public void Upsert(CombatSession session)
    {
        _sessions[session.Id] = session;
    }

    public IReadOnlyCollection<CombatSession> List() => _sessions.Values.ToArray();
}
