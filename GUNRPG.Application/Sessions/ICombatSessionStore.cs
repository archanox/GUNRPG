namespace GUNRPG.Application.Sessions;

/// <summary>
/// Abstraction for storing and retrieving combat sessions.
/// Implementations should provide key-value semantics with snapshot persistence.
/// This abstraction is designed to support both in-memory and persistent storage (e.g., LiteDB).
/// </summary>
public interface ICombatSessionStore
{
    /// <summary>
    /// Creates a new session. Throws if a session with the same ID already exists.
    /// </summary>
    CombatSessionSnapshot Create(CombatSessionSnapshot snapshot);

    /// <summary>
    /// Retrieves a session by ID. Returns null if not found.
    /// </summary>
    CombatSessionSnapshot? Get(Guid id);

    /// <summary>
    /// Updates an existing session or inserts if it doesn't exist.
    /// This should perform a full snapshot replacement.
    /// </summary>
    void Upsert(CombatSessionSnapshot snapshot);

    /// <summary>
    /// Lists all sessions. For production use, consider adding pagination.
    /// </summary>
    IReadOnlyCollection<CombatSessionSnapshot> List();
}
