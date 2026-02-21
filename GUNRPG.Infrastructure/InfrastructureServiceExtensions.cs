using GUNRPG.Application.Backend;
using GUNRPG.Application.Operators;
using GUNRPG.Application.Sessions;
using GUNRPG.Infrastructure.Backend;
using GUNRPG.Infrastructure.Persistence;
using LiteDB;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace GUNRPG.Infrastructure;

/// <summary>
/// Extension methods for registering infrastructure services with dependency injection.
/// </summary>
public static class InfrastructureServiceExtensions
{
    /// <summary>
    /// Registers the combat session store based on configuration.
    /// Defaults to LiteDB. Can be overridden to InMemory via configuration.
    /// </summary>
    public static IServiceCollection AddCombatSessionStore(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Bind storage configuration for IOptions<StorageOptions>
        services.Configure<StorageOptions>(
            configuration.GetSection(StorageOptions.SectionName));

        // Read provider directly from configuration to decide which store to register
        var provider = configuration
            .GetSection(StorageOptions.SectionName)
            .GetValue<string>(nameof(StorageOptions.Provider));

        if (string.Equals(provider, "InMemory", StringComparison.OrdinalIgnoreCase))
        {
            // In-memory store for testing
            services.AddSingleton<ICombatSessionStore, InMemoryCombatSessionStore>();
            // Register a no-op operator event store so CombatSessionService can be resolved
            // Validation will be skipped when the store is present but returns no events
            services.AddSingleton<IOperatorEventStore, InMemoryOperatorEventStore>();
        }
        else
        {
            // LiteDB store (default)
            services.AddSingleton<LiteDatabase>(sp =>
            {
                var options = sp.GetRequiredService<IOptions<StorageOptions>>().Value;
                
                // Create custom BsonMapper to avoid global state issues
                var mapper = new BsonMapper();
                ConfigureLiteDbMapper(mapper);
                
                var database = new LiteDatabase(options.LiteDbConnectionString, mapper);
                
                // Check current schema version before applying migrations
                var currentVersion = LiteDbMigrations.GetDatabaseSchemaVersion(database);
                if (currentVersion < LiteDbMigrations.CurrentSchemaVersion)
                {
                    // Apply any pending migrations and update schema version
                    LiteDbMigrations.ApplyMigrations(database);
                    LiteDbMigrations.SetDatabaseSchemaVersion(database, LiteDbMigrations.CurrentSchemaVersion);
                }
                
                // Register disposal on application shutdown if available
                // LiteDatabase implements IDisposable, so it will be disposed by DI container
                // This registration ensures it happens during graceful shutdown
                var lifetime = sp.GetService<IHostApplicationLifetime>();
                if (lifetime != null)
                {
                    lifetime.ApplicationStopping.Register(() =>
                    {
                        try
                        {
                            database.Dispose();
                        }
                        catch
                        {
                            // Ignore disposal errors during shutdown
                        }
                    });
                }
                
                return database;
            });

            services.AddSingleton<ICombatSessionStore, LiteDbCombatSessionStore>();
            services.AddSingleton<IOperatorEventStore, LiteDbOperatorEventStore>();
            services.AddSingleton<OperatorExfilService>();
        }

        return services;
    }

    /// <summary>
    /// Registers the game backend abstraction with mode resolution logic.
    /// Resolution: server reachable → OnlineGameBackend; else if infilled operator → OfflineGameBackend;
    /// else → OnlineGameBackend (gameplay blocked).
    /// </summary>
    public static IServiceCollection AddGameBackend(this IServiceCollection services)
    {
        services.AddSingleton<OfflineStore>(sp =>
        {
            var db = sp.GetRequiredService<LiteDatabase>();
            return new OfflineStore(db);
        });

        services.AddSingleton<GameBackendResolver>();

        return services;
    }

    /// <summary>
    /// Configures LiteDB mapper for snapshot types.
    /// Ensures proper serialization of enums and nested objects.
    /// </summary>
    private static void ConfigureLiteDbMapper(BsonMapper mapper)
    {
        // LiteDB handles most types automatically, including:
        // - Primitives (int, float, long, bool, etc.)
        // - Strings
        // - DateTime/DateTimeOffset
        // - Guid
        // - Enums (serialized as strings by default)
        // - Nested objects (auto-mapped)
        // - Nullable types
        
        // Explicitly configure enum serialization to use strings for readability
        mapper.EnumAsInteger = false;
        
        // Use Id property as the document key
        mapper.Entity<CombatSessionSnapshot>()
            .Id(x => x.Id);
    }
}
