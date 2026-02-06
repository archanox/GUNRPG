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
    /// Defaults to "combat_sessions.db" in the application directory.
    /// </summary>
    public string LiteDbConnectionString { get; set; } = "combat_sessions.db";
}
