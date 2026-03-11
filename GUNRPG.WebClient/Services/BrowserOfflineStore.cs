using System.Text.Json;
using GUNRPG.Application.Backend;
using GUNRPG.WebClient.Helpers;
using Microsoft.JSInterop;

namespace GUNRPG.WebClient.Services;

public sealed class BrowserOfflineStore
{
    private const string MetadataStore = "metadata";
    private const string PendingExfilPrefix = "pendingExfil:";
    private const string ProcessedOutcomePrefix = "processedOutcome:";
    private const string CorruptedPrefix = "corrupted:";

    private readonly IJSRuntime _js;
    private readonly JsonSerializerOptions _jsonOptions;

    public BrowserOfflineStore(IJSRuntime js)
    {
        _js = js;
        _jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    }

    public async Task SaveInfiledOperatorAsync(OperatorState operatorState)
    {
        var record = new BrowserInfiledOperatorRecord
        {
            Id = operatorState.Id.ToString(),
            SnapshotJson = JsonSerializer.Serialize(operatorState, _jsonOptions),
            InfiledUtc = DateTimeOffset.UtcNow,
            IsActive = true
        };

        await _js.InvokeVoidAsync("gunRpgStorage.saveInfiledOperator", record);
    }

    public async Task<OperatorState?> GetActiveInfiledOperatorAsync()
    {
        var record = await _js.InvokeAsync<BrowserInfiledOperatorRecord?>("gunRpgStorage.getActiveInfiledOperator");
        return DeserializeOperator(record);
    }

    public async Task<OperatorState?> GetInfiledOperatorAsync(Guid operatorId)
    {
        var record = await _js.InvokeAsync<BrowserInfiledOperatorRecord?>("gunRpgStorage.getInfiledOperator", operatorId.ToString());
        return DeserializeOperator(record);
    }

    public Task<bool> HasActiveInfiledOperatorAsync() =>
        _js.InvokeAsync<bool>("gunRpgStorage.hasActiveInfiledOperator").AsTask();

    public async Task UpdateOperatorSnapshotAsync(Guid operatorId, OperatorState updatedState)
    {
        var existing = await _js.InvokeAsync<BrowserInfiledOperatorRecord?>("gunRpgStorage.getInfiledOperator", operatorId.ToString());
        if (existing is null)
        {
            await SaveInfiledOperatorAsync(updatedState);
            return;
        }

        existing.SnapshotJson = JsonSerializer.Serialize(updatedState, _jsonOptions);
        await _js.InvokeVoidAsync("gunRpgStorage.updateInfiledOperator", existing);
    }

    public async Task RemoveInfiledOperatorAsync(Guid operatorId)
    {
        await _js.InvokeVoidAsync("gunRpgStorage.removeInfiledOperator", operatorId.ToString());
        await RemovePendingExfilAsync(operatorId);
    }

