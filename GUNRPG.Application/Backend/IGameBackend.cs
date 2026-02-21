namespace GUNRPG.Application.Backend;

/// <summary>
/// Abstraction for game backend operations.
/// Online implementation uses HTTP API; offline implementation uses local LiteDB.
/// </summary>
public interface IGameBackend
{
    /// <summary>
    /// Gets the operator state by ID.
    /// Returns null if the operator does not exist.
    /// </summary>
    Task<OperatorDto?> GetOperatorAsync(string id);

    /// <summary>
    /// Infils the operator: takes a snapshot of current server state for offline use.
    /// Only available in online mode.
    /// </summary>
    Task<OperatorDto> InfilOperatorAsync(string id);

    /// <summary>
    /// Executes a mission (combat encounter) for the given operator.
    /// Online: delegates to server. Offline: runs locally and persists result.
    /// </summary>
    Task<MissionResultDto> ExecuteMissionAsync(MissionRequest request);

    /// <summary>
    /// Checks whether an operator with the given ID exists.
    /// </summary>
    Task<bool> OperatorExistsAsync(string id);
}
