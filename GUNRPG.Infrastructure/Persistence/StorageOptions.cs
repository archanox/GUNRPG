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
    /// Defaults to "~/.gunrpg/combat_sessions.db" in the user's home directory.
    /// Tilde (~) is expanded to the user's home directory.
    /// The parent directory will be created automatically if it doesn't exist.
    ///
    /// Example values:
    /// - "~/.gunrpg/combat_sessions.db" (user's home directory - recommended)
    /// - "/var/lib/gunrpg/combat_sessions.db" (absolute path on Linux)
    /// - "C:\\ProgramData\\GUNRPG\\combat_sessions.db" (absolute path on Windows)
    /// </summary>
    public string LiteDbConnectionString { get; set; } = "~/.gunrpg/combat_sessions.db";
}
