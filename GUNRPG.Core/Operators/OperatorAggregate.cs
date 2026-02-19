namespace GUNRPG.Core.Operators;

using GUNRPG.Core.VirtualPet;

/// <summary>
/// Event-sourced aggregate representing a long-lived operator character.
/// State is derived entirely by replaying events from the event store.
/// This aggregate represents progression, identity, and long-term state.
/// 
/// IMPORTANT: This is the exfil-only representation of an operator.
/// Combat sessions use a copy of operator stats, not this aggregate directly.
/// </summary>
public sealed class OperatorAggregate
{
    private readonly List<OperatorEvent> _events = new();

    /// <summary>
    /// Unique identifier for this operator.
    /// </summary>
    public OperatorId Id { get; private set; }

    /// <summary>
    /// Operator's display name.
    /// </summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// Total experience points earned.
    /// </summary>
    public long TotalXp { get; private set; }

    /// <summary>
    /// Current health (out of combat). Max is derived from level/progression.
    /// </summary>
    public float CurrentHealth { get; private set; }

    /// <summary>
    /// Maximum health based on progression.
    /// </summary>
    public float MaxHealth { get; private set; }

    /// <summary>
    /// Currently equipped weapon name.
    /// </summary>
    public string EquippedWeaponName { get; private set; } = string.Empty;

    /// <summary>
    /// List of unlocked perks/skills.
    /// </summary>
    public IReadOnlyList<string> UnlockedPerks { get; private set; } = new List<string>();

    /// <summary>
    /// Number of consecutive successful exfils.
    /// Increments on ExfilSucceeded, resets on ExfilFailed, OperatorDied, or rollback.
    /// </summary>
    public int ExfilStreak { get; private set; }

    /// <summary>
    /// Whether this operator is dead.
    /// Once dead, no further state changes are allowed (enforced at service level).
    /// </summary>
    public bool IsDead { get; private set; }

    /// <summary>
    /// Current operational mode (Base or Infil).
    /// </summary>
    public OperatorMode CurrentMode { get; private set; }

    /// <summary>
    /// Time when the current infil started. Null if not in infil mode.
    /// Used to enforce the 30-minute infil timer.
    /// </summary>
    public DateTimeOffset? InfilStartTime { get; private set; }

    /// <summary>
    /// Unique identifier for the current infil session. Persists across multiple combats during a single infil.
    /// Null if not in Infil mode. Set when infil starts, cleared when infil ends.
    /// </summary>
    public Guid? InfilSessionId { get; private set; }

    /// <summary>
    /// Active combat session ID for the current combat encounter. Null if not in active combat or in Base mode.
    /// This can be null even when in Infil mode (between combats after victory).
    /// </summary>
    public Guid? ActiveCombatSessionId { get; private set; }

    /// <summary>
    /// Locked loadout snapshot when in Infil mode. Empty if in Base mode.
    /// Loadout is locked during infil to prevent mid-mission changes.
    /// </summary>
    public string LockedLoadout { get; private set; } = string.Empty;

    /// <summary>
    /// Virtual pet state for this operator.
    /// Tracks health, fatigue, stress, morale, hunger, hydration, and injury,
    /// along with the last updated timestamp. Updated through pet actions and background decay.
    /// </summary>
    public PetState? PetState { get; private set; }

    /// <summary>
    /// Current sequence number (number of events applied).
    /// </summary>
    public long CurrentSequence => _events.Count - 1;

    /// <summary>
    /// All events that have been applied to this aggregate.
    /// </summary>
    public IReadOnlyList<OperatorEvent> Events => _events.AsReadOnly();

    /// <summary>
    /// Creates a new operator aggregate from a creation event.
    /// </summary>
    public static OperatorAggregate Create(OperatorCreatedEvent createdEvent)
    {
        var aggregate = new OperatorAggregate();
        aggregate.ApplyEvent(createdEvent, isNew: true);
        return aggregate;
    }

