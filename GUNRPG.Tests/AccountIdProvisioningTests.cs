using System.Net;
using System.Text;
using JsonSerializer = System.Text.Json.JsonSerializer;
using GUNRPG.Infrastructure.Identity;
using GUNRPG.WebClient.Services;
using LiteDB;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;

namespace GUNRPG.Tests;

public sealed class AccountIdProvisioningTests : IDisposable
{
    private readonly LiteDatabase _db = new(":memory:");

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task EnsureAssignedAsync_AssignsAndPersistsAccountId_WhenMissing()
    {
        using var userManager = CreateUserManager();
        var user = new ApplicationUser
        {
            Id = "user-1",
            UserName = "alice",
            NormalizedUserName = "ALICE",
        };

        var createResult = await userManager.CreateAsync(user);
        Assert.True(createResult.Succeeded);
        Assert.Null(user.AccountId);

        var result = await AccountIdProvisioning.EnsureAssignedAsync(userManager, user);

        Assert.True(result.Succeeded);
        Assert.NotNull(user.AccountId);
        Assert.NotEqual(Guid.Empty, user.AccountId.Value);

        var persisted = await userManager.FindByIdAsync(user.Id);
        Assert.Equal(user.AccountId, persisted?.AccountId);
    }

    [Fact]
    public async Task EnsureAssignedAsync_PreservesExistingAccountId()
    {
        using var userManager = CreateUserManager();
        var existingAccountId = Guid.NewGuid();
        var user = new ApplicationUser
        {
            Id = "user-2",
            UserName = "bob",
            NormalizedUserName = "BOB",
            AccountId = existingAccountId,
        };

        var createResult = await userManager.CreateAsync(user);
        Assert.True(createResult.Succeeded);

        var result = await AccountIdProvisioning.EnsureAssignedAsync(userManager, user);

        Assert.True(result.Succeeded);
        Assert.Equal(existingAccountId, user.AccountId);
    }

    private UserManager<ApplicationUser> CreateUserManager()
    {
        var store = new LiteDbUserStore(_db);
        var services = new ServiceCollection()
            .AddLogging()
            .BuildServiceProvider();

        return new UserManager<ApplicationUser>(
            store,
            Options.Create(new IdentityOptions()),
            new PasswordHasher<ApplicationUser>(),
            [new UserValidator<ApplicationUser>()],
            [],
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            services,
            services.GetRequiredService<ILogger<UserManager<ApplicationUser>>>());
    }
}

public sealed class ApiClientTests
{
    [Fact]
    public async Task PostAsync_RetriesUnauthorizedRequestWithFreshJsonContent()
    {
        var handler = new QueueHandler();
        var refreshed = new TokenResponse
        {
            AccessToken = "new-access",
            RefreshToken = "new-refresh",
            AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15).ToString("O"),
            RefreshTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(7).ToString("O"),
        };

        handler.Enqueue(HttpStatusCode.Unauthorized);
        handler.Enqueue(HttpStatusCode.OK, JsonSerializer.Serialize(refreshed));
        handler.Enqueue(HttpStatusCode.Created, "{}");

        using var http = new HttpClient(handler);
        var js = new FakeJsRuntime();
        var nodeService = new NodeConnectionService(js);
        await nodeService.SetBaseUrlAsync("https://node.example.com");
        var auth = new AuthService(js, http, nodeService);
        await auth.SetTokensAsync("old-access", "refresh-token");
        var client = new ApiClient(http, nodeService, auth);

        var response = await client.PostAsync("/operators", new { Name = "Viper" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(3, handler.Calls.Count);
        Assert.Equal("Bearer old-access", handler.Calls[0].Authorization);
        Assert.Equal("Bearer new-access", handler.Calls[2].Authorization);
        Assert.Equal(handler.Calls[0].Body, handler.Calls[2].Body);
        Assert.Contains("\"name\":\"Viper\"", handler.Calls[0].Body, StringComparison.Ordinal);
    }

    private sealed class QueueHandler : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode StatusCode, string? Body)> _responses = new();

        public List<(string? Uri, string? Authorization, string? Body)> Calls { get; } = [];

        public void Enqueue(HttpStatusCode statusCode, string? body = null)
            => _responses.Enqueue((statusCode, body));

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls.Add((
                request.RequestUri?.ToString(),
                request.Headers.Authorization?.ToString(),
                request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));

            var response = _responses.Dequeue();
            var message = new HttpResponseMessage(response.StatusCode);
            if (response.Body is not null)
                message.Content = new StringContent(response.Body, Encoding.UTF8, "application/json");

            return message;
        }
    }

    private sealed class FakeJsRuntime : IJSRuntime
    {
        private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            return InvokeCoreAsync<TValue>(identifier, args);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
            => InvokeCoreAsync<TValue>(identifier, args);

        private ValueTask<TValue> InvokeCoreAsync<TValue>(string identifier, object?[]? args)
        {
            switch (identifier)
            {
                case "localStorage.getItem":
                    return new ValueTask<TValue>((TValue?)GetValue(args, 0) ?? default!);
                case "tokenStorage.getRefreshToken":
                    return new ValueTask<TValue>((TValue?)GetValue("refreshToken") ?? default!);
                case "localStorage.setItem":
                    _values[Convert.ToString(args?[0])!] = args?[1];
                    return new ValueTask<TValue>(default(TValue)!);
                case "localStorage.removeItem":
                    _values.Remove(Convert.ToString(args?[0])!);
                    return new ValueTask<TValue>(default(TValue)!);
                case "tokenStorage.storeRefreshToken":
                    _values["refreshToken"] = args?[0];
                    return new ValueTask<TValue>(default(TValue)!);
                case "tokenStorage.removeRefreshToken":
                    _values.Remove("refreshToken");
                    return new ValueTask<TValue>(default(TValue)!);
                default:
                    throw new NotSupportedException(identifier);
            }
        }

        private object? GetValue(object?[]? args, int keyIndex)
        {
            var key = Convert.ToString(args?[keyIndex]);
            return key is not null && _values.TryGetValue(key, out var value)
                ? value
                : null;
        }

        private object? GetValue(string key) =>
            _values.TryGetValue(key, out var value) ? value : null;
    }
}
