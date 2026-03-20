using GUNRPG.Ledger;
using GUNRPG.Core.Simulation;
using GUNRPG.Security;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GUNRPG.Gossip;

public sealed class LedgerGossipService : BackgroundService, IGossipService
{
    private static readonly TimeSpan SyncInterval = TimeSpan.FromSeconds(5);

    private readonly IEnumerable<IGossipPeerClient> _peers;
    private readonly LedgerSyncEngine _syncEngine;
    private readonly RunLedger _ledger;
    private readonly IRunReplayEngine _replayEngine;
    private readonly SignatureVerifier _signatureVerifier;
    private readonly QuorumValidator _quorumValidator;
    private readonly QuorumPolicy _quorumPolicy;
    private readonly ILogger<LedgerGossipService> _logger;

    public LedgerGossipService(
        IEnumerable<IGossipPeerClient> peers,
        LedgerSyncEngine syncEngine,
        RunLedger ledger,
        IRunReplayEngine replayEngine,
        SignatureVerifier signatureVerifier,
        QuorumValidator quorumValidator,
        QuorumPolicy quorumPolicy,
        ILogger<LedgerGossipService> logger)
    {
        _peers = peers ?? throw new ArgumentNullException(nameof(peers));
        _syncEngine = syncEngine ?? throw new ArgumentNullException(nameof(syncEngine));
        _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        _replayEngine = replayEngine ?? throw new ArgumentNullException(nameof(replayEngine));
        _signatureVerifier = signatureVerifier ?? throw new ArgumentNullException(nameof(signatureVerifier));
        _quorumValidator = quorumValidator ?? throw new ArgumentNullException(nameof(quorumValidator));
        _quorumPolicy = quorumPolicy ?? throw new ArgumentNullException(nameof(quorumPolicy));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task SyncAsync(CancellationToken cancellationToken = default)
    {
        foreach (var peer in _peers)
        {
            var peerHead = await peer.GetLedgerHeadAsync(cancellationToken).ConfigureAwait(false);
            if (!_syncEngine.NeedsSync(peerHead))
            {
                continue;
            }

            var request = _syncEngine.BuildSyncRequest(peerHead);
            var entries = await peer.GetEntriesFromAsync(request.FromIndex, LedgerSyncEngine.MaxSyncBatchSize, cancellationToken).ConfigureAwait(false);
            var applied = _syncEngine.ApplyResponse(new LedgerSyncResponse(entries));
            if (!applied)
            {
                _logger.LogWarning("Failed to apply gossiped ledger entries starting from index {FromIndex}.", request.FromIndex);
            }
        }
    }

    public async Task<bool> MergePartialValidationAsync(RunInput input, RunValidationResult validationResult, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(validationResult);

        var appended = _ledger.TryAppendWithReplayValidation(
            input,
            validationResult,
            _replayEngine,
            _signatureVerifier,
            _quorumValidator,
            _quorumPolicy);

        if (appended && _ledger.Head is { } head)
        {
            await BroadcastLedgerEntryAsync(head, cancellationToken).ConfigureAwait(false);
        }

        return appended;
    }

    public async Task BroadcastLedgerEntryAsync(RunLedgerEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        foreach (var peer in _peers)
        {
            await peer.BroadcastLedgerEntryAsync(entry, cancellationToken).ConfigureAwait(false);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SyncAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Ledger gossip sync failed.");
            }

            await Task.Delay(SyncInterval, stoppingToken).ConfigureAwait(false);
        }
    }
}
