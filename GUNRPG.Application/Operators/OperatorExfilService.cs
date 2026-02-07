using GUNRPG.Application.Combat;
using GUNRPG.Application.Results;
using GUNRPG.Core.Operators;

namespace GUNRPG.Application.Operators;

/// <summary>
/// Application service for operator exfil (out-of-combat) operations.
/// This is the ONLY place where operator events may be committed.
/// Acts as a transactional boundary between combat (infil) and operator state.
/// 
/// Responsibilities:
/// - Load operators by replaying events
/// - Validate exfil-only actions
/// - Append new operator events
/// - Persist events atomically
/// - Process combat outcomes
/// </summary>
public sealed class OperatorExfilService
{
    private readonly IOperatorEventStore _eventStore;

    public OperatorExfilService(IOperatorEventStore eventStore)
    {
        _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
    }

    /// <summary>
    /// Creates a new operator and persists the creation event.
    /// </summary>
    public async Task<ServiceResult<OperatorId>> CreateOperatorAsync(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return ServiceResult<OperatorId>.ValidationError("Operator name cannot be empty");

        var operatorId = OperatorId.NewId();
        var createdEvent = new OperatorCreatedEvent(operatorId, name);

        try
        {
            await _eventStore.AppendEventAsync(createdEvent);
            return ServiceResult<OperatorId>.Success(operatorId);
        }
        catch (Exception ex)
        {
            return ServiceResult<OperatorId>.InvalidState($"Failed to create operator: {ex.Message}");
        }
    }

    /// <summary>
    /// Loads an operator by replaying all events from the event store.
    /// Verifies hash chain integrity during load.
    /// </summary>
    public async Task<ServiceResult<OperatorAggregate>> LoadOperatorAsync(OperatorId operatorId)
    {
        try
        {
            var events = await _eventStore.LoadEventsAsync(operatorId);
            if (events.Count == 0)
                return ServiceResult<OperatorAggregate>.NotFound($"Operator {operatorId} not found");

            var aggregate = OperatorAggregate.FromEvents(events);
            return ServiceResult<OperatorAggregate>.Success(aggregate);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("hash") || ex.Message.Contains("chain"))
        {
            // Hash chain corruption detected
            return ServiceResult<OperatorAggregate>.InvalidState($"Operator data corrupted: {ex.Message}");
        }
        catch (Exception ex)
        {
            return ServiceResult<OperatorAggregate>.InvalidState($"Failed to load operator: {ex.Message}");
        }
    }

    /// <summary>
    /// Applies experience points to an operator (exfil-only action).
    /// </summary>
    public async Task<ServiceResult> ApplyXpAsync(OperatorId operatorId, long xpAmount, string reason)
    {
        if (xpAmount <= 0)
            return ServiceResult.ValidationError("XP amount must be positive");

        if (string.IsNullOrWhiteSpace(reason))
            return ServiceResult.ValidationError("XP reason cannot be empty");

        var loadResult = await LoadOperatorAsync(operatorId);
        if (!loadResult.IsSuccess)
            return MapLoadResultStatus(loadResult);

        var aggregate = loadResult.Value!;
        var previousHash = aggregate.GetLastEventHash();
        var sequenceNumber = aggregate.CurrentSequence + 1;

        var xpEvent = new XpGainedEvent(
            operatorId,
            sequenceNumber,
            xpAmount,
            reason,
            previousHash);

        try
        {
            await _eventStore.AppendEventAsync(xpEvent);
            return ServiceResult.Success();
        }
        catch (Exception ex)
        {
            return ServiceResult.InvalidState($"Failed to apply XP: {ex.Message}");
        }
    }

    /// <summary>
    /// Treats wounds (restores health) for an operator (exfil-only action).
    /// </summary>
    public async Task<ServiceResult> TreatWoundsAsync(OperatorId operatorId, float healthAmount)
    {
        if (healthAmount <= 0)
            return ServiceResult.ValidationError("Health amount must be positive");

        var loadResult = await LoadOperatorAsync(operatorId);
        if (!loadResult.IsSuccess)
            return MapLoadResultStatus(loadResult);

        var aggregate = loadResult.Value!;
        var previousHash = aggregate.GetLastEventHash();
        var sequenceNumber = aggregate.CurrentSequence + 1;

        var woundsEvent = new WoundsTreatedEvent(
            operatorId,
            sequenceNumber,
            healthAmount,
            previousHash);

        try
        {
            await _eventStore.AppendEventAsync(woundsEvent);
            return ServiceResult.Success();
        }
        catch (Exception ex)
        {
            return ServiceResult.InvalidState($"Failed to treat wounds: {ex.Message}");
        }
    }

