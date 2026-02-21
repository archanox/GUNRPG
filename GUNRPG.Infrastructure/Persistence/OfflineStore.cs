using GUNRPG.Application.Backend;
using LiteDB;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace GUNRPG.Infrastructure.Persistence;

/// <summary>
/// Manages local LiteDB storage for offline mode.
/// Handles infiled operator snapshots and offline mission results.
/// </summary>
public sealed class OfflineStore
{
    private readonly ILiteCollection<InfiledOperator> _operators;
    private readonly ILiteCollection<OfflineMissionResult> _missionResults;

    public OfflineStore(LiteDatabase database)
    {
        _operators = database.GetCollection<InfiledOperator>("infiled_operators");
        _operators.EnsureIndex(x => x.Id);
        _operators.EnsureIndex(x => x.IsActive);

        _missionResults = database.GetCollection<OfflineMissionResult>("offline_mission_results");
        _missionResults.EnsureIndex(x => x.OperatorId);
        _missionResults.EnsureIndex(x => x.Synced);
    }

    /// <summary>
    /// Saves an infiled operator snapshot to local storage.
    /// Deactivates any previously active infiled operator.
    /// </summary>
    public void SaveInfiledOperator(OperatorDto operatorDto)
    {
        // Deactivate any previously active operators
        var activeOps = _operators.Find(x => x.IsActive).ToList();
        foreach (var op in activeOps)
        {
            op.IsActive = false;
            _operators.Update(op);
        }

        var snapshot = new InfiledOperator
        {
            Id = operatorDto.Id,
            SnapshotJson = JsonSerializer.Serialize(operatorDto),
            InfiledUtc = DateTime.UtcNow,
            IsActive = true
        };

        _operators.Upsert(snapshot);
    }

    /// <summary>
    /// Gets the currently active infiled operator, or null if none exists.
    /// </summary>
    public InfiledOperator? GetActiveInfiledOperator()
    {
        return _operators.FindOne(x => x.IsActive);
    }

    /// <summary>
    /// Checks whether an active infiled operator exists.
    /// </summary>
    public bool HasActiveInfiledOperator()
    {
        return _operators.Exists(x => x.IsActive);
    }

    /// <summary>
    /// Gets the infiled operator by ID, or null if not found.
    /// </summary>
    public InfiledOperator? GetInfiledOperator(string id)
    {
        return _operators.FindById(id);
    }

    /// <summary>
    /// Updates the snapshot JSON of an infiled operator (e.g., after offline mission).
    /// </summary>
    public void UpdateOperatorSnapshot(string operatorId, OperatorDto updatedDto)
    {
        var existing = _operators.FindById(operatorId);
        if (existing != null)
        {
            existing.SnapshotJson = JsonSerializer.Serialize(updatedDto);
            _operators.Update(existing);
        }
    }

    /// <summary>
    /// Deactivates the infiled operator and removes the snapshot (exfil complete).
    /// </summary>
    public void RemoveInfiledOperator(string operatorId)
    {
        _operators.Delete(operatorId);
    }

    /// <summary>
    /// Saves an offline mission result.
    /// </summary>
    public void SaveMissionResult(OfflineMissionResult result)
    {
        _missionResults.Insert(result);
    }

    /// <summary>
    /// Gets all unsynced offline mission results for a given operator.
    /// </summary>
    public List<OfflineMissionResult> GetUnsyncedResults(string operatorId)
    {
        return _missionResults.Find(x => x.OperatorId == operatorId && !x.Synced).ToList();
    }

    /// <summary>
    /// Marks a mission result as synced.
    /// </summary>
    public void MarkResultSynced(string resultId)
    {
        var result = _missionResults.FindById(resultId);
        if (result != null)
        {
            result.Synced = true;
            _missionResults.Update(result);
        }
    }

    /// <summary>
    /// Gets all unsynced results across all operators.
    /// TODO: Future ExfilSyncService will call this to retrieve all pending results for server reconciliation.
    /// </summary>
    public List<OfflineMissionResult> GetAllUnsyncedResults()
    {
        return _missionResults.Find(x => !x.Synced).ToList();
    }
}
