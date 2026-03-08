using GUNRPG.Infrastructure;
using GUNRPG.Infrastructure.Persistence;
using LiteDB;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GUNRPG.Tests;

public class InfrastructureServiceExtensionsTests : IDisposable
{
    private readonly string _tempDbPath;
    private readonly string _rootDir;
    private readonly ServiceProvider _provider;

    public InfrastructureServiceExtensionsTests()
    {
        _rootDir = Path.Combine(
            "~/.gunrpg-tests",
            Guid.NewGuid().ToString("N"));

        _tempDbPath = Path.Combine(
            _rootDir,
            "data",
            "combat_sessions.db");

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{StorageOptions.SectionName}:{nameof(StorageOptions.Provider)}"] = "LiteDB",
                [$"{StorageOptions.SectionName}:{nameof(StorageOptions.LiteDbConnectionString)}"] = _tempDbPath
            })
            .Build();

        var services = new ServiceCollection();
        services.AddCombatSessionStore(configuration);

        _provider = services.BuildServiceProvider();
    }

    [Fact]
    public void AddCombatSessionStore_CreatesLiteDbDirectory()
    {
        // Resolve LiteDatabase to trigger directory creation and ensure the instance is usable.
        var database = _provider.GetRequiredService<LiteDatabase>();
        Assert.NotNull(database);

        var directory = Path.GetDirectoryName(PathHelpers.ExpandHomePath(_tempDbPath));
        Assert.False(string.IsNullOrEmpty(directory));
        Assert.True(Directory.Exists(directory));
    }

    public void Dispose()
    {
        _provider.Dispose();

        var expandedRoot = PathHelpers.ExpandHomePath(_rootDir);
        var expandedPath = PathHelpers.ExpandHomePath(_tempDbPath);

        if (File.Exists(expandedPath))
            File.Delete(expandedPath);

        if (!string.IsNullOrEmpty(expandedRoot) && Directory.Exists(expandedRoot))
        {
            Directory.Delete(expandedRoot, recursive: true);
        }
    }
}
