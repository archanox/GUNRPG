using System.Net.Http.Json;
using System.Collections.Concurrent;
using GUNRPG.Application.Backend;
using GUNRPG.WebClient.Helpers;

namespace GUNRPG.WebClient.Services;

public sealed class OfflineSyncService
{
    private readonly ApiClient _api;
    private readonly BrowserOfflineStore _offlineStore;
    private readonly SemaphoreSlim _syncAllGate = new(1, 1);
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _operatorGates = new();

    public OfflineSyncService(ApiClient api, BrowserOfflineStore offlineStore)
    {
        _api = api;
        _offlineStore = offlineStore;
    }

    public async Task<int> GetPendingEnvelopeCountAsync() => (await _offlineStore.GetAllUnsyncedResultsAsync()).Count;

    public async Task<bool> HasPendingExfilAsync(Guid operatorId) => await _offlineStore.HasPendingExfilAsync(operatorId);

    public async Task TrySyncAllAsync()
    {
        await _syncAllGate.WaitAsync();
        try
        {
            var operators = new HashSet<Guid>((await _offlineStore.GetAllUnsyncedResultsAsync())
                .Select(x => Guid.TryParse(x.OperatorId, out var id) ? id : Guid.Empty)
                .Where(x => x != Guid.Empty));

            foreach (var operatorId in await _offlineStore.GetPendingExfilOperatorIdsAsync())
                operators.Add(operatorId);

            foreach (var operatorId in operators)
                await SyncAndFinalizeAsync(operatorId);
        }
        finally
        {
            _syncAllGate.Release();
        }
    }

    public async Task<string?> SyncAndFinalizeAsync(Guid operatorId)
    {
        var gate = _operatorGates.GetOrAdd(operatorId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            var syncResult = await SyncCoreAsync(operatorId);
            if (!syncResult.Success)
                return syncResult.FailureReason;

            if (!await _offlineStore.HasPendingExfilAsync(operatorId))
            {
                var snapshot = await _offlineStore.GetInfiledOperatorAsync(operatorId);
                if (snapshot is not null && string.Equals(snapshot.CurrentMode, "Base", StringComparison.OrdinalIgnoreCase))
                    await _offlineStore.RemoveInfiledOperatorAsync(operatorId);
                return null;
            }

            using var response = await _api.PostAsync($"/operators/{operatorId}/infil/complete");
            if (!response.IsSuccessStatusCode)
                return $"Queued exfil could not be finalized: {response.StatusCode}";

            await _offlineStore.RemovePendingExfilAsync(operatorId);
            await _offlineStore.RemoveInfiledOperatorAsync(operatorId);
            return null;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<SyncResult> SyncAsync(Guid operatorId, CancellationToken cancellationToken = default)
    {
        var gate = _operatorGates.GetOrAdd(operatorId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await SyncCoreAsync(operatorId, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<SyncResult> SyncCoreAsync(Guid operatorId, CancellationToken cancellationToken = default)
    {
        var pending = await _offlineStore.GetUnsyncedResultsAsync(operatorId);
        if (pending.Count == 0)
            return SyncResult.Ok(0);

        var latestSynced = await _offlineStore.GetLatestSyncedResultAsync(operatorId);
        OfflineMissionEnvelope? previous = latestSynced;

        if (previous is null)
        {
            var serverOperator = await GetRemoteOperatorAsync(operatorId, cancellationToken);
            if (serverOperator is not null)
            {
                var serverHash = OfflineMissionHashing.ComputeOperatorStateHash(serverOperator);
                var firstEnvelope = pending[0];
                if (!string.Equals(firstEnvelope.InitialOperatorStateHash, serverHash, StringComparison.Ordinal))
                {
                    var reason = $"Initial state hash mismatch for operator {operatorId}.";
                    await _offlineStore.MarkCorruptedAsync(operatorId, reason);
                    await _offlineStore.RemoveInfiledOperatorAsync(operatorId);
                    return SyncResult.Fail(reason, isIntegrityFailure: true);
                }
            }
        }

        var synced = 0;
        foreach (var envelope in pending)
        {
            if (previous is not null)
            {
                if (envelope.SequenceNumber != previous.SequenceNumber + 1)
                {
                    var reason = $"Sequence gap for operator {operatorId}: expected {previous.SequenceNumber + 1}, got {envelope.SequenceNumber}.";
                    await _offlineStore.MarkCorruptedAsync(operatorId, reason);
                    return SyncResult.Fail(reason, isIntegrityFailure: true);
                }

                if (!string.Equals(envelope.InitialOperatorStateHash, previous.ResultOperatorStateHash, StringComparison.Ordinal))
                {
                    var reason = $"Hash chain mismatch for operator {operatorId} at sequence {envelope.SequenceNumber}.";
                    await _offlineStore.MarkCorruptedAsync(operatorId, reason);
                    return SyncResult.Fail(reason, isIntegrityFailure: true);
                }
            }

            using var response = await _api.PostAsync("/operators/offline/sync", envelope);
            if (!response.IsSuccessStatusCode)
                return SyncResult.Fail($"Server rejected envelope seq={envelope.SequenceNumber} for operator {operatorId}.");

            await _offlineStore.MarkResultSyncedAsync(envelope.Id);
            previous = envelope;
            synced++;
        }

        return SyncResult.Ok(synced);
    }

    private async Task<OperatorDto?> GetRemoteOperatorAsync(Guid operatorId, CancellationToken cancellationToken)
    {
        using var response = await _api.GetAsync($"/operators/{operatorId}");
        if (!response.IsSuccessStatusCode)
            return null;

        var state = await response.Content.ReadFromJsonAsync<OperatorState>(cancellationToken: cancellationToken);
        return state is null ? null : OfflineModelMapper.ToBackendDto(state);
    }
}
