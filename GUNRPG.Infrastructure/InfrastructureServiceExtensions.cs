using GUNRPG.Application.Sessions;
using GUNRPG.Infrastructure.Persistence;
using LiteDB;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
        // Bind storage configuration
        services.Configure<StorageOptions>(
            configuration.GetSection(StorageOptions.SectionName));

        var storageOptions = new StorageOptions();
        configuration.GetSection(StorageOptions.SectionName).Bind(storageOptions);

        if (storageOptions.Provider.Equals("InMemory", StringComparison.OrdinalIgnoreCase))
        {
            // In-memory store for testing
            services.AddSingleton<ICombatSessionStore, InMemoryCombatSessionStore>();
        }
        else
        {
            // LiteDB store (default)
            services.AddSingleton<LiteDatabase>(sp =>
            {
                var options = sp.GetRequiredService<IOptions<StorageOptions>>().Value;
                var database = new LiteDatabase(options.LiteDbConnectionString);
                
                // Configure LiteDB mapper for snapshot types
                ConfigureLiteDbMapper(database.Mapper);
                
                // Apply any pending migrations
                LiteDbMigrations.ApplyMigrations(database);
                
                // Update schema version
                LiteDbMigrations.SetDatabaseSchemaVersion(database, LiteDbMigrations.CurrentSchemaVersion);
                
                return database;
            });

            services.AddSingleton<ICombatSessionStore, LiteDbCombatSessionStore>();
        }

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
