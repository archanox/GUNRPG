namespace GUNRPG.Infrastructure.Persistence;

/// <summary>
/// LiteDB document representing a mission result executed while offline.
/// Stored in the "offline_mission_results" collection.
/// These results are synced to the server during exfil.
/// </summary>
public class OfflineMissionResult
{
    /// <summary>
    /// Unique ID for this offline result.
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// The operator ID that executed this mission.
    /// </summary>
    public string OperatorId { get; set; } = string.Empty;

    /// <summary>
    /// JSON-serialized mission result data.
    /// </summary>
    public string ResultJson { get; set; } = string.Empty;

    /// <summary>
    /// UTC timestamp of when the mission was executed offline.
    /// </summary>
    public DateTime ExecutedUtc { get; set; }

    /// <summary>
    /// Whether this result has been synced to the server.
    /// </summary>
    public bool Synced { get; set; }
}
