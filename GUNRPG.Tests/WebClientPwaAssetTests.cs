using System.Text.Json;

namespace GUNRPG.Tests;

public sealed class WebClientPwaAssetTests
{
    [Fact]
    public void Manifest_UsesRelativeStartUrlAndScope_ForSubdirectoryHosting()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(GetWebClientAssetPath("manifest.webmanifest")));

        Assert.Equal("./", manifest.RootElement.GetProperty("start_url").GetString());
        Assert.Equal("./", manifest.RootElement.GetProperty("scope").GetString());
    }

    [Fact]
    public void IndexHtml_RegistersServiceWorkerRelativeToAppBase()
    {
        var indexHtml = File.ReadAllText(GetWebClientAssetPath("index.html"));

        Assert.Contains("navigator.serviceWorker.register('service-worker.js')", indexHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("navigator.serviceWorker.register('/service-worker.js')", indexHtml, StringComparison.Ordinal);
    }

    private static string GetWebClientAssetPath(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new DirectoryNotFoundException("Could not locate the repository root from the test output directory.");
        }

        return Path.Combine(directory.FullName, "GUNRPG.WebClient", "wwwroot", fileName);
    }
}
