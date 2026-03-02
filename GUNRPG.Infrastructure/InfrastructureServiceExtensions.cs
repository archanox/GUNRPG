using System.Text.Json;
using GUNRPG.Application.Backend;
using GUNRPG.Application.Operators;
using GUNRPG.Application.Sessions;
using GUNRPG.Infrastructure.Backend;
using GUNRPG.Infrastructure.Distributed;
using GUNRPG.Infrastructure.Persistence;
using LiteDB;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nethermind.Libp2p;
using Nethermind.Libp2p.Core;
using Nethermind.Libp2p.Core.Discovery;
using Nethermind.Libp2p.Protocols;

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
            services.AddSingleton<IOfflineSyncHeadStore, InMemoryOfflineSyncHeadStore>();
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
            services.AddSingleton<IOfflineSyncHeadStore, LiteDbOfflineSyncHeadStore>();
            services.AddSingleton<OperatorExfilService>();
        }

        return services;
    }

    /// <summary>
    /// Registers offline persistence and backend resolver services used by the game backend.
    /// </summary>
    /// <remarks>
    /// This method registers <see cref="OfflineStore"/> and <see cref="GameBackendResolver"/> for
    /// client-side DI container usage. It does not register <see cref="IGameBackend"/>,
    /// concrete backend implementations such as <see cref="OnlineGameBackend"/>, or an
    /// <see cref="HttpClient"/>; those must be configured by the hosting application.
    /// <para>
    /// Note: This should only be used in client contexts (e.g., console client) where a separate
    /// offline LiteDB file is configured. Do not use in the API host — it would mix offline
    /// snapshots into the server's persistence store.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddGameBackend(this IServiceCollection services, string offlineDbPath)
    {
        var directory = Path.GetDirectoryName(offlineDbPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        services.AddSingleton<LiteDatabase>(sp => new LiteDatabase(offlineDbPath));
        services.AddSingleton<OfflineStore>(sp => new OfflineStore(sp.GetRequiredService<LiteDatabase>()));
        services.AddSingleton<GameBackendResolver>();

        return services;
    }

    /// <summary>
    /// Creates the offline services needed by the console client.
    /// Centralizes construction of OfflineStore, GameBackendResolver, and IGameBackend
    /// to avoid scattered new() instantiation in UI code.
    /// </summary>
    /// <param name="httpClient">HTTP client for API communication.</param>
    /// <param name="offlineDbPath">Path to the offline LiteDB file.</param>
    /// <param name="jsonOptions">JSON serializer options.</param>
    /// <returns>Tuple of (offlineDb, offlineStore, backendResolver).</returns>
    public static (LiteDatabase offlineDb, OfflineStore offlineStore, GameBackendResolver backendResolver) CreateConsoleServices(
        HttpClient httpClient,
        string offlineDbPath,
        JsonSerializerOptions? jsonOptions = null)
    {
        var directory = Path.GetDirectoryName(offlineDbPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        var offlineDb = new LiteDatabase(offlineDbPath);
        var offlineStore = new OfflineStore(offlineDb);
        var resolver = new GameBackendResolver(httpClient, offlineStore, jsonOptions);
        return (offlineDb, offlineStore, resolver);
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

    /// <summary>
    /// Registers the libp2p peer service for server-to-server operator event replication.
    /// <para>
    /// Starts a libp2p peer that listens for connections from other GUNRPG servers and uses
    /// mDNS to discover peers on the local network. When peers connect, the
    /// <see cref="Libp2pLockstepTransport"/> handles the session and fires
    /// <c>OnPeerConnected</c>, which causes the <c>OperatorEventReplicator</c> to synchronise
    /// operator events so that operators created on any server are visible from all others.
    /// </para>
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="transport">The already-registered lockstep transport singleton.</param>
    /// <param name="nodeId">The server's persistent node ID (used to derive a stable libp2p identity).</param>
    public static IServiceCollection AddLibp2pPeer(
        this IServiceCollection services,
        Libp2pLockstepTransport transport,
        Guid nodeId)
    {
        // Register libp2p infrastructure with our custom lockstep protocol.
        services.AddLibp2p(b => b.AddProtocol(transport, true));

        // Register the hosted service that manages the peer lifecycle.
        services.AddSingleton(sp => new LibP2pPeerService(
            nodeId,
            transport,
            sp.GetRequiredService<IPeerFactory>(),
            sp.GetRequiredService<PeerStore>(),
            sp.GetRequiredService<MDnsDiscoveryProtocol>(),
            sp.GetRequiredService<ILogger<LibP2pPeerService>>()));
        services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<LibP2pPeerService>());

        return services;
    }
}
