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
    /// Loads a session snapshot by ID.
    /// </summary>
    /// <remarks>
    /// All implementations must return <c>null</c> when a session with the given <paramref name="id"/> is not found.
    /// Implementations that perform integrity or replay validation (typically server-side or persistent stores)
    /// should additionally return <c>null</c> when a completed session's stored
    /// <see cref="CombatSessionSnapshot.FinalHash"/> does not match the hash recomputed from the replayed
    /// simulation state (integrity violation), or when replay fails during validation.
    /// Lightweight or client-side implementations that do not perform such validation may only return <c>null</c>
    /// to indicate that the session does not exist.
    /// </remarks>
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
