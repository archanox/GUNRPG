using GUNRPG.Application.Backend;
using GUNRPG.Infrastructure.Persistence;

namespace GUNRPG.Infrastructure.Backend;

public sealed class ExfilSyncService : IExfilSyncService
{
    private readonly OfflineStore _offlineStore;
    private readonly OnlineGameBackend _onlineBackend;

    public ExfilSyncService(OfflineStore offlineStore, OnlineGameBackend onlineBackend)
    {
        _offlineStore = offlineStore;
        _onlineBackend = onlineBackend;
    }

    /// <inheritdoc />
    public async Task<SyncResult> SyncAsync(string operatorId, CancellationToken cancellationToken = default)
    {
        var pending = _offlineStore.GetUnsyncedResults(operatorId)
            .OrderBy(x => x.SequenceNumber)
            .ToList();

        Console.WriteLine($"[SYNC] Operator {operatorId}: {pending.Count} unsynced envelope(s) pending.");

        if (pending.Count == 0)
        {
            return SyncResult.Ok(0);
        }

        var latestSynced = _offlineStore.GetLatestSyncedResult(operatorId);
        OfflineMissionEnvelope? previous = latestSynced;

        int synced = 0;
        foreach (var envelope in pending)
        {
            if (previous != null)
            {
                if (envelope.SequenceNumber != previous.SequenceNumber + 1)
                {
                    var reason = $"Sequence gap for operator {operatorId}: expected {previous.SequenceNumber + 1}, got {envelope.SequenceNumber}.";
                    Console.WriteLine($"[SYNC] FAIL — {reason}");
                    return SyncResult.Fail(reason);
                }

                if (!string.Equals(envelope.InitialOperatorStateHash, previous.ResultOperatorStateHash, StringComparison.Ordinal))
                {
                    var reason = $"Hash chain mismatch for operator {operatorId} at sequence {envelope.SequenceNumber}.";
                    Console.WriteLine($"[SYNC] FAIL — {reason}");
                    return SyncResult.Fail(reason);
                }
            }

            Console.WriteLine($"[SYNC] Sending envelope seq={envelope.SequenceNumber} seed={envelope.RandomSeed} initialHash={envelope.InitialOperatorStateHash} resultHash={envelope.ResultOperatorStateHash}");

            var ok = await _onlineBackend.SyncOfflineMission(envelope, cancellationToken);
            if (!ok)
            {
                var reason = $"Server rejected envelope seq={envelope.SequenceNumber} for operator {operatorId}.";
                Console.WriteLine($"[SYNC] FAIL — {reason}");
                return SyncResult.Fail(reason);
            }

            _offlineStore.MarkResultSynced(envelope.Id);
            previous = envelope;
            synced++;
        }

        Console.WriteLine($"[SYNC] SUCCESS — {synced} envelope(s) synced for operator {operatorId}.");
        return SyncResult.Ok(synced);
    }
}
