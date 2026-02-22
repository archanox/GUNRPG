using GUNRPG.Application.Backend;
using GUNRPG.Infrastructure.Persistence;

namespace GUNRPG.Infrastructure.Backend;

public sealed class ExfilSyncService
{
    private readonly OfflineStore _offlineStore;
    private readonly OnlineGameBackend _onlineBackend;

    public ExfilSyncService(OfflineStore offlineStore, OnlineGameBackend onlineBackend)
    {
        _offlineStore = offlineStore;
        _onlineBackend = onlineBackend;
    }

    public async Task<bool> SyncPendingAsync(CancellationToken cancellationToken = default)
    {
        var pending = _offlineStore.GetAllUnsyncedResults()
            .OrderBy(x => x.OperatorId, StringComparer.Ordinal)
            .ThenBy(x => x.SequenceNumber)
            .ToList();
        var previousByOperator = new Dictionary<string, OfflineMissionEnvelope>(StringComparer.Ordinal);
        foreach (var operatorId in pending.Select(x => x.OperatorId).Distinct(StringComparer.Ordinal))
        {
            var latestSynced = _offlineStore.GetLatestSyncedResult(operatorId);
            if (latestSynced != null)
            {
                previousByOperator[operatorId] = latestSynced;
            }
        }

        foreach (var envelope in pending)
        {
            if (previousByOperator.TryGetValue(envelope.OperatorId, out var previous))
            {
                if (envelope.SequenceNumber != previous.SequenceNumber + 1 ||
                    !string.Equals(envelope.InitialOperatorStateHash, previous.ResultOperatorStateHash, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            var synced = await _onlineBackend.SyncOfflineMission(envelope, cancellationToken);
            if (!synced)
            {
                return false;
            }

            _offlineStore.MarkResultSynced(envelope.Id);
            previousByOperator[envelope.OperatorId] = envelope;
        }

        return true;
    }
}
