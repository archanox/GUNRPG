using GUNRPG.Application.Backend;
using GUNRPG.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GUNRPG.Infrastructure.Backend;

public sealed class ExfilSyncService : IExfilSyncService
{
    private readonly OfflineStore _offlineStore;
    private readonly OnlineGameBackend _onlineBackend;
    private readonly ILogger<ExfilSyncService> _logger;

    public ExfilSyncService(OfflineStore offlineStore, OnlineGameBackend onlineBackend, ILogger<ExfilSyncService>? logger = null)
    {
        _offlineStore = offlineStore;
        _onlineBackend = onlineBackend;
        _logger = logger ?? NullLogger<ExfilSyncService>.Instance;
    }

    /// <inheritdoc />
    public async Task<SyncResult> SyncAsync(string operatorId, CancellationToken cancellationToken = default)
    {
        var pending = _offlineStore.GetUnsyncedResults(operatorId)
            .OrderBy(x => x.SequenceNumber)
            .ToList();

        _logger.LogInformation("[SYNC] Operator {OperatorId}: {PendingCount} unsynced envelope(s) pending.", operatorId, pending.Count);

        if (pending.Count == 0)
        {
            return SyncResult.Ok(0);
        }

        var latestSynced = _offlineStore.GetLatestSyncedResult(operatorId);
        OfflineMissionEnvelope? previous = latestSynced;

        // When there is no previously synced envelope, validate the first envelope's
        // InitialOperatorStateHash against the server's current operator state.
        // This detects fabricated chains early and provides faster user feedback.
        if (previous == null)
        {
            var serverDto = await _onlineBackend.GetOperatorAsync(operatorId);
            if (serverDto != null)
            {
                var serverHash = OfflineMissionHashing.ComputeOperatorStateHash(serverDto);
                var firstEnvelope = pending[0];
                if (!string.Equals(firstEnvelope.InitialOperatorStateHash, serverHash, StringComparison.Ordinal))
                {
                    var reason = $"Initial state hash mismatch for operator {operatorId}: server hash does not match first envelope's initial hash (seq={firstEnvelope.SequenceNumber}).";
                    _logger.LogWarning("[SYNC] FAIL — {Reason}", reason);
                    return SyncResult.Fail(reason, isIntegrityFailure: true);
                }
            }
        }

        int synced = 0;
        foreach (var envelope in pending)
        {
            if (previous != null)
            {
                if (envelope.SequenceNumber != previous.SequenceNumber + 1)
                {
                    var reason = $"Sequence gap for operator {operatorId}: expected {previous.SequenceNumber + 1}, got {envelope.SequenceNumber}.";
                    _logger.LogWarning("[SYNC] FAIL — {Reason}", reason);
                    return SyncResult.Fail(reason, isIntegrityFailure: true);
                }

                if (!string.Equals(envelope.InitialOperatorStateHash, previous.ResultOperatorStateHash, StringComparison.Ordinal))
                {
                    var reason = $"Hash chain mismatch for operator {operatorId} at sequence {envelope.SequenceNumber}.";
                    _logger.LogWarning("[SYNC] FAIL — {Reason}", reason);
                    return SyncResult.Fail(reason, isIntegrityFailure: true);
                }
            }

            _logger.LogDebug("[SYNC] Sending envelope seq={Seq} seed={Seed} initialHash={InitialHash} resultHash={ResultHash}",
                envelope.SequenceNumber, envelope.RandomSeed, envelope.InitialOperatorStateHash, envelope.ResultOperatorStateHash);

            var ok = await _onlineBackend.SyncOfflineMission(envelope, cancellationToken);
            if (!ok)
            {
                var reason = $"Server rejected envelope seq={envelope.SequenceNumber} for operator {operatorId}.";
                _logger.LogWarning("[SYNC] FAIL — {Reason}", reason);
                return SyncResult.Fail(reason);
            }

            _offlineStore.MarkResultSynced(envelope.Id);
            previous = envelope;
            synced++;
        }

        _logger.LogInformation("[SYNC] SUCCESS — {Synced} envelope(s) synced for operator {OperatorId}.", synced, operatorId);
        return SyncResult.Ok(synced);
    }
}
