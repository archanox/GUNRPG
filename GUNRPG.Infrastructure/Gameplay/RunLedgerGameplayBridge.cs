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
        Guid runId,
        OperatorId operatorId,
        IReadOnlyList<OperatorEvent> operatorEvents,
        IReadOnlyList<GameplayLedgerEvent>? gameplayEvents = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operatorEvents);

        var semanticEvents = gameplayEvents is { Count: > 0 }
            ? gameplayEvents.ToArray()
            : BuildSemanticEvents(operatorEvents).ToArray();
        var input = new RunInput
        {
            RunId = runId,
            PlayerId = operatorId.Value,
            Actions = [],
            Mutation = new RunLedgerMutation(operatorEvents, semanticEvents)
        };

        var replayed = _replayEngine.Replay(input);
        var signedResult = AttachAuthoritySignature(replayed);
        await _gossipService.MergePartialValidationAsync(input, signedResult, cancellationToken).ConfigureAwait(false);
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

    private static IEnumerable<GameplayLedgerEvent> BuildSemanticEvents(IEnumerable<OperatorEvent> operatorEvents)
    {
        foreach (var evt in operatorEvents)
        {
            switch (evt)
            {
                case OperatorCreatedEvent created:
                    yield return new OperatorCreatedLedgerEvent(created.GetName());
                    break;
                case XpGainedEvent xp:
                    var (amount, reason) = xp.GetPayload();
                    yield return new XpAwardedLedgerEvent(amount, reason);
                    break;
                case WoundsTreatedEvent healed:
                    yield return new PlayerHealedLedgerEvent(healed.GetHealthRestored(), "TreatWounds");
                    break;
                case LoadoutChangedEvent loadout:
                    yield return new ItemAcquiredLedgerEvent(loadout.GetWeaponName());
                    break;
                case PerkUnlockedEvent perk:
                    yield return new PerkUnlockedLedgerEvent(perk.GetPerkName());
                    break;
                case CombatVictoryEvent:
                    yield return new RunCompletedLedgerEvent(true, "Victory");
                    break;
                case ExfilFailedEvent failed:
                    yield return new RunCompletedLedgerEvent(false, failed.GetReason());
                    break;
                case OperatorDiedEvent died:
                    yield return new PlayerDamagedLedgerEvent(100f, died.GetCauseOfDeath());
                    yield return new RunCompletedLedgerEvent(false, died.GetCauseOfDeath());
                    break;
                case InfilStartedEvent infilStarted:
                    var (sessionId, lockedLoadout, infilStartTime) = infilStarted.GetPayload();
                    yield return new InfilStateChangedLedgerEvent("Started", "InfilStarted");
                    yield return new CombatSessionLedgerEvent(sessionId, "Infil");
                    _ = lockedLoadout;
                    _ = infilStartTime;
                    break;
                case InfilEndedEvent infilEnded:
                    var (wasSuccessful, endedReason) = infilEnded.GetPayload();
                    yield return new InfilStateChangedLedgerEvent("Ended", endedReason);
                    yield return new RunCompletedLedgerEvent(wasSuccessful, endedReason);
                    break;
                case CombatSessionStartedEvent combatStarted:
                    yield return new CombatSessionLedgerEvent(combatStarted.GetPayload(), "Started");
                    break;
                case PetActionAppliedEvent petAction:
                    var (action, health, fatigue, injury, stress, morale, hunger, hydration, _) = petAction.GetPayload();
                    yield return new PetStateLedgerEvent(action, health, fatigue, injury, stress, morale, hunger, hydration);
                    break;
            }
        }
    }
}
