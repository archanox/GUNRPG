using System.Net;
using System.Text;
using System.Text.Json;
using GUNRPG.Application.Backend;
using GUNRPG.Infrastructure.Backend;
using GUNRPG.Infrastructure.Persistence;
using LiteDB;
using Xunit;

namespace GUNRPG.Tests;

public sealed class ExfilSyncServiceTests : IDisposable
{
    private readonly LiteDatabase _database = new(":memory:");

    public void Dispose()
    {
        _database.Dispose();
    }

    [Fact]
    public async Task SyncAsync_SendsInSequence_StopsOnFirstFailure()
    {
        var store = new OfflineStore(_database);
        SaveEnvelope(store, 1, "h0", "h1");
        SaveEnvelope(store, 2, "h1", "h2");
        SaveEnvelope(store, 3, "h2", "h3");

        // No operator configured → GET returns 404 → initial hash check skipped
        var handler = new QueueHandler(HttpStatusCode.OK, HttpStatusCode.BadRequest, HttpStatusCode.OK);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var backend = new OnlineGameBackend(client, store);
        var service = new ExfilSyncService(store, backend);

        var result = await service.SyncAsync("op-1");

        Assert.False(result.Success);
        Assert.False(result.IsIntegrityFailure); // server HTTP rejection, not an integrity violation
        Assert.Equal(new long[] { 1, 2 }, handler.SequenceNumbers);
        var unsyncedSequences = store.GetUnsyncedResults("op-1").Select(x => x.SequenceNumber).ToArray();
        Assert.Equal(new long[] { 2, 3 }, unsyncedSequences);
    }

    [Fact]
    public async Task SyncAsync_ContinuesFromLatestSyncedHead()
    {
        var store = new OfflineStore(_database);
        SaveEnvelope(store, 1, "h0", "h1");
        store.MarkResultSynced(store.GetUnsyncedResults("op-1").Single().Id);
        SaveEnvelope(store, 2, "h1", "h2");
        SaveEnvelope(store, 3, "h2", "h3");

        // previous != null so no GET is issued
        var handler = new QueueHandler(HttpStatusCode.OK, HttpStatusCode.OK);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var backend = new OnlineGameBackend(client, store);
        var service = new ExfilSyncService(store, backend);

        var result = await service.SyncAsync("op-1");

        Assert.True(result.Success);
        Assert.Equal(2, result.EnvelopesSynced);
        Assert.Equal(new long[] { 2, 3 }, handler.SequenceNumbers);
        Assert.Empty(store.GetUnsyncedResults("op-1"));
    }

    [Fact]
    public async Task SyncAsync_IntegrityFailure_WhenInitialHashMismatchesServerState()
    {
        var store = new OfflineStore(_database);
        var operatorDto = CreateTestOperator("op-1", "TestOp");
        // Envelope initial hash intentionally does NOT match the server's hash
        SaveEnvelope(store, 1, "wrong_initial_hash", "h1");

        var handler = new QueueHandler(operatorDto); // GET returns operator; no POST expected
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var backend = new OnlineGameBackend(client, store);
        var service = new ExfilSyncService(store, backend);

        var result = await service.SyncAsync("op-1");

        Assert.False(result.Success);
        Assert.True(result.IsIntegrityFailure);
        Assert.Empty(handler.SequenceNumbers); // no envelopes uploaded
    }

    [Fact]
    public async Task SyncAsync_Succeeds_WhenInitialHashMatchesServerState()
    {
        var store = new OfflineStore(_database);
        var operatorDto = CreateTestOperator("op-1", "TestOp");
        var serverHash = OfflineMissionHashing.ComputeOperatorStateHash(operatorDto);
        SaveEnvelope(store, 1, serverHash, "h1");

        var handler = new QueueHandler(operatorDto, HttpStatusCode.OK); // GET returns operator; POST succeeds
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var backend = new OnlineGameBackend(client, store);
        var service = new ExfilSyncService(store, backend);

        var result = await service.SyncAsync("op-1");

        Assert.True(result.Success);
        Assert.Equal(1, result.EnvelopesSynced);
        Assert.Equal(new long[] { 1 }, handler.SequenceNumbers);
    }

    [Fact]
    public async Task SyncAsync_ServerRejection_IsNotIntegrityFailure()
    {
        var store = new OfflineStore(_database);
        // Mark seq=1 as synced so previous != null and GET is skipped
        SaveEnvelope(store, 1, "h0", "h1");
        store.MarkResultSynced(store.GetUnsyncedResults("op-1").Single().Id);
        SaveEnvelope(store, 2, "h1", "h2");

        var handler = new QueueHandler(HttpStatusCode.InternalServerError);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var backend = new OnlineGameBackend(client, store);
        var service = new ExfilSyncService(store, backend);

        var result = await service.SyncAsync("op-1");

        Assert.False(result.Success);
        Assert.False(result.IsIntegrityFailure); // transient error, allow retry
    }

    private static OperatorDto CreateTestOperator(string id, string name) => new()
    {
        Id = id,
        Name = name,
        TotalXp = 0,
        CurrentHealth = 100,
        MaxHealth = 100,
        EquippedWeaponName = "TestWeapon",
        UnlockedPerks = new List<string>(),
        ExfilStreak = 0,
        IsDead = false,
        CurrentMode = "Infil",
        LockedLoadout = ""
    };

    private static void SaveEnvelope(OfflineStore store, long sequence, string initial, string result)
    {
        store.SaveMissionResult(new OfflineMissionEnvelope
        {
            OperatorId = "op-1",
            SequenceNumber = sequence,
            RandomSeed = 42,
            InitialOperatorStateHash = initial,
            ResultOperatorStateHash = result,
            FullBattleLog = new List<GUNRPG.Application.Dtos.BattleLogEntryDto> { new() { EventType = "Damage", TimeMs = sequence, Message = "hit" } },
            ExecutedUtc = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Flexible HTTP handler that returns operator JSON for GET requests and queued statuses for POST requests.
    /// </summary>
    private sealed class QueueHandler : HttpMessageHandler
    {
        private readonly string? _operatorJson;
        private readonly Queue<HttpStatusCode> _statuses;
        public List<long> SequenceNumbers { get; } = new();

        /// <summary>No operator configured — GET requests return 404 (initial hash check skipped).</summary>
        public QueueHandler(params HttpStatusCode[] statuses)
        {
            _statuses = new Queue<HttpStatusCode>(statuses);
        }

        /// <summary>Operator configured — GET requests return the operator JSON; POST requests use queued statuses.</summary>
        public QueueHandler(OperatorDto operatorDto, params HttpStatusCode[] postStatuses)
        {
            _operatorJson = System.Text.Json.JsonSerializer.Serialize(operatorDto, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            _statuses = new Queue<HttpStatusCode>(postStatuses);
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Get)
            {
                if (_operatorJson != null)
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(_operatorJson, Encoding.UTF8, "application/json")
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent(string.Empty, Encoding.UTF8, "application/json")
                };
            }

            // POST — offline mission sync
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            using var json = JsonDocument.Parse(body);
            SequenceNumbers.Add(json.RootElement.GetProperty("sequenceNumber").GetInt64());

            var status = _statuses.Count > 0 ? _statuses.Dequeue() : HttpStatusCode.OK;
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(string.Empty, Encoding.UTF8, "application/json")
            };
        }
    }
}
