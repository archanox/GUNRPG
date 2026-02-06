namespace GUNRPG.Application.Sessions;

/// <summary>
/// Abstraction for storing and retrieving combat sessions.
/// Implementations should provide key-value semantics with snapshot persistence.
/// This abstraction is designed to support both in-memory and persistent storage (e.g., LiteDB).
/// </summary>
public interface ICombatSessionStore
{
    /// <summary>
    /// Saves a session snapshot. Creates if new, updates if exists.
    /// </summary>
    Task SaveAsync(CombatSessionSnapshot snapshot);

    /// <summary>
    /// Loads a session snapshot by ID. Returns null if not found.
    /// </summary>
    Task<CombatSessionSnapshot?> LoadAsync(Guid id);

    /// <summary>
    /// Deletes a session by ID if it exists.
    /// </summary>
    Task DeleteAsync(Guid id);

    /// <summary>
    /// Lists all session snapshots. For production use, consider adding pagination.
    /// </summary>
    Task<IReadOnlyCollection<CombatSessionSnapshot>> ListAsync();
}
