namespace GUNRPG.Core.Operators;

/// <summary>
/// Operator aggregate root that derives its state from an event stream.
/// State is append-only and can only be modified by applying events.
/// </summary>
public class OperatorAggregate
{
    /// <summary>
    /// Unique identifier for this operator.
    /// </summary>
    public Guid Id { get; private set; }

    /// <summary>
    /// Operator's name.
    /// </summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// Current exfil streak (number of consecutive successful exfils).
    /// </summary>
    public int ExfilStreak { get; private set; }

    /// <summary>
    /// Total experience earned by this operator.
    /// </summary>
    public int TotalExperience { get; private set; }

    /// <summary>
    /// Number of successful exfils completed.
    /// </summary>
    public int SuccessfulExfils { get; private set; }

    /// <summary>
    /// Number of failed exfils.
    /// </summary>
    public int FailedExfils { get; private set; }

    /// <summary>
    /// Number of times this operator has died.
    /// </summary>
    public int Deaths { get; private set; }

    /// <summary>
    /// Whether this operator is currently alive (not permanently dead).
    /// </summary>
    public bool IsAlive { get; private set; } = true;

    /// <summary>
    /// Current sequence number (last event applied).
    /// </summary>
    public long CurrentSequenceNumber { get; private set; }

    /// <summary>
    /// Hash of the last applied event.
    /// </summary>
    public string? LastEventHash { get; private set; }

    /// <summary>
    /// Timestamp of the last applied event.
    /// </summary>
    public DateTimeOffset? LastEventTimestamp { get; private set; }

    /// <summary>
    /// Creates a new operator aggregate by replaying events.
    /// </summary>
    public static OperatorAggregate FromEvents(IEnumerable<OperatorEvent> events)
    {
        var aggregate = new OperatorAggregate();
        foreach (var @event in events)
        {
            aggregate.Apply(@event);
        }
        return aggregate;
    }

    /// <summary>
    /// Applies an event to this aggregate, updating its state.
    /// </summary>
    public void Apply(OperatorEvent @event)
    {
        switch (@event)
        {
            case OperatorCreated created:
                ApplyOperatorCreated(created);
                break;
            case ExfilSucceeded succeeded:
                ApplyExfilSucceeded(succeeded);
                break;
            case ExfilFailed failed:
                ApplyExfilFailed(failed);
                break;
            case OperatorDied died:
                ApplyOperatorDied(died);
                break;
            default:
                throw new InvalidOperationException($"Unknown event type: {@event.GetType().Name}");
        }

        CurrentSequenceNumber = @event.SequenceNumber;
        LastEventHash = @event.Hash;
        LastEventTimestamp = @event.Timestamp;
    }

    private void ApplyOperatorCreated(OperatorCreated @event)
    {
        Id = @event.OperatorId;
        Name = @event.Name;
        ExfilStreak = @event.StartingExfilStreak;
        TotalExperience = 0;
        SuccessfulExfils = 0;
        FailedExfils = 0;
        Deaths = 0;
        IsAlive = true;
    }

    private void ApplyExfilSucceeded(ExfilSucceeded @event)
    {
        ExfilStreak = @event.NewExfilStreak;
        TotalExperience += @event.ExperienceGained;
        SuccessfulExfils++;
    }

    private void ApplyExfilFailed(ExfilFailed @event)
    {
        ExfilStreak = @event.NewExfilStreak;
        FailedExfils++;
    }

    private void ApplyOperatorDied(OperatorDied @event)
    {
        ExfilStreak = @event.NewExfilStreak;
        Deaths++;
        // Note: IsAlive remains true - death is tracked but doesn't permanently disable the operator
        // This allows for resurrection mechanics or treating death as a setback rather than permanent
    }

    /// <summary>
    /// Gets a read-only snapshot of the operator's combat state for use in combat sessions.
    /// This is the interface between the event-sourced aggregate and the combat system.
    /// </summary>
    public OperatorCombatSnapshot GetCombatSnapshot()
    {
        return new OperatorCombatSnapshot
        {
            OperatorId = Id,
            Name = Name,
            ExfilStreak = ExfilStreak,
            TotalExperience = TotalExperience
        };
    }
}

/// <summary>
/// Read-only snapshot of operator state for use in combat sessions.
/// Combat sessions use this snapshot and never mutate the operator aggregate directly.
/// </summary>
public class OperatorCombatSnapshot
{
    public Guid OperatorId { get; init; }
    public string Name { get; init; } = string.Empty;
    public int ExfilStreak { get; init; }
    public int TotalExperience { get; init; }
}
