using System.Collections.Concurrent;

namespace GUNRPG.Application.Backend;

public sealed class InMemoryOfflineSyncHeadStore : IOfflineSyncHeadStore
{
    private readonly ConcurrentDictionary<Guid, OfflineSyncHead> _heads = new();

    public Task<OfflineSyncHead?> GetAsync(Guid operatorId)
    {
        _heads.TryGetValue(operatorId, out var head);
        return Task.FromResult(head);
    }

    public Task UpsertAsync(OfflineSyncHead head)
    {
        _heads[head.OperatorId] = head;
        return Task.CompletedTask;
    }
}
