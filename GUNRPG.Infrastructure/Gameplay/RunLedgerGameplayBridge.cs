using GUNRPG.Application.Gameplay;
using GUNRPG.Core.Operators;
using GUNRPG.Gossip;
using GUNRPG.Ledger;
using GUNRPG.Security;

namespace GUNRPG.Infrastructure.Gameplay;

public sealed class RunLedgerGameplayBridge : IGameplayLedgerBridge
{
    private readonly RunLedger _ledger;
    private readonly IGameStateProjector _projector;
    private readonly IRunReplayEngine _replayEngine;
    private readonly IGossipService _gossipService;
    private readonly Authority _authority;
    private readonly byte[] _authorityPrivateKey;

    public RunLedgerGameplayBridge(
        RunLedger ledger,
        IGameStateProjector projector,
        IRunReplayEngine replayEngine,
        IGossipService gossipService,
        Authority authority,
        byte[] authorityPrivateKey)
    {
        _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        _projector = projector ?? throw new ArgumentNullException(nameof(projector));
        _replayEngine = replayEngine ?? throw new ArgumentNullException(nameof(replayEngine));
        _gossipService = gossipService ?? throw new ArgumentNullException(nameof(gossipService));
        _authority = authority ?? throw new ArgumentNullException(nameof(authority));
        _authorityPrivateKey = authorityPrivateKey ?? throw new ArgumentNullException(nameof(authorityPrivateKey));
    }

    public async Task MirrorAsync(
        RunInput runInput,
        IReadOnlyList<OperatorEvent> operatorEvents,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runInput);
        ArgumentNullException.ThrowIfNull(operatorEvents);

        // Replay derives GameplayLedgerEvents from Actions + Seed.
        // OperatorEvents are NOT in RunInput — they are supplied by the server pipeline here
        // and merged into the mutation for projection purposes only.
        var replayed = _replayEngine.Replay(runInput);

        // Combine gameplay events: action-based events from the replay engine take precedence;
        // if the run had no actions yet, fall back to a semantic translation of the server-side
        // OperatorEvents so that GameplayEvents are always populated for ledger-history queries.
        IReadOnlyList<GameplayLedgerEvent> gameplayEvents = replayed.Events.Count > 0
            ? replayed.Events
            : BuildSemanticEvents(operatorEvents);

        var enrichedMutation = new RunLedgerMutation(operatorEvents, gameplayEvents);
        var enrichedResult = new RunValidationResult(
            replayed.RunId,
            replayed.PlayerId,
            replayed.ServerId,
            replayed.FinalStateHash,
            replayed.Attestation,
            enrichedMutation);

        var signedResult = AttachAuthoritySignature(enrichedResult);
        await _gossipService.MergePartialValidationAsync(runInput, signedResult, cancellationToken).ConfigureAwait(false);
    }

    public Task<OperatorAggregate?> LoadProjectedOperatorAsync(OperatorId operatorId, CancellationToken cancellationToken = default)
    {
        var events = _ledger.Entries
            .SelectMany(static entry => entry.Run?.Mutation.OperatorEvents ?? [])
            .Where(evt => evt.OperatorId == operatorId)
            .OrderBy(evt => evt.SequenceNumber)
            .ToArray();

        if (events.Length == 0)
        {
            return Task.FromResult<OperatorAggregate?>(null);
        }

        return Task.FromResult<OperatorAggregate?>(OperatorAggregate.FromEvents(events));
    }

    public Task<IReadOnlyList<OperatorId>> ListProjectedOperatorsAsync(CancellationToken cancellationToken = default)
    {
        var ids = _ledger.Entries
            .SelectMany(static entry => entry.Run?.Mutation.OperatorEvents ?? [])
            .Select(static evt => evt.OperatorId)
            .Distinct()
            .OrderBy(static id => id.Value)
            .ToArray();

        return Task.FromResult<IReadOnlyList<OperatorId>>(ids);
    }

    public Task<GameState> ProjectAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_projector.Project(_ledger.Entries));
    }

    private RunValidationResult AttachAuthoritySignature(RunValidationResult replayed)
    {
        var resultHash = replayed.Attestation.ResultHash;
        var signature = new AuthoritySignature(
            _authority.PublicKey,
            AuthorityCrypto.SignHashedPayload(_authorityPrivateKey, resultHash));

        var attestation = new SignedRunValidation(replayed.Attestation.Validation, replayed.Attestation.Certificate)
        {
            Signatures = [signature]
        };

        return new RunValidationResult(
            replayed.RunId,
            replayed.PlayerId,
            replayed.ServerId,
            replayed.FinalStateHash,
            attestation,
            replayed.Mutation);
    }

    /// <summary>
    /// Translates server-side OperatorEvents into GameplayLedgerEvents.
    /// This is used as a fallback when the RunInput contains no player Actions so that
    /// ledger entries always carry gameplay history for UI and analytics queries.
    /// Once the service fully migrates to action-based replay this path will be phased out.
    /// </summary>
    private static IReadOnlyList<GameplayLedgerEvent> BuildSemanticEvents(IEnumerable<OperatorEvent> operatorEvents)
    {
        var events = new List<GameplayLedgerEvent>();
        foreach (var evt in operatorEvents)
        {
            switch (evt)
            {
                case OperatorCreatedEvent created:
                    events.Add(new OperatorCreatedLedgerEvent(created.GetName()));
                    break;
                case XpGainedEvent xp:
                    var (amount, reason) = xp.GetPayload();
                    events.Add(new XpAwardedLedgerEvent(amount, reason));
                    break;
                case WoundsTreatedEvent healed:
                    events.Add(new PlayerHealedLedgerEvent(healed.GetHealthRestored(), "TreatWounds"));
                    break;
                case LoadoutChangedEvent loadout:
                    events.Add(new ItemAcquiredLedgerEvent(loadout.GetWeaponName()));
                    break;
                case PerkUnlockedEvent perk:
                    events.Add(new PerkUnlockedLedgerEvent(perk.GetPerkName()));
                    break;
                case CombatVictoryEvent:
                    events.Add(new RunCompletedLedgerEvent(true, "Victory"));
                    break;
                case ExfilFailedEvent failed:
                    events.Add(new RunCompletedLedgerEvent(false, failed.GetReason()));
                    break;
                case OperatorDiedEvent died:
                    events.Add(new PlayerDamagedLedgerEvent(100f, died.GetCauseOfDeath()));
                    events.Add(new RunCompletedLedgerEvent(false, died.GetCauseOfDeath()));
                    break;
                case InfilStartedEvent infilStarted:
                    var (sessionId, _, _) = infilStarted.GetPayload();
                    events.Add(new InfilStateChangedLedgerEvent("Started", "InfilStarted"));
                    events.Add(new CombatSessionLedgerEvent(sessionId, "Infil"));
                    break;
                case InfilEndedEvent infilEnded:
                    var (wasSuccessful, endedReason) = infilEnded.GetPayload();
                    events.Add(new InfilStateChangedLedgerEvent("Ended", endedReason));
                    events.Add(new RunCompletedLedgerEvent(wasSuccessful, endedReason));
                    break;
                case CombatSessionStartedEvent combatStarted:
                    events.Add(new CombatSessionLedgerEvent(combatStarted.GetPayload(), "Started"));
                    break;
                case PetActionAppliedEvent petAction:
                    var (action, health, fatigue, injury, stress, morale, hunger, hydration, _) = petAction.GetPayload();
                    events.Add(new PetStateLedgerEvent(action, health, fatigue, injury, stress, morale, hunger, hydration));
                    break;
            }
        }

        return events;
    }
}
