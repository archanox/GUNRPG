using GUNRPG.Application.Sessions;
using LiteDB;
using LiteDB.Migration;

namespace GUNRPG.Infrastructure.Persistence;

/// <summary>
/// Manages database schema migrations for LiteDB.
/// Handles versioning and upgrading of combat session snapshots.
/// </summary>
public static class LiteDbMigrations
{
    /// <summary>
    /// Current schema version of the combat session snapshot.
    /// Increment this when making breaking changes to the snapshot structure.
    /// </summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>
    /// Applies all necessary migrations to the database.
    /// Should be called during application startup before any data access.
    /// </summary>
    /// <param name="database">The LiteDB database instance</param>
    public static void ApplyMigrations(LiteDatabase database)
    {
        // Currently on version 1 with no migrations needed
        // When schema changes are needed, add migrations here using LiteDB.Migration
        
        // Example migration setup for future use:
        // var migrations = new MigrationContainer(config =>
        // {
        //     config.Collection<CombatSessionSnapshot>("combat_sessions", collectionConfig =>
        //     {
        //         collectionConfig
        //             .StartWithModel<OldCombatSessionSnapshot>()
        //             .WithMigration(old => new CurrentCombatSessionSnapshot
        //             {
        //                 Id = old.Id,
        //                 // ... map properties
        //                 NewProperty = "default"
        //             })
        //             .UseLatestVersion();
        //     });
        // });
        // migrations.Apply(database);
        
        // No migrations to apply yet - we're on the initial schema version
    }

    /// <summary>
    /// Gets the current schema version stored in the database metadata.
    /// Returns 0 if no version is set (new database).
    /// </summary>
    public static int GetDatabaseSchemaVersion(LiteDatabase database)
    {
        var metadata = database.GetCollection("_metadata");
        var versionDoc = metadata.FindById("schema_version");
        
        if (versionDoc == null)
        {
            return 0;
        }

        return versionDoc["version"].AsInt32;
    }

    /// <summary>
    /// Sets the schema version in the database metadata.
    /// Used to track which migrations have been applied.
    /// </summary>
    public static void SetDatabaseSchemaVersion(LiteDatabase database, int version)
    {
        var metadata = database.GetCollection("_metadata");
        var versionDoc = new BsonDocument
        {
            ["_id"] = "schema_version",
            ["version"] = version,
            ["updated_at"] = DateTimeOffset.UtcNow.ToString("O")
        };
        metadata.Upsert(versionDoc);
    }
}
