using GUNRPG.Application.Combat;
using GUNRPG.Application.Dtos;
using GUNRPG.Application.Requests;
using GUNRPG.Application.Results;
using GUNRPG.Core.Operators;
using GUNRPG.Core.VirtualPet;

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

        // Dead operators cannot gain XP
        if (aggregate.IsDead)
            return ServiceResult.InvalidState("Cannot apply XP to dead operator");

        // Must be in Base mode
        if (aggregate.CurrentMode != OperatorMode.Base)
            return ServiceResult.InvalidState("Cannot apply XP while in Infil mode");

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

        // Dead operators cannot be healed
        if (aggregate.IsDead)
            return ServiceResult.InvalidState("Cannot treat wounds on dead operator");

        // Must be in Base mode
        if (aggregate.CurrentMode != OperatorMode.Base)
            return ServiceResult.InvalidState("Cannot treat wounds while in Infil mode");

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

        // Dead operators cannot change loadout
        if (aggregate.IsDead)
            return ServiceResult.InvalidState("Cannot change loadout for dead operator");

        // Must be in Base mode - loadout is locked during infil
        if (aggregate.CurrentMode != OperatorMode.Base)
            return ServiceResult.InvalidState("Cannot change loadout while in Infil mode - loadout is locked");

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

        // Dead operators cannot have perks unlocked
        if (aggregate.IsDead)
            return ServiceResult.InvalidState("Cannot unlock perk for dead operator");

        // Must be in Base mode
        if (aggregate.CurrentMode != OperatorMode.Base)
            return ServiceResult.InvalidState("Cannot unlock perk while in Infil mode");

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
    /// Marks an exfil as successful, incrementing the operator's exfil streak.
    /// DEPRECATED for combat-based exfil: Use ProcessCombatOutcomeAsync instead.
    /// 
    /// This method is kept for backward compatibility and non-combat exfil scenarios.
    /// Note: This method does NOT transition the operator back to Base mode.
    /// For combat exfil, ProcessCombatOutcomeAsync handles the complete workflow including mode transition.
    /// </summary>
    public async Task<ServiceResult> CompleteExfilAsync(OperatorId operatorId)
    {
        var loadResult = await LoadOperatorAsync(operatorId);
        if (!loadResult.IsSuccess)
            return MapLoadResultStatus(loadResult);

        var aggregate = loadResult.Value!;

        // Dead operators cannot complete exfil
        if (aggregate.IsDead)
            return ServiceResult.InvalidState("Dead operators cannot complete exfil");

        // Must be in Infil mode to complete exfil
        if (aggregate.CurrentMode != OperatorMode.Infil)
            return ServiceResult.InvalidState("Cannot complete exfil when not in Infil mode");

        var previousHash = aggregate.GetLastEventHash();
        var sequenceNumber = aggregate.CurrentSequence + 1;

        var exfilEvent = new ExfilSucceededEvent(
            operatorId,
            sequenceNumber,
            previousHash);

        try
        {
            await _eventStore.AppendEventAsync(exfilEvent);
            return ServiceResult.Success();
        }
        catch (Exception ex)
        {
            return ServiceResult.InvalidState($"Failed to complete exfil: {ex.Message}");
        }
    }

    /// <summary>
    /// Marks an exfil as failed, resetting the operator's exfil streak.
    /// Used when operator retreats, abandons mission, or fails to extract.
    /// </summary>
    public async Task<ServiceResult> FailExfilAsync(OperatorId operatorId, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return ServiceResult.ValidationError("Exfil failure reason cannot be empty");

        var loadResult = await LoadOperatorAsync(operatorId);
        if (!loadResult.IsSuccess)
            return MapLoadResultStatus(loadResult);

        var aggregate = loadResult.Value!;

        // Dead operators don't need exfil failure recorded (death event already resets streak)
        if (aggregate.IsDead)
            return ServiceResult.InvalidState("Dead operators cannot fail exfil - already dead");

        var previousHash = aggregate.GetLastEventHash();
        var sequenceNumber = aggregate.CurrentSequence + 1;

        var exfilEvent = new ExfilFailedEvent(
            operatorId,
            sequenceNumber,
            reason,
            previousHash);

        try
        {
            await _eventStore.AppendEventAsync(exfilEvent);
            return ServiceResult.Success();
        }
        catch (Exception ex)
        {
            return ServiceResult.InvalidState($"Failed to record exfil failure: {ex.Message}");
        }
    }

    /// <summary>
    /// Starts an infil operation, transitioning the operator from Base mode to Infil mode.
    /// Locks the operator's loadout and starts the 30-minute timer.
    /// Returns the session ID to associate with the combat session.
    /// </summary>
    public async Task<ServiceResult<Guid>> StartInfilAsync(OperatorId operatorId)
    {
        var loadResult = await LoadOperatorAsync(operatorId);
        if (!loadResult.IsSuccess)
            return ServiceResult<Guid>.FromResult(MapLoadResultStatus(loadResult));

        var aggregate = loadResult.Value!;

        // Dead operators cannot start infil
        if (aggregate.IsDead)
            return ServiceResult<Guid>.InvalidState("Dead operators cannot start infil");

        // Must be in Base mode to start infil
        if (aggregate.CurrentMode != OperatorMode.Base)
            return ServiceResult<Guid>.InvalidState("Operator is already in Infil mode");

        var sessionId = Guid.NewGuid();
        var infilStartTime = DateTimeOffset.UtcNow;
        var lockedLoadout = aggregate.EquippedWeaponName; // Lock current loadout

        var previousHash = aggregate.GetLastEventHash();
        var sequenceNumber = aggregate.CurrentSequence + 1;

        var infilEvent = new InfilStartedEvent(
            operatorId,
            sequenceNumber,
            sessionId,
            lockedLoadout,
            infilStartTime,
            previousHash);

        try
        {
            await _eventStore.AppendEventAsync(infilEvent);
            return ServiceResult<Guid>.Success(sessionId);
        }
        catch (Exception ex)
        {
            return ServiceResult<Guid>.InvalidState($"Failed to start infil: {ex.Message}");
        }
    }

    /// <summary>
    /// Fails an infil operation due to timeout or other reasons.
    /// Clears the locked loadout (gear loss), resets streak, and returns operator to Base mode.
    /// </summary>
    public async Task<ServiceResult> FailInfilAsync(OperatorId operatorId, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return ServiceResult.ValidationError("Infil failure reason cannot be empty");

        var loadResult = await LoadOperatorAsync(operatorId);
        if (!loadResult.IsSuccess)
            return MapLoadResultStatus(loadResult);

        var aggregate = loadResult.Value!;

        // Must be in Infil mode to fail infil
        if (aggregate.CurrentMode != OperatorMode.Infil)
            return ServiceResult.InvalidState("Cannot fail infil when not in Infil mode");

        var previousHash = aggregate.GetLastEventHash();
        var sequenceNumber = aggregate.CurrentSequence + 1;

        // Build events to append atomically: ExfilFailed + InfilEnded
        var eventsToAppend = new List<OperatorEvent>();

        var exfilFailedEvent = new ExfilFailedEvent(
            operatorId,
            sequenceNumber,
            reason,
            previousHash);
        eventsToAppend.Add(exfilFailedEvent);

        var infilEndedEvent = new InfilEndedEvent(
            operatorId,
            sequenceNumber + 1,
            wasSuccessful: false,
            reason: reason,
            previousHash: exfilFailedEvent.Hash);
        eventsToAppend.Add(infilEndedEvent);

        try
        {
            await _eventStore.AppendEventsAsync(eventsToAppend);
            return ServiceResult.Success();
        }
        catch (Exception ex)
        {
            return ServiceResult.InvalidState($"Failed to fail infil: {ex.Message}");
        }
    }

    /// <summary>
    /// Checks if the operator's infil has timed out (exceeded 30 minutes).
    /// Returns true if the operator is in infil mode and the timer has expired.
    /// </summary>
    public async Task<ServiceResult<bool>> IsInfilTimedOutAsync(OperatorId operatorId)
    {
        var loadResult = await LoadOperatorAsync(operatorId);
        if (!loadResult.IsSuccess)
            return ServiceResult<bool>.FromResult(MapLoadResultStatus(loadResult));

        var aggregate = loadResult.Value!;

        // Not in infil mode, so can't be timed out
        if (aggregate.CurrentMode != OperatorMode.Infil)
            return ServiceResult<bool>.Success(false);

        if (!aggregate.InfilStartTime.HasValue)
            return ServiceResult<bool>.Success(false);

        var elapsed = DateTimeOffset.UtcNow - aggregate.InfilStartTime.Value;
        var isTimedOut = elapsed.TotalMinutes >= 30;

        return ServiceResult<bool>.Success(isTimedOut);
    }

    /// <summary>
    /// Marks an operator as dead. This is permanent and resets the exfil streak.
    /// Once dead, no further state changes are allowed for this operator.
    /// </summary>
    public async Task<ServiceResult> KillOperatorAsync(OperatorId operatorId, string causeOfDeath)
    {
        if (string.IsNullOrWhiteSpace(causeOfDeath))
            return ServiceResult.ValidationError("Cause of death cannot be empty");

        var loadResult = await LoadOperatorAsync(operatorId);
        if (!loadResult.IsSuccess)
            return MapLoadResultStatus(loadResult);

        var aggregate = loadResult.Value!;

        // Cannot kill an already-dead operator
        if (aggregate.IsDead)
            return ServiceResult.InvalidState("Operator is already dead");

        var previousHash = aggregate.GetLastEventHash();
        var sequenceNumber = aggregate.CurrentSequence + 1;

        var deathEvent = new OperatorDiedEvent(
            operatorId,
            sequenceNumber,
            causeOfDeath,
            previousHash);

        try
        {
            await _eventStore.AppendEventAsync(deathEvent);
            return ServiceResult.Success();
        }
        catch (Exception ex)
        {
            return ServiceResult.InvalidState($"Failed to record operator death: {ex.Message}");
        }
    }

    /// <summary>
    /// Processes a combat outcome and applies the results to an operator.
    /// This is the primary boundary between infil (combat) and exfil (operator management).
    /// 
    /// The outcome is accepted from combat, but the player can review/modify before committing.
    /// Once committed, this method translates the outcome into operator events atomically.
    /// 
    /// Exfil Semantics:
    /// - Operator must be in Infil mode to process combat outcome
    /// - If operator died: Emit OperatorDied + InfilEnded (failure) events (resets streak, marks IsDead, no XP awarded)
    /// - If operator survived and is victorious: Apply XP (if any), emit ExfilSucceeded + InfilEnded (success) (increments streak)
    /// - If operator survived but retreated/failed: Apply XP (if any), emit no exfil events (neutral outcome)
    /// - If infil timer expired (30+ minutes), automatically fail the infil
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

        var aggregate = loadResult.Value!;

        // Already dead operators cannot process new outcomes
        if (aggregate.IsDead)
            return ServiceResult.InvalidState("Cannot process combat outcome for dead operator");

        // Must be in Infil mode to process combat outcome
        if (aggregate.CurrentMode != OperatorMode.Infil)
            return ServiceResult.InvalidState("Cannot process combat outcome when not in Infil mode");

        // Session ID must match the active infil session
        if (aggregate.ActiveSessionId == null)
            return ServiceResult.InvalidState("Cannot process combat outcome without an active infil session");

        if (!Equals(aggregate.ActiveSessionId, outcome.SessionId))
            return ServiceResult.ValidationError("Combat outcome session does not match active infil session");

        // Check if infil timer has expired (30 minutes)
        if (aggregate.InfilStartTime.HasValue)
        {
            var elapsed = DateTimeOffset.UtcNow - aggregate.InfilStartTime.Value;
            if (elapsed.TotalMinutes >= 30)
            {
                // Timer expired - automatically fail the infil
                return await FailInfilAsync(outcome.OperatorId, "Infil timer expired (30 minutes)");
            }
        }

        // Build event list to append atomically
        var eventsToAppend = new List<OperatorEvent>();
        var previousHash = aggregate.GetLastEventHash();
        var nextSequence = aggregate.CurrentSequence + 1;

        // If operator died in combat, emit death event + infil ended (failure)
        if (outcome.OperatorDied)
        {
            var deathEvent = new OperatorDiedEvent(
                outcome.OperatorId,
                nextSequence,
                "Killed in combat",
                previousHash);
            
            eventsToAppend.Add(deathEvent);
            previousHash = deathEvent.Hash;
            nextSequence++;

            // End infil as failure (death automatically transitions to Base mode in aggregate)
            var infilEndedEvent = new InfilEndedEvent(
                outcome.OperatorId,
                nextSequence,
                wasSuccessful: false,
                reason: "Operator died in combat",
                previousHash: previousHash);
            
            eventsToAppend.Add(infilEndedEvent);
        }
        else
        {
            // Operator survived - emit XP event if earned
            if (outcome.XpGained > 0)
            {
                // Derive XP reason from outcome properties
                var xpReason = outcome.IsVictory ? "Victory" : "Survival";
                
                var xpEvent = new XpGainedEvent(
                    outcome.OperatorId,
                    nextSequence,
                    outcome.XpGained,
                    xpReason,
                    previousHash);
                
                eventsToAppend.Add(xpEvent);
                previousHash = xpEvent.Hash;
                nextSequence++;
            }

            // If operator achieved victory, emit exfil success event and end infil successfully
            if (outcome.IsVictory)
            {
                var exfilEvent = new ExfilSucceededEvent(
                    outcome.OperatorId,
                    nextSequence,
                    previousHash);
                
                eventsToAppend.Add(exfilEvent);
                previousHash = exfilEvent.Hash;
                nextSequence++;

                // End infil as success
                var infilEndedEvent = new InfilEndedEvent(
                    outcome.OperatorId,
                    nextSequence,
                    wasSuccessful: true,
                    reason: "Exfil succeeded",
                    previousHash: previousHash);
                
                eventsToAppend.Add(infilEndedEvent);
            }
        }

        // Append all events atomically
        if (eventsToAppend.Count > 0)
        {
            try
            {
                await _eventStore.AppendEventsAsync(eventsToAppend);
            }
            catch (Exception ex)
            {
                return ServiceResult.InvalidState($"Failed to process combat outcome: {ex.Message}");
            }
        }

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
    /// Applies a pet action to an operator's virtual pet.
    /// Only allowed in Base mode. Updates pet state via event sourcing.
    /// </summary>
    public async Task<ServiceResult> ApplyPetActionAsync(
        OperatorId operatorId,
        PetActionRequest request)
    {
        var loadResult = await LoadOperatorAsync(operatorId);
        if (!loadResult.IsSuccess)
            return MapLoadResultStatus(loadResult);

        var aggregate = loadResult.Value!;

        // Validate operator state
        if (aggregate.CurrentMode != OperatorMode.Base)
        {
            return ServiceResult.ValidationError(
                "Pet actions can only be performed in Base mode, not during infil");
        }

        if (aggregate.IsDead)
        {
            return ServiceResult.ValidationError(
                "Cannot apply pet actions to a dead operator");
        }

        if (aggregate.PetState == null)
        {
            return ServiceResult.InvalidState(
                "Operator has no pet state");
        }

        // Parse the pet action from the request
        PetInput petInput;
        try
        {
            petInput = ParsePetInput(request);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return ServiceResult.ValidationError(ex.Message);
        }
        catch (ArgumentException ex)
        {
            return ServiceResult.ValidationError(ex.Message);
        }

        // Apply the pet action to the aggregate (creates event)
        try
        {
            var evt = aggregate.ApplyPetAction(petInput, DateTimeOffset.UtcNow);
            await _eventStore.AppendEventAsync(evt);
        }
        catch (InvalidOperationException ex)
        {
            return ServiceResult.InvalidState(ex.Message);
        }

        return ServiceResult.Success();
    }

    private static PetInput ParsePetInput(PetActionRequest request)
    {
        var action = request.Action?.Trim().ToLowerInvariant() ?? "rest";
        return action switch
        {
            "rest" => CreateRestInput(request),
            "eat" => CreateEatInput(request),
            "drink" => CreateDrinkInput(request),
            _ => throw new ArgumentException($"Unknown pet action: {request.Action}")
        };
    }

    private static RestInput CreateRestInput(PetActionRequest request)
    {
        var hours = request.Hours ?? 1f;

        if (hours <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(request.Hours), hours, "Rest hours must be greater than zero.");
        }

        // Optionally cap the maximum rest duration to a reasonable upper bound.
        if (hours > 24f)
        {
            hours = 24f;
        }

        return new RestInput(TimeSpan.FromHours(hours));
    }

    private static EatInput CreateEatInput(PetActionRequest request)
    {
        var nutrition = request.Nutrition ?? 30f;

        if (nutrition <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(request.Nutrition), nutrition, "Nutrition amount must be greater than zero.");
        }

        // Optionally cap the maximum nutrition amount to prevent unrealistic changes.
        if (nutrition > 100f)
        {
            nutrition = 100f;
        }

        return new EatInput(nutrition);
    }

    private static DrinkInput CreateDrinkInput(PetActionRequest request)
    {
        var hydration = request.Hydration ?? 30f;

        if (hydration <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(request.Hydration), hydration, "Hydration amount must be greater than zero.");
        }

        // Optionally cap the maximum hydration amount to prevent unrealistic changes.
        if (hydration > 100f)
        {
            hydration = 100f;
        }

        return new DrinkInput(hydration);
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
