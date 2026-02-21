namespace GUNRPG.Application.Backend;

/// <summary>
/// Abstraction for game backend operations.
/// Online implementation uses HTTP API; offline implementation uses local LiteDB.
/// Combat remains interactive and player-driven in both modes — this interface
/// handles operator data access, not gameplay execution.
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
    /// Checks whether an operator with the given ID exists.
    /// </summary>
    Task<bool> OperatorExistsAsync(string id);
}
