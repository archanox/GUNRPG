using GUNRPG.Application.Combat;
using GUNRPG.Application.Requests;
using GUNRPG.Application.Operators;
using GUNRPG.Application.Services;
using GUNRPG.Application.Sessions;
using GUNRPG.Core.Equipment;
using GUNRPG.Core.Operators;
using GUNRPG.Infrastructure.Persistence;
using LiteDB;

namespace GUNRPG.Tests;

public sealed class OperatorStatsServiceTests : IDisposable
{
    private readonly LiteDatabase _database;
    private readonly LiteDbOperatorEventStore _eventStore;
    private readonly LiteDbOperatorStatsStore _statsStore;
    private readonly OperatorStatsService _statsService;

    public OperatorStatsServiceTests()
    {
        _database = new LiteDatabase(":memory:");
        _eventStore = new LiteDbOperatorEventStore(_database);
        _statsStore = new LiteDbOperatorStatsStore(_database);
        _statsService = new OperatorStatsService(_statsStore, _eventStore);
    }

    public void Dispose()
    {
        _database.Dispose();
    }

    [Fact]
    public async Task UpdateStatsAsync_AggregatesInfilExfilDurationAndKills()
    {
        var operatorId = Guid.NewGuid();

        await _statsService.UpdateStatsAsync(new RunStats(operatorId, SuccessfulExfil: true, InfilDurationTicks: TimeSpan.FromMinutes(12).Ticks, EnemyKills: 2));
        await _statsService.UpdateStatsAsync(new RunStats(operatorId, SuccessfulExfil: false, InfilDurationTicks: TimeSpan.FromMinutes(8).Ticks, EnemyKills: 1));

        var stats = await _statsService.GetStatsAsync(operatorId);

        Assert.Equal(2, stats.InfilCount);
        Assert.Equal(1, stats.ExfilCount);
        Assert.Equal(TimeSpan.FromMinutes(20).Ticks, stats.TotalInfilDurationTicks);
        Assert.Equal(3, stats.EnemyKills);
    }

    [Fact]
    public void RunStatsExtractor_ExtractCompletedRuns_UsesInfilWindowKillsAndExfilFlag()
    {
        var operatorId = OperatorId.NewId();
        var startedAt = new DateTimeOffset(2026, 03, 29, 6, 0, 0, TimeSpan.Zero);

        var created = new OperatorCreatedEvent(operatorId, "StatsOp", startedAt.AddMinutes(-1));
        var infilStarted = new InfilStartedEvent(operatorId, 1, Guid.NewGuid(), "SOKOL 545", startedAt, created.Hash, startedAt);
        var victoryOne = new CombatVictoryEvent(operatorId, 2, infilStarted.Hash, startedAt.AddMinutes(5));
        var victoryTwo = new CombatVictoryEvent(operatorId, 3, victoryOne.Hash, startedAt.AddMinutes(9));
        var infilEnded = new InfilEndedEvent(operatorId, 4, wasSuccessful: false, reason: "Mission failed", previousHash: victoryTwo.Hash, timestamp: startedAt.AddMinutes(14));

        var runs = RunStatsExtractor.ExtractCompletedRuns([created, infilStarted, victoryOne, victoryTwo, infilEnded]);

        var run = Assert.Single(runs);
        Assert.Equal(operatorId.Value, run.OperatorId);
        Assert.False(run.SuccessfulExfil);
        Assert.Equal(TimeSpan.FromMinutes(14).Ticks, run.InfilDurationTicks);
        Assert.Equal(2, run.EnemyKills);
    }

    [Fact]
    public async Task GetOperatorAsync_RebuildsAndReturnsOperatorStats()
    {
        var exfilService = new OperatorExfilService(_eventStore, operatorStatsService: _statsService);
        var sessionStore = new LiteDbCombatSessionStore(_database);
        var sessionService = new CombatSessionService(sessionStore, _eventStore);
        var operatorService = new OperatorService(exfilService, sessionService, _eventStore, statsService: _statsService);

        var createResult = await operatorService.CreateOperatorAsync(new OperatorCreateRequest { Name = "StatsOp" });
        Assert.True(createResult.IsSuccess);
        var operatorId = createResult.Value!.Id;

        var infilResult = await operatorService.StartInfilAsync(operatorId);
        Assert.True(infilResult.IsSuccess);

        var processResult = await exfilService.ProcessCombatOutcomeAsync(new CombatOutcome(
            infilResult.Value!.SessionId,
            OperatorId.FromGuid(operatorId),
            operatorDied: false,
            xpGained: 100,
            gearLost: Array.Empty<GearId>(),
            isVictory: true,
            completedAt: DateTimeOffset.UtcNow), playerConfirmed: true);
        Assert.True(processResult.IsSuccess);

        var exfilComplete = await exfilService.CompleteInfilSuccessfullyAsync(OperatorId.FromGuid(operatorId));
        Assert.True(exfilComplete.IsSuccess);

        var dto = await operatorService.GetOperatorAsync(operatorId);
        Assert.True(dto.IsSuccess);
        Assert.Equal(1, dto.Value!.Stats.InfilCount);
        Assert.Equal(1, dto.Value.Stats.ExfilCount);
        Assert.Equal(1, dto.Value.Stats.EnemyKills);
        Assert.True(dto.Value.Stats.TotalInfilDurationTicks >= 0);
    }
}
