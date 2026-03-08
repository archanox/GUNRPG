using GUNRPG.Infrastructure;
using GUNRPG.Infrastructure.Persistence;
using LiteDB;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GUNRPG.Tests;

public class InfrastructureServiceExtensionsTests : IDisposable
{
    private readonly string _tempDbPath;
    private readonly ServiceProvider _provider;

    public InfrastructureServiceExtensionsTests()
    {
        _tempDbPath = Path.Combine(
            "~/.gunrpg-tests",
            Guid.NewGuid().ToString("N"),
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

        var directory = Path.GetDirectoryName(ExpandHomePath(_tempDbPath));
        Assert.False(string.IsNullOrEmpty(directory));
        Assert.True(Directory.Exists(directory));
    }

    public void Dispose()
    {
        _provider.Dispose();

        var expandedPath = ExpandHomePath(_tempDbPath);

        if (File.Exists(expandedPath))
            File.Delete(expandedPath);

        var directory = Path.GetDirectoryName(expandedPath);
        if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string ExpandHomePath(string path)
    {
        if (path.StartsWith("~/", StringComparison.Ordinal))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, path[2..]);
        }
        if (path == "~")
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return path;
    }
}