    /// <summary>
    /// Changes an operator's loadout (exfil-only action).
    /// </summary>
    public async Task<ServiceResult> ChangeLoadoutAsync(OperatorId operatorId, string weaponName)
    {
        if (string.IsNullOrWhiteSpace(weaponName))
            return ServiceResult.ValidationError("Weapon name cannot be empty");

        var loadResult = await LoadOperatorAsync(operatorId);
        if (!loadResult.IsSuccess)
            return MapLoadResultStatus(loadResult);

        var aggregate = loadResult.Value!;
        var previousHash = aggregate.GetLastEventHash();
        var sequenceNumber = aggregate.CurrentSequence + 1;

        var loadoutEvent = new LoadoutChangedEvent(
            operatorId,
            sequenceNumber,
            weaponName,
            previousHash);

        try
        {
            await _eventStore.AppendEventAsync(loadoutEvent);
            return ServiceResult.Success();
        }
        catch (Exception ex)
        {
            return ServiceResult.InvalidState($"Failed to change loadout: {ex.Message}");
        }
    }

    /// <summary>
    /// Unlocks a perk for an operator (exfil-only action).
    /// </summary>
    public async Task<ServiceResult> UnlockPerkAsync(OperatorId operatorId, string perkName)
    {
        if (string.IsNullOrWhiteSpace(perkName))
            return ServiceResult.ValidationError("Perk name cannot be empty");

        var loadResult = await LoadOperatorAsync(operatorId);
        if (!loadResult.IsSuccess)
            return MapLoadResultStatus(loadResult);

        var aggregate = loadResult.Value!;

        // Check if perk already unlocked
        if (aggregate.UnlockedPerks.Contains(perkName))
            return ServiceResult.ValidationError($"Perk '{perkName}' already unlocked");

        var previousHash = aggregate.GetLastEventHash();
        var sequenceNumber = aggregate.CurrentSequence + 1;

        var perkEvent = new PerkUnlockedEvent(
            operatorId,
            sequenceNumber,
            perkName,
            previousHash);

        try
        {
            await _eventStore.AppendEventAsync(perkEvent);
            return ServiceResult.Success();
        }
        catch (Exception ex)
        {
            return ServiceResult.InvalidState($"Failed to unlock perk: {ex.Message}");
        }
    }

    /// <summary>
    /// Processes a combat outcome and applies the results to an operator.
    /// This is the primary boundary between infil (combat) and exfil (operator management).
    /// 
    /// The outcome is accepted from combat, but the player can review/modify before committing.
    /// Once committed, this method translates the outcome into operator events.
    /// </summary>
    public async Task<ServiceResult> ProcessCombatOutcomeAsync(CombatOutcome outcome, bool playerConfirmed = true)
    {
        if (outcome == null)
            return ServiceResult.ValidationError("Combat outcome cannot be null");

        if (!playerConfirmed)
            return ServiceResult.ValidationError("Combat outcome must be confirmed before processing");

        var loadResult = await LoadOperatorAsync(outcome.OperatorId);
        if (!loadResult.IsSuccess)
            return MapLoadResultStatus(loadResult);

        // Apply XP if earned
        if (outcome.XpEarned > 0)
        {
            var xpResult = await ApplyXpAsync(outcome.OperatorId, outcome.XpEarned, outcome.XpReason);
            if (!xpResult.IsSuccess)
                return xpResult;
        }

        // If operator survived but took damage, update health state
        // Note: We don't emit health events here yet - that would be added when we have
        // a HealthChangedEvent or we could treat full healing in exfil as a separate action

        return ServiceResult.Success();
    }

    /// <summary>
    /// Lists all known operator IDs.
    /// </summary>
    public async Task<ServiceResult<IReadOnlyList<OperatorId>>> ListOperatorsAsync()
    {
        try
        {
            var operatorIds = await _eventStore.ListOperatorIdsAsync();
            return ServiceResult<IReadOnlyList<OperatorId>>.Success(operatorIds);
        }
        catch (Exception ex)
        {
            return ServiceResult<IReadOnlyList<OperatorId>>.InvalidState($"Failed to list operators: {ex.Message}");
        }
    }

    /// <summary>
    /// Checks if an operator exists.
    /// </summary>
    public async Task<bool> OperatorExistsAsync(OperatorId operatorId)
    {
        return await _eventStore.OperatorExistsAsync(operatorId);
    }

    /// <summary>
    /// Maps a ServiceResult&lt;OperatorAggregate&gt; to a ServiceResult,
    /// preserving the original ResultStatus (NotFound/ValidationError/InvalidState).
    /// </summary>
    private static ServiceResult MapLoadResultStatus(ServiceResult<OperatorAggregate> loadResult)
    {
        return loadResult.Status switch
        {
            ResultStatus.NotFound => ServiceResult.NotFound(loadResult.ErrorMessage!),
            ResultStatus.ValidationError => ServiceResult.ValidationError(loadResult.ErrorMessage!),
            ResultStatus.InvalidState => ServiceResult.InvalidState(loadResult.ErrorMessage!),
            _ => ServiceResult.InvalidState(loadResult.ErrorMessage!)
        };
    }
}
