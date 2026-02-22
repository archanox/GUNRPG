using GUNRPG.Application.Backend;
using LiteDB;

namespace GUNRPG.Infrastructure.Persistence;

public sealed class LiteDbOfflineSyncHeadStore : IOfflineSyncHeadStore
{
    private readonly ILiteCollection<OfflineSyncHeadDocument> _heads;
    private readonly object _syncLock = new();

    public LiteDbOfflineSyncHeadStore(LiteDatabase database)
    {
        _heads = database.GetCollection<OfflineSyncHeadDocument>("offline_sync_heads");
        _heads.EnsureIndex(x => x.OperatorId, true);
    }

    public Task<OfflineSyncHead?> GetAsync(Guid operatorId)
    {
        lock (_syncLock)
        {
            var doc = _heads.FindById(operatorId.ToString());
            if (doc == null)
            {
                return Task.FromResult<OfflineSyncHead?>(null);
            }

            return Task.FromResult<OfflineSyncHead?>(new OfflineSyncHead
            {
                OperatorId = doc.OperatorId,
                SequenceNumber = doc.SequenceNumber,
                ResultOperatorStateHash = doc.ResultOperatorStateHash
            });
        }
    }

    public Task UpsertAsync(OfflineSyncHead head)
    {
        lock (_syncLock)
        {
            _heads.Upsert(new OfflineSyncHeadDocument
            {
                Id = head.OperatorId.ToString(),
                OperatorId = head.OperatorId,
                SequenceNumber = head.SequenceNumber,
                ResultOperatorStateHash = head.ResultOperatorStateHash
            });
        }

        return Task.CompletedTask;
    }

    private sealed class OfflineSyncHeadDocument
    {
        public string Id { get; init; } = string.Empty;
        public Guid OperatorId { get; init; }
        public long SequenceNumber { get; init; }
        public string ResultOperatorStateHash { get; init; } = string.Empty;
    }
}
