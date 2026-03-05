using System.Net;
using System.Text;
using System.Text.Json;
using GUNRPG.ConsoleClient.Identity;

namespace GUNRPG.Tests;

public sealed class TokenStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly TokenStore _store;

    public TokenStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"gunrpg-test-{Guid.NewGuid():N}");
        _store = new TokenStore(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task SaveAndLoad_RoundTrips_RefreshTokenAndNodeUrl()
    {
        await _store.SaveAsync("rt-abc", "https://node.example.com");

        var loaded = await _store.LoadAsync();

        Assert.NotNull(loaded);
        Assert.Equal("rt-abc", loaded.RefreshToken);
        Assert.Equal("https://node.example.com", loaded.NodeUrl);
    }

    [Fact]
    public async Task LoadAsync_ReturnsNull_WhenFileAbsent()
    {
        var result = await _store.LoadAsync();
        Assert.Null(result);
    }

    [Fact]
    public async Task LoadAsync_ReturnsNull_WhenFileCorrupt()
    {
        var filePath = Path.Combine(_tempDir, "auth.json");
        await File.WriteAllTextAsync(filePath, "not valid json {{{{");

        var result = await _store.LoadAsync();

        Assert.Null(result);
    }

    [Fact]
    public async Task Clear_RemovesFile()
    {
        await _store.SaveAsync("rt-xyz", "https://node.example.com");
        _store.Clear();

        var result = await _store.LoadAsync();
        Assert.Null(result);
    }

    [Fact]
    public async Task SaveAsync_DoesNotPersistAccessToken()
    {
        await _store.SaveAsync("my-refresh-token", "https://node.example.com");

        var filePath = Path.Combine(_tempDir, "auth.json");
        var rawJson = await File.ReadAllTextAsync(filePath);

        // File should contain only refreshToken + nodeUrl — no accessToken field.
        using var doc = JsonDocument.Parse(rawJson);
        var props = doc.RootElement.EnumerateObject().Select(p => p.Name).ToList();
        Assert.Contains("refreshToken", props);
        Assert.Contains("nodeUrl", props);
        Assert.DoesNotContain("accessToken", props);
    }

    [Fact]
    public async Task SaveAsync_Overwrites_PreviousToken()
    {
        await _store.SaveAsync("first-refresh", "https://node1.example.com");
        await _store.SaveAsync("second-refresh", "https://node2.example.com");

        var loaded = await _store.LoadAsync();
        Assert.NotNull(loaded);
        Assert.Equal("second-refresh", loaded.RefreshToken);
        Assert.Equal("https://node2.example.com", loaded.NodeUrl);
    }
}
