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
    public async Task SyncPendingAsync_SendsInSequence_StopsOnFirstFailure()
    {
        var store = new OfflineStore(_database);
        SaveEnvelope(store, 1, "h0", "h1");
        SaveEnvelope(store, 2, "h1", "h2");
        SaveEnvelope(store, 3, "h2", "h3");

        var handler = new QueueHandler(HttpStatusCode.OK, HttpStatusCode.BadRequest, HttpStatusCode.OK);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var backend = new OnlineGameBackend(client, store);
        var service = new ExfilSyncService(store, backend);

        var result = await service.SyncPendingAsync();

        Assert.False(result);
        Assert.Equal(new long[] { 1, 2 }, handler.SequenceNumbers);
        var unsyncedSequences = store.GetUnsyncedResults("op-1").Select(x => x.SequenceNumber).ToArray();
        Assert.Equal(new long[] { 2, 3 }, unsyncedSequences);
    }

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

    private sealed class QueueHandler : HttpMessageHandler
    {
        private readonly Queue<HttpStatusCode> _statuses;
        public List<long> SequenceNumbers { get; } = new();

        public QueueHandler(params HttpStatusCode[] statuses)
        {
            _statuses = new Queue<HttpStatusCode>(statuses);
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
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