    /// <summary>
    /// Reconstitutes an operator aggregate by replaying events from the event store.
    /// Verifies hash chain integrity during replay.
    /// If verification fails, rolls back to the last valid event.
    /// </summary>
    /// <param name="events">Ordered events from the event store</param>
    /// <returns>Reconstituted aggregate</returns>
    /// <exception cref="InvalidOperationException">If no valid events exist</exception>
    public static OperatorAggregate FromEvents(IEnumerable<OperatorEvent> events)
    {
        var eventList = events.ToList();
        if (eventList.Count == 0)
            throw new InvalidOperationException("Cannot create aggregate from empty event list");

        var aggregate = new OperatorAggregate();
        var validEvents = new List<OperatorEvent>();

        OperatorEvent? previousEvent = null;
        foreach (var evt in eventList)
        {
            // Verify hash integrity
            if (!evt.VerifyHash())
            {
                // Hash verification failed - stop here and use only valid events up to this point
                break;
            }

            // Verify chain integrity
            if (!evt.VerifyChain(previousEvent))
            {
                // Chain broken - stop here and use only valid events up to this point
                break;
            }

            // Event is valid - apply it
            validEvents.Add(evt);
            aggregate.ApplyEvent(evt, isNew: false);
            previousEvent = evt;
        }

        // Must have at least one valid event
        if (validEvents.Count == 0)
            throw new InvalidOperationException("No valid events found - first event failed verification");

        return aggregate;
    }

    /// <summary>
    /// Applies a new event to this aggregate.
    /// For new events, adds to the pending changes. For historical events, just updates state.
    /// </summary>
    private void ApplyEvent(OperatorEvent evt, bool isNew)
    {
        // Apply state change based on event type
        switch (evt)
        {
            case OperatorCreatedEvent created:
                Id = created.OperatorId;
                Name = created.GetName();
                TotalXp = 0;
                MaxHealth = 100f; // Default starting health
                CurrentHealth = MaxHealth;
                EquippedWeaponName = string.Empty;
                UnlockedPerks = new List<string>();
                ExfilStreak = 0;
                IsDead = false;
                CurrentMode = OperatorMode.Base; // Operators start in Base mode
                InfilStartTime = null;
                InfilSessionId = null;
                ActiveCombatSessionId = null;
                LockedLoadout = string.Empty;
                // Initialize pet state with healthy defaults
                PetState = new PetState(
                    OperatorId: Id.Value,
                    Health: 100f,
                    Fatigue: 0f,
                    Injury: 0f,
                    Stress: 0f,
                    Morale: 100f,
                    Hunger: 0f,
                    Hydration: 100f,
                    LastUpdated: created.Timestamp
                );
                break;

            case XpGainedEvent xpGained:
                var (xpAmount, _) = xpGained.GetPayload();
                TotalXp += xpAmount;
                break;

            case WoundsTreatedEvent woundsTreated:
                var healthRestored = woundsTreated.GetHealthRestored();
                CurrentHealth = Math.Min(MaxHealth, CurrentHealth + healthRestored);
                break;

            case LoadoutChangedEvent loadoutChanged:
                EquippedWeaponName = loadoutChanged.GetWeaponName();
                break;

            case PerkUnlockedEvent perkUnlocked:
                var perkName = perkUnlocked.GetPerkName();
                var perks = new List<string>(UnlockedPerks) { perkName };
                UnlockedPerks = perks;
                break;

            case ExfilSucceededEvent:
                // Clear active combat session since this combat is complete
                // Operator stays in Infil mode with InfilSessionId intact to allow consecutive combats
                // Note: ExfilStreak is NOT incremented here - it only increments on successful infil completion
                ActiveCombatSessionId = null;
                break;

            case ExfilFailedEvent:
                ExfilStreak = 0;
                break;

            case OperatorDiedEvent:
                // Operator "died" in mission but is respawned at base with full health
                // This allows continued gameplay after mission failure
                IsDead = false; // Allow operator to continue after respawn
                CurrentHealth = MaxHealth;
                ExfilStreak = 0;
                // Death automatically ends infil if active
                if (CurrentMode == OperatorMode.Infil)
                {
                    CurrentMode = OperatorMode.Base;
                    InfilStartTime = null;
                    InfilSessionId = null;
                    ActiveCombatSessionId = null;
                    LockedLoadout = string.Empty;
                }
                break;

            case InfilStartedEvent infilStarted:
                var (infilSessionId, lockedLoadout, infilStartTime) = infilStarted.GetPayload();
                CurrentMode = OperatorMode.Infil;
                InfilStartTime = infilStartTime;
                InfilSessionId = infilSessionId;
                // ActiveCombatSessionId is NOT set here - combat sessions are created separately
                LockedLoadout = lockedLoadout;
                break;

            case InfilEndedEvent infilEnded:
                CurrentMode = OperatorMode.Base;
                InfilStartTime = null;
                InfilSessionId = null;
                ActiveCombatSessionId = null;
                var (wasSuccessful, _) = infilEnded.GetPayload();
                if (wasSuccessful)
                {
                    // Increment exfil streak on successful infil completion
                    ExfilStreak++;
                    // On success, preserve loadout
                    LockedLoadout = string.Empty;
                }
                else
                {
                    // On failure, clear loadout (gear loss) and reset streak
                    ExfilStreak = 0;
                    LockedLoadout = string.Empty;
                    EquippedWeaponName = string.Empty;
                }
                break;

            case CombatSessionStartedEvent combatSessionStarted:
                var combatSessionId = combatSessionStarted.GetPayload();
                ActiveCombatSessionId = combatSessionId;
                break;

            case PetActionAppliedEvent petAction:
                var (_, health, fatigue, injury, stress, morale, hunger, hydration, lastUpdated) = petAction.GetPayload();
                PetState = new PetState(
                    OperatorId: Id.Value,
                    Health: health,
                    Fatigue: fatigue,
                    Injury: injury,
                    Stress: stress,
                    Morale: morale,
                    Hunger: hunger,
                    Hydration: hydration,
                    LastUpdated: lastUpdated
                );
                break;

            default:
                throw new InvalidOperationException($"Unknown event type: {evt.EventType}");
        }

        // Add to event list
        _events.Add(evt);
    }