    public async Task SaveMissionResultAsync(OfflineMissionEnvelope result)
    {
        var allForOperator = (await GetAllMissionResultsAsync())
            .Where(x => string.Equals(x.OperatorId, result.OperatorId, StringComparison.Ordinal))
            .OrderBy(x => x.SequenceNumber)
            .ToList();

        var previous = allForOperator.LastOrDefault();
        var expectedSequence = previous is null ? 1 : previous.SequenceNumber + 1;
        if (result.SequenceNumber != expectedSequence)
        {
            throw new InvalidOperationException(
                $"Offline mission sequence mismatch for operator {result.OperatorId}. Expected {expectedSequence}, got {result.SequenceNumber}.");
        }

        if (previous is not null &&
            !string.Equals(result.InitialOperatorStateHash, previous.ResultOperatorStateHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Offline mission hash chain mismatch for operator {result.OperatorId} at sequence {result.SequenceNumber}.");
        }

        if (string.IsNullOrWhiteSpace(result.Id))
            result.Id = Guid.NewGuid().ToString();

        await _js.InvokeVoidAsync("gunRpgStorage.saveOfflineMissionResult", result);
    }

    public async Task<List<OfflineMissionEnvelope>> GetUnsyncedResultsAsync(Guid operatorId)
    {
        return (await GetAllMissionResultsAsync())
            .Where(x => string.Equals(x.OperatorId, operatorId.ToString(), StringComparison.Ordinal) && !x.Synced)
            .OrderBy(x => x.SequenceNumber)
            .ToList();
    }

    public async Task<OfflineMissionEnvelope?> GetLatestSyncedResultAsync(Guid operatorId)
    {
        return (await GetAllMissionResultsAsync())
            .Where(x => string.Equals(x.OperatorId, operatorId.ToString(), StringComparison.Ordinal) && x.Synced)
            .OrderByDescending(x => x.SequenceNumber)
            .FirstOrDefault();
    }

    public async Task MarkResultSyncedAsync(string resultId)
    {
        var envelope = await _js.InvokeAsync<OfflineMissionEnvelope?>("gunRpgStorage.getOfflineMissionResult", resultId);
        if (envelope is null)
            return;

        envelope.Synced = true;
        await _js.InvokeVoidAsync("gunRpgStorage.saveOfflineMissionResult", envelope);
    }

    public async Task<List<OfflineMissionEnvelope>> GetAllUnsyncedResultsAsync()
    {
        return (await GetAllMissionResultsAsync())
            .Where(x => !x.Synced)
            .OrderBy(x => x.SequenceNumber)
            .ToList();
    }

    public async Task<long> GetNextMissionSequenceAsync(Guid operatorId)
    {
        var latest = (await GetAllMissionResultsAsync())
            .Where(x => string.Equals(x.OperatorId, operatorId.ToString(), StringComparison.Ordinal))
            .OrderByDescending(x => x.SequenceNumber)
            .FirstOrDefault();

        return latest is null ? 1 : latest.SequenceNumber + 1;
    }

    public Task SetPendingExfilAsync(Guid operatorId) =>
        SetMetadataAsync(PendingExfilPrefix + operatorId, operatorId.ToString());

    public async Task<bool> HasPendingExfilAsync(Guid operatorId) =>
        !string.IsNullOrEmpty(await GetMetadataAsync(PendingExfilPrefix + operatorId));

    public Task RemovePendingExfilAsync(Guid operatorId) =>
        RemoveMetadataAsync(PendingExfilPrefix + operatorId);

    public async Task<List<Guid>> GetPendingExfilOperatorIdsAsync()
    {
        var entries = await GetAllMetadataEntriesAsync();
        return entries
            .Where(x => x.Key.StartsWith(PendingExfilPrefix, StringComparison.Ordinal))
            .Select(x => Guid.TryParse(x.Value, out var value) ? value : Guid.Empty)
            .Where(x => x != Guid.Empty)
            .Distinct()
            .ToList();
    }

    public Task MarkOutcomeProcessedAsync(Guid sessionId) =>
        SetMetadataAsync(ProcessedOutcomePrefix + sessionId, bool.TrueString);

    public async Task<bool> IsOutcomeProcessedAsync(Guid sessionId) =>
        bool.TryParse(await GetMetadataAsync(ProcessedOutcomePrefix + sessionId), out var value) && value;

    public Task ClearOutcomeProcessedAsync(Guid sessionId) =>
        RemoveMetadataAsync(ProcessedOutcomePrefix + sessionId);

    public Task MarkCorruptedAsync(Guid operatorId, string reason) =>
        SetMetadataAsync(CorruptedPrefix + operatorId, reason);

    public Task<string?> GetCorruptedReasonAsync(Guid operatorId) =>
        GetMetadataAsync(CorruptedPrefix + operatorId);

    private async Task<List<OfflineMissionEnvelope>> GetAllMissionResultsAsync() =>
        await _js.InvokeAsync<List<OfflineMissionEnvelope>>("gunRpgStorage.getAllOfflineMissionResults");

    private async Task SetMetadataAsync(string key, string value)
    {
        var record = new BrowserMetadataRecord { Key = key, Value = value };
        await _js.InvokeVoidAsync("gunRpgStorage.putValue", MetadataStore, key, record);
    }

    private async Task<string?> GetMetadataAsync(string key)
    {
        var record = await _js.InvokeAsync<BrowserMetadataRecord?>("gunRpgStorage.getValue", MetadataStore, key);
        return record?.Value;
    }

    private Task RemoveMetadataAsync(string key) =>
        _js.InvokeVoidAsync("gunRpgStorage.deleteValue", MetadataStore, key).AsTask();

    private async Task<List<BrowserMetadataRecord>> GetAllMetadataEntriesAsync() =>
        await _js.InvokeAsync<List<BrowserMetadataRecord>>("gunRpgStorage.getAllValues", MetadataStore);

    private static OperatorState? DeserializeOperator(BrowserInfiledOperatorRecord? record)
    {
        if (record is null || string.IsNullOrWhiteSpace(record.SnapshotJson))
            return null;

        return JsonSerializer.Deserialize<OperatorState>(record.SnapshotJson, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    public sealed class BrowserInfiledOperatorRecord
    {
        public string Id { get; set; } = string.Empty;
        public string SnapshotJson { get; set; } = string.Empty;
        public DateTimeOffset InfiledUtc { get; set; }
        public bool IsActive { get; set; }
    }

    public sealed class BrowserMetadataRecord
    {
        public string Key { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }
}
