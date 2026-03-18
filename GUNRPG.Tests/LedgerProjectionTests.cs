using GUNRPG.Application.Combat;
using GUNRPG.Application.Gameplay;
using GUNRPG.Application.Operators;
using GUNRPG.Core.Operators;
using GUNRPG.Gossip;
using GUNRPG.Infrastructure.Gameplay;
using GUNRPG.Infrastructure.Persistence;
using GUNRPG.Ledger;
using GUNRPG.Security;
using LiteDB;
using Microsoft.Extensions.Logging.Abstractions;

namespace GUNRPG.Tests;

public sealed class LedgerProjectionTests : IDisposable
{
    private readonly LiteDatabase _database;
    private readonly LiteDbOperatorEventStore _eventStore;
    private readonly RunLedger _ledger;
    private readonly IGameplayLedgerBridge _bridge;
    private readonly OperatorExfilService _service;

    public LedgerProjectionTests()
    {
        var mapper = new BsonMapper();
        mapper.Entity<OperatorEventDocument>().Id(x => x.Id);
        _database = new LiteDatabase(":memory:", mapper);
        _eventStore = new LiteDbOperatorEventStore(_database);

        var rootPrivateKey = CertificateIssuer.GeneratePrivateKey();
        var certificateIssuer = new CertificateIssuer(rootPrivateKey);

        var authorityPrivateKey = CertificateIssuer.GeneratePrivateKey();
        var authority = new Authority(CertificateIssuer.GetPublicKey(authorityPrivateKey), "ledger-authority");

        var serverPrivateKey = ServerIdentity.GeneratePrivateKey();
        var serverIdentity = new ServerIdentity(
            certificateIssuer.IssueServerCertificate(
                Guid.NewGuid(),
                ServerIdentity.GetPublicKey(serverPrivateKey),
                DateTimeOffset.UtcNow.AddMinutes(-5),
                DateTimeOffset.UtcNow.AddHours(1)),
            serverPrivateKey);

        var authorityRoot = new AuthorityRoot(certificateIssuer.RootPublicKey);
        var signatureVerifier = new SignatureVerifier(authorityRoot);
        _ledger = new RunLedger([authority], new QuorumPolicy(1));
        var replayEngine = new RunReplayEngine(serverIdentity);
        var gossip = new LedgerGossipService(
            [],
            new LedgerSyncEngine(_ledger),
            _ledger,
            replayEngine,
            signatureVerifier,
            new QuorumValidator(),
            new QuorumPolicy(1),
            NullLogger<LedgerGossipService>.Instance);

        _bridge = new RunLedgerGameplayBridge(
            _ledger,
            new LedgerGameStateProjector(),
            replayEngine,
            gossip,
            authority,
            authorityPrivateKey);

        _service = new OperatorExfilService(
            _eventStore,
            ledgerBridge: _bridge,
            ledgerOptions: new GameplayLedgerOptions
            {
                MirrorLegacyWrites = true,
                PreferLedgerReads = false,
                CompareLegacyAndLedgerState = true
            });
    }

    public void Dispose()
    {
        _database.Dispose();
    }

    [Fact]
    public async Task LegacyVsLedger_StateParity()
    {
        var create = await _service.CreateOperatorAsync("Ledger Ranger");
        var operatorId = create.Value!;

        await _service.ChangeLoadoutAsync(operatorId, "AK-47");
        await _service.UnlockPerkAsync(operatorId, "Fast Reload");
        var infil = await _service.StartInfilAsync(operatorId);
        await _service.StartCombatSessionAsync(operatorId, infil.Value);
        await _service.ProcessCombatOutcomeAsync(new CombatOutcome(
            infil.Value,
            operatorId,
            operatorDied: false,
            xpGained: 120,
            gearLost: [],
            isVictory: true,
            turnsSurvived: 6,
            damageTaken: 18f));
        await _service.CompleteInfilSuccessfullyAsync(operatorId);
        await _service.TreatWoundsAsync(operatorId, 10f);

        var legacy = OperatorAggregate.FromEvents(await _eventStore.LoadEventsAsync(operatorId));
        var projected = await _bridge.LoadProjectedOperatorAsync(operatorId);

        Assert.NotNull(projected);
        Assert.Equal(legacy.Name, projected!.Name);
        Assert.Equal(legacy.TotalXp, projected.TotalXp);
        Assert.Equal(legacy.CurrentHealth, projected.CurrentHealth);
        Assert.Equal(legacy.EquippedWeaponName, projected.EquippedWeaponName);
        Assert.Equal(legacy.UnlockedPerks, projected.UnlockedPerks);
        Assert.Equal(legacy.ExfilStreak, projected.ExfilStreak);
        Assert.Equal(legacy.CurrentMode, projected.CurrentMode);
        Assert.Equal(legacy.InfilSessionId, projected.InfilSessionId);
        Assert.Equal(legacy.ActiveCombatSessionId, projected.ActiveCombatSessionId);
    }

