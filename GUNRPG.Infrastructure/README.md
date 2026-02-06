# GUNRPG.Infrastructure

Infrastructure layer for GUNRPG, providing concrete implementations of persistence and external service abstractions.

## Overview

This project contains implementation details for storage and external integrations that are abstracted in the Application layer. It follows Clean Architecture principles by keeping infrastructure concerns separate from domain and application logic.

## Components

### Persistence

#### LiteDbCombatSessionStore

Embedded document database implementation of `ICombatSessionStore` using LiteDB.

**Features:**
- Persist combat session snapshots to disk
- Support for save, load, update, delete, and list operations
- Thread-safe for concurrent requests
- Automatic enum serialization to strings for readability
- No annotations required on domain objects
- Schema migration support via LiteDB.Migration

#### LiteDbMigrations

Manages database schema migrations using the [LiteDB.Migration](https://github.com/JKamsker/LiteDB.Migration) library.

**Features:**
- Automatic migration application on startup
- Schema version tracking
- Forward-only migrations
- Supports complex data transformations

**Current Schema Version:** 1 (initial baseline)

**Adding Migrations:**

When the snapshot schema evolves, add migrations in `LiteDbMigrations.ApplyMigrations`:

```csharp
var migrations = new MigrationContainer(config =>
{
    config.Collection<CombatSessionSnapshotV2>("combat_sessions", collectionConfig =>
    {
        collectionConfig
            .StartWithModel<CombatSessionSnapshotV1>()
            .WithMigration(v1 => new CombatSessionSnapshotV2
            {
                Id = v1.Id,
                // ... map existing properties
                NewProperty = "default-value" // Add new property
            })
            .UseLatestVersion();
    });
});
migrations.Apply(database);
```

Update `CurrentSchemaVersion` constant after adding migrations.

**Configuration:**

```json
{
  "Storage": {
    "Provider": "LiteDB",
    "LiteDbConnectionString": "combat_sessions.db"
  }
}
```

**Usage:**

The store is automatically registered via the `AddCombatSessionStore` extension method:

```csharp
builder.Services.AddCombatSessionStore(builder.Configuration);
```

To switch to in-memory storage for testing:

```json
{
  "Storage": {
    "Provider": "InMemory"
  }
}
```

## Design Principles

1. **Separation of Concerns**: LiteDB types never leak into Core or Application layers
2. **Configuration-Driven**: Store selection via appsettings.json
3. **Dependency Injection**: Singleton lifetime for database connection
4. **Clean Architecture**: Infrastructure depends on Application, never the reverse
5. **Thread Safety**: All operations are safe for concurrent use

## Dependencies

- `LiteDB` (5.0.21): Embedded NoSQL document database
- `LiteDB.Migration` (0.0.10): Schema migration framework for LiteDB
- `Microsoft.Extensions.Configuration.Abstractions`: Configuration support
- `Microsoft.Extensions.DependencyInjection.Abstractions`: DI support
- `Microsoft.Extensions.Options.ConfigurationExtensions`: Options pattern support

## Testing

Tests are located in `GUNRPG.Tests/LiteDbCombatSessionStoreTests.cs` and verify:
- Basic CRUD operations
- Nested object serialization
- Enum handling
- Concurrent access
- Update semantics

## Future Enhancements

Potential future additions to this project:
- Database migration utilities
- Backup and restore tools
- Session replay functionality
- Export/import capabilities
- Alternative storage providers (SQL, cloud storage)
