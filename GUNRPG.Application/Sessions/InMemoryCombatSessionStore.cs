using System.Collections.Concurrent;

namespace GUNRPG.Application.Sessions;

public sealed class InMemoryCombatSessionStore : ICombatSessionStore
{
    private readonly ConcurrentDictionary<Guid, CombatSessionSnapshot> _sessions = new();

    public Task SaveAsync(CombatSessionSnapshot snapshot)
    {
        _sessions[snapshot.Id] = snapshot;
        return Task.CompletedTask;
    }

    public Task<CombatSessionSnapshot?> LoadAsync(Guid id)
    {
        _sessions.TryGetValue(id, out var snapshot);
        return Task.FromResult(snapshot);
    }

    public Task DeleteAsync(Guid id)
    {
        _sessions.TryRemove(id, out _);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyCollection<CombatSessionSnapshot>> ListAsync()
    {
        IReadOnlyCollection<CombatSessionSnapshot> snapshots = _sessions.Values.ToArray();
        return Task.FromResult(snapshots);
    }
}
