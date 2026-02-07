namespace GUNRPG.Core.Operators;

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
    /// </summary>
    /// <param name="events">Ordered events from the event store</param>
    /// <returns>Reconstituted aggregate</returns>
    /// <exception cref="InvalidOperationException">If event chain is invalid or corrupted</exception>
    public static OperatorAggregate FromEvents(IEnumerable<OperatorEvent> events)
    {
        var eventList = events.ToList();
        if (eventList.Count == 0)
            throw new InvalidOperationException("Cannot create aggregate from empty event list");

        var aggregate = new OperatorAggregate();

        OperatorEvent? previousEvent = null;
        foreach (var evt in eventList)
        {
            // Verify hash integrity
            if (!evt.VerifyHash())
                throw new InvalidOperationException(
                    $"Event hash verification failed at sequence {evt.SequenceNumber}. Possible tampering detected.");

            // Verify chain integrity
            if (!evt.VerifyChain(previousEvent))
                throw new InvalidOperationException(
                    $"Event chain broken at sequence {evt.SequenceNumber}. Expected sequence {(previousEvent?.SequenceNumber + 1 ?? 0)}");

            // Apply the event
            aggregate.ApplyEvent(evt, isNew: false);
            previousEvent = evt;
        }

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
    /// </summary>
    public void TakeCombatDamage(float damageAmount)
    {
        CurrentHealth = Math.Max(0, CurrentHealth - damageAmount);
    }
}