    [Fact]
    public async Task Projector_DerivesStateAndRunHistoryFromLedger()
    {
        var create = await _service.CreateOperatorAsync("Projector");
        var operatorId = create.Value!;

        await _service.ChangeLoadoutAsync(operatorId, "Rifle");
        await _service.ApplyXpAsync(operatorId, 25, "Training");

        var projectedState = await _bridge.ProjectAsync();
        var player = Assert.Single(projectedState.Players);

        Assert.Equal(operatorId.Value, player.PlayerId);
        Assert.Equal("Projector", player.Name);
        Assert.Equal("Rifle", player.EquippedWeaponName);
        Assert.Contains("Rifle", player.Inventory);
        Assert.Equal(25, player.TotalXp);
        Assert.NotEmpty(projectedState.RunHistory);
    }

    [Fact]
    public async Task LoadOperatorAsync_CanPreferLedgerProjection()
    {
        var create = await _service.CreateOperatorAsync("Projected Reader");
        var operatorId = create.Value!;
        await _service.ApplyXpAsync(operatorId, 40, "Ledger");

        var projectedReader = new OperatorExfilService(
            _eventStore,
            ledgerBridge: _bridge,
            ledgerOptions: new GameplayLedgerOptions
            {
                MirrorLegacyWrites = true,
                PreferLedgerReads = true,
                CompareLegacyAndLedgerState = true
            });

        var result = await projectedReader.LoadOperatorAsync(operatorId);

        Assert.True(result.IsSuccess);
        Assert.Equal(40, result.Value!.TotalXp);
    }

    [Fact]
    public async Task LegacyWrite_SucceedsWhenLedgerMirrorThrows()
    {
        var throwingService = new OperatorExfilService(
            _eventStore,
            ledgerBridge: new ThrowingGameplayLedgerBridge(),
            ledgerOptions: new GameplayLedgerOptions
            {
                MirrorLegacyWrites = true,
                PreferLedgerReads = false,
                CompareLegacyAndLedgerState = true
            });

        var create = await throwingService.CreateOperatorAsync("Best Effort");

        Assert.True(create.IsSuccess);

        var load = await throwingService.LoadOperatorAsync(create.Value!);
        Assert.True(load.IsSuccess);
        Assert.Equal("Best Effort", load.Value!.Name);
    }

    private sealed class ThrowingGameplayLedgerBridge : IGameplayLedgerBridge
    {
        public Task MirrorAsync(Guid runId, OperatorId operatorId, IReadOnlyList<OperatorEvent> operatorEvents, IReadOnlyList<GameplayLedgerEvent>? gameplayEvents = null, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Mirror failed.");
        }

        public Task<OperatorAggregate?> LoadProjectedOperatorAsync(OperatorId operatorId, CancellationToken cancellationToken = default)
            => Task.FromResult<OperatorAggregate?>(null);

        public Task<IReadOnlyList<OperatorId>> ListProjectedOperatorsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<OperatorId>>([]);

        public Task<GUNRPG.Application.Gameplay.GameState> ProjectAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new GUNRPG.Application.Gameplay.GameState());
    }
}
