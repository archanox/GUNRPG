using GUNRPG.Application.Backend;
using LiteDB;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace GUNRPG.Infrastructure.Persistence;

/// <summary>
/// Manages local LiteDB storage for offline mode.
/// Handles infilled operator snapshots and offline mission results.
/// </summary>
public sealed class OfflineStore
{
    private readonly ILiteCollection<InfilledOperator> _operators;
    private readonly ILiteCollection<OfflineMissionResult> _missionResults;

    public OfflineStore(LiteDatabase database)
    {
        _operators = database.GetCollection<InfilledOperator>("infilled_operators");
        _operators.EnsureIndex(x => x.Id);
        _operators.EnsureIndex(x => x.IsActive);

        _missionResults = database.GetCollection<OfflineMissionResult>("offline_mission_results");
        _missionResults.EnsureIndex(x => x.OperatorId);
        _missionResults.EnsureIndex(x => x.Synced);
    }

    /// <summary>
    /// Saves an infilled operator snapshot to local storage.
    /// Deactivates any previously active infilled operator.
    /// </summary>
    public void SaveInfilledOperator(OperatorDto operatorDto)
    {
        // Deactivate any previously active operators
        var activeOps = _operators.Find(x => x.IsActive).ToList();
        foreach (var op in activeOps)
        {
            op.IsActive = false;
            _operators.Update(op);
        }

        var snapshot = new InfilledOperator
        {
            Id = operatorDto.Id,
            SnapshotJson = JsonSerializer.Serialize(operatorDto),
            InfilledUtc = DateTime.UtcNow,
            IsActive = true
        };

        _operators.Upsert(snapshot);
    }

    /// <summary>
    /// Gets the currently active infilled operator, or null if none exists.
    /// </summary>
    public InfilledOperator? GetActiveInfilledOperator()
    {
        return _operators.FindOne(x => x.IsActive);
    }

    /// <summary>
    /// Checks whether an active infilled operator exists.
    /// </summary>
    public bool HasActiveInfilledOperator()
    {
        return _operators.Exists(x => x.IsActive);
    }

    /// <summary>
    /// Gets the infilled operator by ID, or null if not found.
    /// </summary>
    public InfilledOperator? GetInfilledOperator(string id)
    {
        return _operators.FindById(id);
    }

    /// <summary>
    /// Updates the snapshot JSON of an infilled operator (e.g., after offline mission).
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
    /// Deactivates the infilled operator and removes the snapshot (exfil complete).
    /// </summary>
    public void RemoveInfilledOperator(string operatorId)
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
    /// </summary>
    public List<OfflineMissionResult> GetAllUnsyncedResults()
    {
        return _missionResults.Find(x => !x.Synced).ToList();
    }
}
