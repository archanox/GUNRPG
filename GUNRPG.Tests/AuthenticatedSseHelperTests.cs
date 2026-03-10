using GUNRPG.WebClient.Helpers;

namespace GUNRPG.Tests;

public sealed class AuthenticatedSseHelperTests
{
    [Fact]
    public void TryGetHttpsBaseUrl_ReturnsTrue_ForHttpsUrl()
    {
        var result = AuthenticatedSseHelper.TryGetHttpsBaseUrl("https://node.example.com/api", out var baseUrl);

        Assert.True(result);
        Assert.Equal("https://node.example.com/api", baseUrl);
    }

    [Fact]
    public void TryGetHttpsBaseUrl_ReturnsFalse_ForHttpUrl()
    {
        var result = AuthenticatedSseHelper.TryGetHttpsBaseUrl("http://lan-host:5209", out var baseUrl);

        Assert.False(result);
        Assert.Null(baseUrl);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("/relative/path")]
    [InlineData("not-a-url")]
    public void TryGetHttpsBaseUrl_ReturnsFalse_ForInvalidInput(string? input)
    {
        var result = AuthenticatedSseHelper.TryGetHttpsBaseUrl(input, out var baseUrl);

        Assert.False(result);
        Assert.Null(baseUrl);
    }
}
