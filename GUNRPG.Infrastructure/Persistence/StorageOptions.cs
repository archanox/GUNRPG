namespace GUNRPG.Infrastructure.Persistence;

/// <summary>
/// Configuration settings for combat session storage.
/// </summary>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>
    /// Storage provider to use: "InMemory" or "LiteDB"
    /// </summary>
    public string Provider { get; set; } = "LiteDB";

    /// <summary>
    /// Connection string for LiteDB (file path or connection string).
    /// Defaults to "combat_sessions.db" in the application's working directory.
    /// 
    /// For production deployments, consider using an absolute path or a path
    /// relative to a well-defined location (e.g., /var/lib/gunrpg/combat_sessions.db).
    /// 
    /// Example values:
    /// - "combat_sessions.db" (relative to working directory)
    /// - "/var/lib/gunrpg/combat_sessions.db" (absolute path on Linux)
    /// - "C:\\ProgramData\\GUNRPG\\combat_sessions.db" (absolute path on Windows)
    /// </summary>
    public string LiteDbConnectionString { get; set; } = "combat_sessions.db";
}