    /// <summary>
    /// Gets the hash of the most recent event, used for chaining new events.
    /// Returns empty string if no events exist yet.
    /// </summary>
    public string GetLastEventHash()
    {
        return _events.Count > 0 ? _events[^1].Hash : string.Empty;
    }

    /// <summary>
    /// Applies a pet action to the operator's virtual pet.
    /// Only allowed in Base mode. Updates pet state via event sourcing.
    /// </summary>
    /// <param name="input">The pet action to apply (Rest, Eat, Drink)</param>
    /// <param name="now">Current timestamp for calculating decay and tracking update time</param>
    /// <returns>The new pet action applied event</returns>
    public PetActionAppliedEvent ApplyPetAction(PetInput input, DateTimeOffset now)
    {
        if (CurrentMode != OperatorMode.Base)
        {
            throw new InvalidOperationException("Pet actions can only be applied in Base mode");
        }

        if (IsDead)
        {
            throw new InvalidOperationException("Cannot apply pet actions to a dead operator");
        }

        if (PetState == null)
        {
            throw new InvalidOperationException("Operator has no pet state");
        }

        // Apply the pet action using the pure rules engine
        var newPetState = PetRules.Apply(PetState, input, now);

        // Determine action name for event tracking
        string actionName = input switch
        {
            RestInput => "rest",
            EatInput => "eat",
            DrinkInput => "drink",
            _ => throw new InvalidOperationException("Unsupported pet input type for pet action")
        };

        // Create and apply the event
        var evt = new PetActionAppliedEvent(
            Id,
            CurrentSequence + 1,
            actionName,
            newPetState.Health,
            newPetState.Fatigue,
            newPetState.Injury,
            newPetState.Stress,
            newPetState.Morale,
            newPetState.Hunger,
            newPetState.Hydration,
            newPetState.LastUpdated,
            GetLastEventHash(),
            now
        );

        ApplyEvent(evt, isNew: true);
        return evt;
    }

    /// <summary>
    /// Creates a copy of this operator's stats for use in combat (infil).
    /// Combat works with a snapshot and never mutates the aggregate directly.
    /// </summary>
    public Operator CreateCombatSnapshot()
    {
        var combatOp = new Operator(Name, Id.Value)
        {
            MaxHealth = MaxHealth,
            Health = CurrentHealth
            // Additional stats can be mapped here as the system evolves
        };

        return combatOp;
    }

    /// <summary>
    /// Applies damage taken during combat. This should only be called during exfil
    /// after reviewing combat outcomes. 
    /// INTERNAL: This method mutates state without emitting an event.
    /// Use with caution - prefer emitting a proper event when available.
    /// </summary>
    internal void TakeCombatDamage(float damageAmount)
    {
        CurrentHealth = Math.Max(0, CurrentHealth - damageAmount);
    }
}
