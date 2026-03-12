using GUNRPG.Application.Backend;
using GUNRPG.ClientModels;
using GUNRPG.WebClient.Helpers;
using GUNRPG.WebClient.Services;
using Microsoft.JSInterop;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace GUNRPG.Tests;

public sealed class WebOfflineSupportTests
{
    private static readonly Guid SyncOperatorId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task BrowserOfflineStore_SaveInfiledOperatorAsync_DeactivatesPreviousActiveSnapshot()
    {
        var js = new FakeBrowserJsRuntime();
        var store = new BrowserOfflineStore(js);

        await store.SaveInfiledOperatorAsync(CreateOperator(Guid.Parse("11111111-1111-1111-1111-111111111111"), "Alpha"));
        await store.SaveInfiledOperatorAsync(CreateOperator(Guid.Parse("22222222-2222-2222-2222-222222222222"), "Bravo"));

        var first = await store.GetInfiledOperatorAsync(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var active = await store.GetActiveInfiledOperatorAsync();

        Assert.NotNull(first);
        Assert.NotNull(active);
        Assert.Equal("Alpha", first.Name);
        Assert.Equal("Bravo", active.Name);
        Assert.Equal(Guid.Parse("22222222-2222-2222-2222-222222222222"), active.Id);
    }

    [Fact]
    public async Task BrowserOfflineStore_SaveMissionResultAsync_RejectsBrokenHashChain()
    {
        var js = new FakeBrowserJsRuntime();
        var store = new BrowserOfflineStore(js);

        await store.SaveMissionResultAsync(new OfflineMissionEnvelope
        {
            OperatorId = "op-1",
            SequenceNumber = 1,
            RandomSeed = 1,
            InitialOperatorStateHash = "h0",
            ResultOperatorStateHash = "h1",
            ExecutedUtc = DateTime.UtcNow,
            FullBattleLog = []
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveMissionResultAsync(new OfflineMissionEnvelope
        {
            OperatorId = "op-1",
            SequenceNumber = 2,
            RandomSeed = 2,
            InitialOperatorStateHash = "wrong",
            ResultOperatorStateHash = "h2",
            ExecutedUtc = DateTime.UtcNow,
            FullBattleLog = []
        }));
    }

    [Fact]
    public async Task AuthService_TryRestoreAsync_UsesStoredAccessTokenWhenOffline()
    {
        var js = new FakeBrowserJsRuntime();
        await js.InvokeVoidAsync("tokenStorage.storeAccessToken", "cached-access");
        await js.InvokeVoidAsync("tokenStorage.storeRefreshToken", "cached-refresh");

        using var http = new HttpClient(new FailingHandler());
        var nodeService = new NodeConnectionService(js);
        await nodeService.SetBaseUrlAsync("https://node.example.com");
        var auth = new AuthService(js, http, nodeService);

        var restored = await auth.TryRestoreAsync();

        Assert.True(restored);
        Assert.True(auth.IsAuthenticated);
        Assert.Equal("cached-access", auth.GetAccessToken());
    }

    [Fact]
    public async Task AuthService_RefreshTokenAsync_ReturnsFalseWhenRefreshCannotRun()
    {
        var js = new FakeBrowserJsRuntime();
        await js.InvokeVoidAsync("tokenStorage.storeAccessToken", "cached-access");

        using var http = new HttpClient(new FailingHandler());
        var nodeService = new NodeConnectionService(js);
        var auth = new AuthService(js, http, nodeService);
        await auth.TryRestoreAsync();

        var refreshed = await auth.RefreshTokenAsync();
        var sseToken = await auth.GetSseAccessTokenAsync(forceRefresh: true);

        Assert.False(refreshed);
        Assert.Null(sseToken);
        Assert.Equal("cached-access", auth.GetAccessToken());
    }

    [Fact]
    public async Task OfflineSyncService_SyncAsync_CoalescesConcurrentRequestsPerOperator()
    {
        var js = new FakeBrowserJsRuntime();
        var store = new BrowserOfflineStore(js);
        await store.SaveMissionResultAsync(new OfflineMissionEnvelope
        {
            Id = "env-1",
            OperatorId = SyncOperatorId.ToString(),
            SequenceNumber = 1,
            RandomSeed = 1,
            InitialOperatorStateHash = "h0",
            ResultOperatorStateHash = "h1",
            ExecutedUtc = DateTime.UtcNow,
            Synced = true,
            FullBattleLog = []
        });
        await store.SaveMissionResultAsync(new OfflineMissionEnvelope
        {
            Id = "env-2",
            OperatorId = SyncOperatorId.ToString(),
            SequenceNumber = 2,
            RandomSeed = 2,
            InitialOperatorStateHash = "h1",
            ResultOperatorStateHash = "h2",
            ExecutedUtc = DateTime.UtcNow,
            FullBattleLog = []
        });

        var handler = new DelayedSyncHandler();
        using var http = new HttpClient(handler);
        var nodeService = new NodeConnectionService(js);
        await nodeService.SetBaseUrlAsync("https://node.example.com");
        var auth = new AuthService(js, http, nodeService);
        var api = new ApiClient(http, nodeService, auth);
        var sync = new OfflineSyncService(api, store);

        var first = sync.SyncAsync(SyncOperatorId);
        var second = sync.SyncAsync(SyncOperatorId);
        var results = await Task.WhenAll(first, second);

        Assert.Equal(1, handler.OfflineSyncPostCount);
        Assert.Empty(await store.GetAllUnsyncedResultsAsync());
        Assert.Equal(1, results[0].EnvelopesSynced);
        Assert.Equal(0, results[1].EnvelopesSynced);
    }

    [Fact]
    public async Task OperatorService_StartCombatSessionAsync_UsesApiWhenOnlineWithoutOfflineSnapshot()
    {
        var js = new FakeBrowserJsRuntime();
        var handler = new StartCombatHandler();
        using var http = new HttpClient(handler);
        var nodeService = new NodeConnectionService(js);
        await nodeService.SetBaseUrlAsync("https://node.example.com");
        var auth = new AuthService(js, http, nodeService);
        var api = new ApiClient(http, nodeService, auth);
        var offlineStore = new BrowserOfflineStore(js);
        var combatStore = new BrowserCombatSessionStore(js);
        var offlineGameplay = new OfflineGameplayService(combatStore, offlineStore);
        var offlineSync = new OfflineSyncService(api, offlineStore);
        var connection = new ConnectionStateService(js);
        var service = new OperatorService(api, offlineStore, offlineGameplay, offlineSync, connection);
        var operatorId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

        var (sessionId, error) = await service.StartCombatSessionAsync(operatorId);

        Assert.Null(error);
        Assert.Equal(handler.SessionId, sessionId);
        Assert.Equal(1, handler.StartCombatCount);
        Assert.Equal($"/operators/{operatorId}/infil/combat", handler.LastRequestPath);
    }

    [Fact]
    public async Task OperatorService_StartCombatSessionAsync_WhenOnlineUpdatesLocalSnapshotWithActiveSession()
    {
        var js = new FakeBrowserJsRuntime();
        var handler = new StartCombatHandler();
        using var http = new HttpClient(handler);
        var nodeService = new NodeConnectionService(js);
        await nodeService.SetBaseUrlAsync("https://node.example.com");
        var auth = new AuthService(js, http, nodeService);
        var api = new ApiClient(http, nodeService, auth);
        var offlineStore = new BrowserOfflineStore(js);
        var combatStore = new BrowserCombatSessionStore(js);
        var offlineGameplay = new OfflineGameplayService(combatStore, offlineStore);
        var offlineSync = new OfflineSyncService(api, offlineStore);
        var connection = new ConnectionStateService(js);
        var service = new OperatorService(api, offlineStore, offlineGameplay, offlineSync, connection);
        var operatorId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

        await offlineStore.SaveInfiledOperatorAsync(CreateOperator(operatorId, "Bravo"));

        var (sessionId, error) = await service.StartCombatSessionAsync(operatorId);
        var updatedOperator = await offlineStore.GetInfiledOperatorAsync(operatorId);

        Assert.Null(error);
        Assert.Equal(handler.SessionId, sessionId);
        Assert.NotNull(updatedOperator);
        Assert.Equal(handler.SessionId, updatedOperator.ActiveCombatSessionId);
    }

    [Fact]
    public async Task OperatorService_StartCombatSessionAsync_UsesApiWhenOnlineAndStorageThrows()
    {
        var js = new FakeBrowserJsRuntime(throwOnGunRpgStorage: true);
        var handler = new StartCombatHandler();
        using var http = new HttpClient(handler);
        var nodeService = new NodeConnectionService(js);
        await nodeService.SetBaseUrlAsync("https://node.example.com");
        var auth = new AuthService(js, http, nodeService);
        var api = new ApiClient(http, nodeService, auth);
        var offlineStore = new BrowserOfflineStore(js);
        var combatStore = new BrowserCombatSessionStore(js);
        var offlineGameplay = new OfflineGameplayService(combatStore, offlineStore);
        var offlineSync = new OfflineSyncService(api, offlineStore);
        var connection = new ConnectionStateService(js);
        var service = new OperatorService(api, offlineStore, offlineGameplay, offlineSync, connection);
        var operatorId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

        var (sessionId, error) = await service.StartCombatSessionAsync(operatorId);

        Assert.Null(error);
        Assert.Equal(handler.SessionId, sessionId);
        Assert.Equal(1, handler.StartCombatCount);
        Assert.Equal($"/operators/{operatorId}/infil/combat", handler.LastRequestPath);
    }

    [Fact]
    public async Task OperatorService_GetAsync_WhenInfilTimerExpiredAndOnline_FetchesFromServerAndPurgesStaleSnapshot()
    {
        var operatorId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
        var js = new FakeBrowserJsRuntime();
        var handler = new GetBaseOperatorHandler(operatorId);
        using var http = new HttpClient(handler);
        var nodeService = new NodeConnectionService(js);
        await nodeService.SetBaseUrlAsync("https://node.example.com");
        var auth = new AuthService(js, http, nodeService);
        var api = new ApiClient(http, nodeService, auth);
        var offlineStore = new BrowserOfflineStore(js);
        var combatStore = new BrowserCombatSessionStore(js);
        var offlineGameplay = new OfflineGameplayService(combatStore, offlineStore);
        var offlineSync = new OfflineSyncService(api, offlineStore);
        var connection = new ConnectionStateService(js); // IsOnline defaults to true
        var service = new OperatorService(api, offlineStore, offlineGameplay, offlineSync, connection);

        // Save a stale infil snapshot whose timer lapsed 31 minutes ago.
        await offlineStore.SaveInfiledOperatorAsync(CreateOperator(operatorId, "Delta",
            infilStartTime: DateTimeOffset.UtcNow.AddMinutes(-31)));

        var (data, error) = await service.GetAsync(operatorId);
        var remaining = await offlineStore.GetInfiledOperatorAsync(operatorId);

        Assert.Null(error);
        Assert.NotNull(data);
        Assert.Equal("Base", data.CurrentMode);    // Server state returned, not stale cache
        Assert.Equal(1, handler.GetOperatorCount); // Server was queried
        Assert.Null(remaining);                    // Stale snapshot was purged
    }

    [Fact]
    public async Task OperatorService_GetAsync_WhenInfilTimerStillActiveAndOnline_PrefersServerStateAndPurgesStaleSnapshot()
    {
        var operatorId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
        var js = new FakeBrowserJsRuntime();
        var handler = new GetBaseOperatorHandler(operatorId);
        using var http = new HttpClient(handler);
        var nodeService = new NodeConnectionService(js);
        await nodeService.SetBaseUrlAsync("https://node.example.com");
        var auth = new AuthService(js, http, nodeService);
        var api = new ApiClient(http, nodeService, auth);
        var offlineStore = new BrowserOfflineStore(js);
        var combatStore = new BrowserCombatSessionStore(js);
        var offlineGameplay = new OfflineGameplayService(combatStore, offlineStore);
        var offlineSync = new OfflineSyncService(api, offlineStore);
        var connection = new ConnectionStateService(js); // IsOnline defaults to true
        var service = new OperatorService(api, offlineStore, offlineGameplay, offlineSync, connection);

        // Save an active infil snapshot (timer started 5 minutes ago).
        await offlineStore.SaveInfiledOperatorAsync(CreateOperator(operatorId, "Echo",
            infilStartTime: DateTimeOffset.UtcNow.AddMinutes(-5)));

        var (data, error) = await service.GetAsync(operatorId);
        var remaining = await offlineStore.GetInfiledOperatorAsync(operatorId);

        Assert.Null(error);
        Assert.NotNull(data);
        Assert.Equal("Base", data.CurrentMode);       // Authoritative server state returned
        Assert.Equal(1, handler.GetOperatorCount);    // Server queried even while timer is active
        Assert.Null(remaining);                       // Stale local snapshot was purged
    }

    [Fact]
    public async Task OperatorService_GetAsync_WhenOnlineFetchFails_FallsBackToLocalSnapshot()
    {
        var operatorId = Guid.Parse("abababab-abab-abab-abab-abababababab");
        var js = new FakeBrowserJsRuntime();
        using var http = new HttpClient(new FailingHandler());
        var nodeService = new NodeConnectionService(js);
        await nodeService.SetBaseUrlAsync("https://node.example.com");
        var auth = new AuthService(js, http, nodeService);
        var api = new ApiClient(http, nodeService, auth);
        var offlineStore = new BrowserOfflineStore(js);
        var combatStore = new BrowserCombatSessionStore(js);
        var offlineGameplay = new OfflineGameplayService(combatStore, offlineStore);
        var offlineSync = new OfflineSyncService(api, offlineStore);
        var connection = new ConnectionStateService(js); // IsOnline defaults to true
        var service = new OperatorService(api, offlineStore, offlineGameplay, offlineSync, connection);

        await offlineStore.SaveInfiledOperatorAsync(CreateOperator(operatorId, "Fallback",
            infilStartTime: DateTimeOffset.UtcNow.AddMinutes(-5)));

        var (data, error) = await service.GetAsync(operatorId);

        Assert.Null(error);
        Assert.NotNull(data);
        Assert.Equal("Infil", data.CurrentMode);
        Assert.Equal(operatorId, data.Id);
    }

    [Fact]
    public async Task OperatorService_GetAsync_WhenOnlineServerReturnsError_FallsBackToLocalSnapshot()
    {
        var operatorId = Guid.Parse("cdcdcdcd-cdcd-cdcd-cdcd-cdcdcdcdcdcd");
        var js = new FakeBrowserJsRuntime();
        var handler = new ErrorOperatorHandler(HttpStatusCode.ServiceUnavailable);
        using var http = new HttpClient(handler);
        var nodeService = new NodeConnectionService(js);
        await nodeService.SetBaseUrlAsync("https://node.example.com");
        var auth = new AuthService(js, http, nodeService);
        var api = new ApiClient(http, nodeService, auth);
        var offlineStore = new BrowserOfflineStore(js);
        var combatStore = new BrowserCombatSessionStore(js);
        var offlineGameplay = new OfflineGameplayService(combatStore, offlineStore);
        var offlineSync = new OfflineSyncService(api, offlineStore);
        var connection = new ConnectionStateService(js); // IsOnline defaults to true
        var service = new OperatorService(api, offlineStore, offlineGameplay, offlineSync, connection);

        await offlineStore.SaveInfiledOperatorAsync(CreateOperator(operatorId, "Fallback Error",
            infilStartTime: DateTimeOffset.UtcNow.AddMinutes(-5)));

        var (data, error) = await service.GetAsync(operatorId);

        Assert.Null(error);
        Assert.NotNull(data);
        Assert.Equal("Infil", data.CurrentMode);
        Assert.Equal(operatorId, data.Id);
        Assert.Equal(1, handler.GetOperatorCount);
    }

    [Fact]
    public async Task OperatorService_GetAsync_WhenServerReturnsInfil_RefreshesLocalSnapshot()
    {
        var operatorId = Guid.Parse("dededede-dede-dede-dede-dededededede");
        var js = new FakeBrowserJsRuntime();
        var handler = new GetInfilOperatorHandler(operatorId, "Server Echo");
        using var http = new HttpClient(handler);
        var nodeService = new NodeConnectionService(js);
        await nodeService.SetBaseUrlAsync("https://node.example.com");
        var auth = new AuthService(js, http, nodeService);
        var api = new ApiClient(http, nodeService, auth);
        var offlineStore = new BrowserOfflineStore(js);
        var combatStore = new BrowserCombatSessionStore(js);
        var offlineGameplay = new OfflineGameplayService(combatStore, offlineStore);
        var offlineSync = new OfflineSyncService(api, offlineStore);
        var connection = new ConnectionStateService(js); // IsOnline defaults to true
        var service = new OperatorService(api, offlineStore, offlineGameplay, offlineSync, connection);

        await offlineStore.SaveInfiledOperatorAsync(CreateOperator(operatorId, "Local Echo",
            infilStartTime: DateTimeOffset.UtcNow.AddMinutes(-5)));

        var (data, error) = await service.GetAsync(operatorId);
        var refreshed = await offlineStore.GetInfiledOperatorAsync(operatorId);

        Assert.Null(error);
        Assert.NotNull(data);
        Assert.Equal("Server Echo", data.Name);
        Assert.NotNull(refreshed);
        Assert.Equal("Server Echo", refreshed.Name);
        Assert.Equal(1, handler.GetOperatorCount);
    }

    [Fact]
    public async Task OperatorService_GetAsync_WhenInfilTimerExpiredAndOffline_ReturnsStaleLocalSnapshot()
    {
        var operatorId = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
        var js = new FakeBrowserJsRuntime();
        var handler = new CountingGetHandler();
        using var http = new HttpClient(handler);
        var nodeService = new NodeConnectionService(js);
        await nodeService.SetBaseUrlAsync("https://node.example.com");
        var auth = new AuthService(js, http, nodeService);
        var api = new ApiClient(http, nodeService, auth);
        var offlineStore = new BrowserOfflineStore(js);
        var combatStore = new BrowserCombatSessionStore(js);
        var offlineGameplay = new OfflineGameplayService(combatStore, offlineStore);
        var offlineSync = new OfflineSyncService(api, offlineStore);
        var connection = new ConnectionStateService(js);
        await connection.OnConnectionChanged(false); // Simulate offline
        var service = new OperatorService(api, offlineStore, offlineGameplay, offlineSync, connection);

        // Save a stale infil snapshot whose timer lapsed 31 minutes ago.
        await offlineStore.SaveInfiledOperatorAsync(CreateOperator(operatorId, "Foxtrot",
            infilStartTime: DateTimeOffset.UtcNow.AddMinutes(-31)));

        var (data, error) = await service.GetAsync(operatorId);

        Assert.Null(error);
        Assert.NotNull(data);
        Assert.Equal("Infil", data.CurrentMode);    // Local snapshot returned (offline fallback)
        Assert.Equal(0, handler.GetOperatorCount);  // Server NOT queried while offline
    }

    [Fact]
    public async Task OperatorService_CompleteInfilAsync_WhenOnlineWithSnapshotAndNoPendingOfflineExfil_CallsServerApi()
    {
        // Regression test: after an online combat victory, the player clicks Exfil on the infil
        // screen. An infil snapshot exists (saved at infil start) but there is no queued offline
        // exfil. The old code returned early from SyncAndFinalizeAsync without calling the server,
        // leaving the operator in Infil mode and causing OperatorDetails to redirect back to infil.
        var operatorId = Guid.Parse("a1a1a1a1-a1a1-a1a1-a1a1-a1a1a1a1a1a1");
        var js = new FakeBrowserJsRuntime();
        var handler = new InfilCompleteHandler();
        using var http = new HttpClient(handler);
        var nodeService = new NodeConnectionService(js);
        await nodeService.SetBaseUrlAsync("https://node.example.com");
        var auth = new AuthService(js, http, nodeService);
        var api = new ApiClient(http, nodeService, auth);
        var offlineStore = new BrowserOfflineStore(js);
        var combatStore = new BrowserCombatSessionStore(js);
        var offlineGameplay = new OfflineGameplayService(combatStore, offlineStore);
        var offlineSync = new OfflineSyncService(api, offlineStore);
        var connection = new ConnectionStateService(js); // IsOnline defaults to true
        var service = new OperatorService(api, offlineStore, offlineGameplay, offlineSync, connection);

        // Simulate an infil snapshot saved at infil start (always present for online infil).
        // No pending offline combat envelopes and no queued offline exfil — this is the online path.
        await offlineStore.SaveInfiledOperatorAsync(CreateOperator(operatorId, "Ghost",
            infilStartTime: DateTimeOffset.UtcNow.AddMinutes(-5)));

        var error = await service.CompleteInfilAsync(operatorId);

        Assert.Null(error);
        Assert.Equal(1, handler.InfilCompleteCount); // Server API must be called
    }

    [Fact]
    public async Task OperatorService_OnCombatSessionConcludedAsync_ClearsActiveCombatSessionIdFromLocalSnapshot()
    {
        // Regression: after an online session concludes, the local snapshot still holds the
        // ActiveCombatSessionId. If the user goes offline before navigating away, MissionInfil
        // would treat the stale ID as an active session and redirect in an infinite loop.
        // OnCombatSessionConcludedAsync must clear the ID so HasActiveCombat returns false offline.
        var operatorId = Guid.Parse("b2b2b2b2-b2b2-b2b2-b2b2-b2b2b2b2b2b2");
        var sessionId = Guid.Parse("c3c3c3c3-c3c3-c3c3-c3c3-c3c3c3c3c3c3");
        var js = new FakeBrowserJsRuntime();
        var handler = new FailingHandler();
        using var http = new HttpClient(handler);
        var nodeService = new NodeConnectionService(js);
        await nodeService.SetBaseUrlAsync("https://node.example.com");
        var auth = new AuthService(js, http, nodeService);
        var api = new ApiClient(http, nodeService, auth);
        var offlineStore = new BrowserOfflineStore(js);
        var combatStore = new BrowserCombatSessionStore(js);
        var offlineGameplay = new OfflineGameplayService(combatStore, offlineStore);
        var offlineSync = new OfflineSyncService(api, offlineStore);
        var connection = new ConnectionStateService(js);
        var service = new OperatorService(api, offlineStore, offlineGameplay, offlineSync, connection);

        // Save a snapshot with an active combat session ID (as happens when combat starts online)
        await offlineStore.SaveInfiledOperatorAsync(CreateOperator(operatorId, "Hawk",
            infilStartTime: DateTimeOffset.UtcNow.AddMinutes(-5)));
        await offlineStore.UpdateActiveCombatSessionIdAsync(operatorId, sessionId);

        // Verify the session ID is present before clearing
        var before = await offlineStore.GetInfiledOperatorAsync(operatorId);
        Assert.Equal(sessionId, before?.ActiveCombatSessionId);

        await service.OnCombatSessionConcludedAsync(operatorId);

        // After clearing, HasActiveCombat must return false so MissionInfil won't redirect offline
        var after = await offlineStore.GetInfiledOperatorAsync(operatorId);
        Assert.NotNull(after);
        Assert.Null(after.ActiveCombatSessionId);
        Assert.False(OperatorNavigationHelper.HasActiveCombat(after));
    }

    private static OperatorState CreateOperator(Guid id, string name, DateTimeOffset? infilStartTime = null) => new()
    {
        Id = id,
        Name = name,
        CurrentMode = "Infil",
        CurrentHealth = 100,
        MaxHealth = 100,
        EquippedWeaponName = "Rifle",
        UnlockedPerks = [],
        InfilStartTime = infilStartTime
    };

    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("offline");
    }

    private sealed class DelayedSyncHandler : HttpMessageHandler
    {
        public int OfflineSyncPostCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath == "/operators/offline/sync")
            {
                OfflineSyncPostCount++;
                await Task.Delay(50, cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(string.Empty, Encoding.UTF8, "application/json")
                };
            }

            if (request.Method == HttpMethod.Get && request.RequestUri?.AbsolutePath.StartsWith("/operators/", StringComparison.Ordinal) == true)
            {
                var op = new OperatorState
                {
                    Id = SyncOperatorId,
                    Name = "Offline Op",
                    CurrentMode = "Infil",
                    CurrentHealth = 100,
                    MaxHealth = 100,
                    EquippedWeaponName = "Rifle",
                    UnlockedPerks = []
                };
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(op), Encoding.UTF8, "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(string.Empty, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class StartCombatHandler : HttpMessageHandler
    {
        public Guid SessionId { get; } = Guid.Parse("33333333-3333-3333-3333-333333333333");
        public int StartCombatCount { get; private set; }
        public string? LastRequestPath { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestPath = request.RequestUri?.AbsolutePath;

            if (request.Method == HttpMethod.Post &&
                request.RequestUri?.AbsolutePath.EndsWith("/infil/combat", StringComparison.Ordinal) == true)
            {
                StartCombatCount++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(SessionId)
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(string.Empty, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class GetBaseOperatorHandler : HttpMessageHandler
    {
        private readonly Guid _operatorId;
        public int GetOperatorCount { get; private set; }

        public GetBaseOperatorHandler(Guid operatorId) => _operatorId = operatorId;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Get &&
                request.RequestUri?.AbsolutePath == $"/operators/{_operatorId}")
            {
                GetOperatorCount++;
                var op = new OperatorState
                {
                    Id = _operatorId,
                    Name = "Delta",
                    CurrentMode = "Base",
                    CurrentHealth = 100,
                    MaxHealth = 100,
                    EquippedWeaponName = string.Empty,
                    UnlockedPerks = []
                };
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(op)
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(string.Empty, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class GetInfilOperatorHandler : HttpMessageHandler
    {
        private readonly Guid _operatorId;
        private readonly string _name;
        public int GetOperatorCount { get; private set; }

        public GetInfilOperatorHandler(Guid operatorId, string name)
        {
            _operatorId = operatorId;
            _name = name;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Get &&
                request.RequestUri?.AbsolutePath == $"/operators/{_operatorId}")
            {
                GetOperatorCount++;
                var op = CreateOperator(_operatorId, _name, DateTimeOffset.UtcNow.AddMinutes(-4));
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(op)
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(string.Empty, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class ErrorOperatorHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        public int GetOperatorCount { get; private set; }

        public ErrorOperatorHandler(HttpStatusCode statusCode) => _statusCode = statusCode;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Get &&
                request.RequestUri?.AbsolutePath.StartsWith("/operators/", StringComparison.Ordinal) == true)
            {
                GetOperatorCount++;
                return Task.FromResult(new HttpResponseMessage(_statusCode)
                {
                    Content = new StringContent(string.Empty, Encoding.UTF8, "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(string.Empty, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class CountingGetHandler : HttpMessageHandler
    {
        public int GetOperatorCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Get &&
                request.RequestUri?.AbsolutePath.StartsWith("/operators/", StringComparison.Ordinal) == true &&
                !request.RequestUri.AbsolutePath.EndsWith("/operators/", StringComparison.Ordinal))
            {
                GetOperatorCount++;
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(string.Empty, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class InfilCompleteHandler : HttpMessageHandler
    {
        public int InfilCompleteCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Post &&
                request.RequestUri?.AbsolutePath.EndsWith("/infil/complete", StringComparison.Ordinal) == true)
            {
                InfilCompleteCount++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(string.Empty, Encoding.UTF8, "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(string.Empty, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class FakeBrowserJsRuntime : IJSRuntime
    {
        private readonly bool _throwOnGunRpgStorage;
        private readonly Dictionary<string, object?> _localStorage = new(StringComparer.Ordinal);
        private readonly Dictionary<string, object?> _tokens = new(StringComparer.Ordinal);
        private readonly Dictionary<string, BrowserOfflineStore.BrowserMetadataRecord> _metadata = new(StringComparer.Ordinal);
        private readonly Dictionary<string, BrowserOfflineStore.BrowserInfiledOperatorRecord> _infiledOperators = new(StringComparer.Ordinal);
        private readonly Dictionary<string, OfflineMissionEnvelope> _missionResults = new(StringComparer.Ordinal);

        public FakeBrowserJsRuntime(bool throwOnGunRpgStorage = false)
        {
            _throwOnGunRpgStorage = throwOnGunRpgStorage;
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            if (_throwOnGunRpgStorage && identifier.StartsWith("gunRpgStorage.", StringComparison.Ordinal))
                throw new JSException("Storage unavailable.");

            object? value = identifier switch
            {
                "localStorage.getItem" => GetDictionaryValue(_localStorage, Convert.ToString(args?[0])),
                "localStorage.setItem" => SetDictionaryValue(_localStorage, Convert.ToString(args?[0]), args?[1]),
                "localStorage.removeItem" => RemoveDictionaryValue(_localStorage, Convert.ToString(args?[0])),
                "tokenStorage.storeAccessToken" => SetDictionaryValue(_tokens, "accessToken", args?[0]),
                "tokenStorage.getAccessToken" => GetDictionaryValue(_tokens, "accessToken"),
                "tokenStorage.removeAccessToken" => RemoveDictionaryValue(_tokens, "accessToken"),
                "tokenStorage.storeRefreshToken" => SetDictionaryValue(_tokens, "refreshToken", args?[0]),
                "tokenStorage.getRefreshToken" => GetDictionaryValue(_tokens, "refreshToken"),
                "tokenStorage.removeRefreshToken" => RemoveDictionaryValue(_tokens, "refreshToken"),
                "tokenStorage.clearTokens" => ClearTokens(),
                "gunRpgStorage.saveInfiledOperator" => SaveInfiledOperator((BrowserOfflineStore.BrowserInfiledOperatorRecord)args![0]!),
                "gunRpgStorage.getInfiledOperator" => GetDictionaryValue(_infiledOperators, Convert.ToString(args?[0])),
                "gunRpgStorage.getActiveInfiledOperator" => _infiledOperators.Values.FirstOrDefault(x => x.IsActive),
                "gunRpgStorage.hasActiveInfiledOperator" => _infiledOperators.Values.Any(x => x.IsActive),
                "gunRpgStorage.updateInfiledOperator" => SaveOrUpdateInfiledOperator((BrowserOfflineStore.BrowserInfiledOperatorRecord)args![0]!),
                "gunRpgStorage.removeInfiledOperator" => RemoveDictionaryValue(_infiledOperators, Convert.ToString(args?[0])),
                "gunRpgStorage.saveOfflineMissionResult" => SaveMissionResult((OfflineMissionEnvelope)args![0]!),
                "gunRpgStorage.getOfflineMissionResult" => GetDictionaryValue(_missionResults, Convert.ToString(args?[0])),
                "gunRpgStorage.getAllOfflineMissionResults" => _missionResults.Values.OrderBy(x => x.SequenceNumber).ToList(),
                "gunRpgStorage.putValue" => PutMetadata(Convert.ToString(args?[1]), (BrowserOfflineStore.BrowserMetadataRecord)args![2]!),
                "gunRpgStorage.getValue" => GetMetadata(Convert.ToString(args?[1])),
                "gunRpgStorage.deleteValue" => DeleteMetadata(Convert.ToString(args?[1])),
                "gunRpgStorage.getAllValues" => _metadata.Values.ToList(),
                _ => throw new NotSupportedException(identifier)
            };

            return new ValueTask<TValue>((TValue?)value ?? default!);
        }

        private static object? SetDictionaryValue(IDictionary<string, object?> dictionary, string? key, object? value)
        {
            if (!string.IsNullOrEmpty(key))
                dictionary[key] = value;
            return null;
        }

        private static object? RemoveDictionaryValue<TValue>(IDictionary<string, TValue> dictionary, string? key)
        {
            if (!string.IsNullOrEmpty(key))
                dictionary.Remove(key);
            return null;
        }

        private static object? GetDictionaryValue<TValue>(IReadOnlyDictionary<string, TValue> dictionary, string? key)
        {
            return !string.IsNullOrEmpty(key) && dictionary.TryGetValue(key, out var value) ? value : default;
        }

        private object? ClearTokens()
        {
            _tokens.Clear();
            return null;
        }

        private object? SaveInfiledOperator(BrowserOfflineStore.BrowserInfiledOperatorRecord record)
        {
            foreach (var existing in _infiledOperators.Values)
                existing.IsActive = false;
            _infiledOperators[record.Id] = record;
            return null;
        }

        private object? SaveOrUpdateInfiledOperator(BrowserOfflineStore.BrowserInfiledOperatorRecord record)
        {
            _infiledOperators[record.Id] = record;
            return null;
        }

        private object? SaveMissionResult(OfflineMissionEnvelope envelope)
        {
            _missionResults[envelope.Id] = envelope;
            return null;
        }

        private object? PutMetadata(string? key, BrowserOfflineStore.BrowserMetadataRecord record)
        {
            if (!string.IsNullOrEmpty(key))
                _metadata[key] = record;
            return null;
        }

        private BrowserOfflineStore.BrowserMetadataRecord? GetMetadata(string? key) =>
            !string.IsNullOrEmpty(key) && _metadata.TryGetValue(key, out var record) ? record : null;

        private object? DeleteMetadata(string? key)
        {
            if (!string.IsNullOrEmpty(key))
                _metadata.Remove(key);
            return null;
        }
    }
}
