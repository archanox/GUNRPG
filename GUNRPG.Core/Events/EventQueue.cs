namespace GUNRPG.Core.Events;

/// <summary>
/// Priority queue for simulation events.
/// Events are ordered by timestamp, then by operator ID, then by sequence number.
/// </summary>
public class EventQueue
{
    private readonly SortedSet<ISimulationEvent> _events;
    private int _nextSequenceNumber = 0;

    public EventQueue()
    {
        _events = new SortedSet<ISimulationEvent>(new EventComparer());
    }

    /// <summary>
    /// Gets the number of events in the queue.
    /// </summary>
    public int Count => _events.Count;

    /// <summary>
    /// Schedules an event for execution.
    /// </summary>
    public void Schedule(ISimulationEvent evt)
    {
        _events.Add(evt);
    }

    /// <summary>
    /// Peeks at the next event without removing it.
    /// </summary>
    public ISimulationEvent? PeekNext()
    {
        return _events.FirstOrDefault();
    }

    /// <summary>
    /// Dequeues the next event.
    /// </summary>
    public ISimulationEvent? DequeueNext()
    {
        var next = PeekNext();
        if (next != null)
        {
            _events.Remove(next);
        }
        return next;
    }

    /// <summary>
    /// Removes all events for a specific operator.
    /// Used when cancelling intents.
    /// </summary>
    public void RemoveEventsForOperator(Guid operatorId)
    {
        _events.RemoveWhere(e => e.OperatorId == operatorId);
    }

    /// <summary>
    /// Clears all events from the queue.
    /// </summary>
    public void Clear()
    {
        _events.Clear();
        _nextSequenceNumber = 0;
    }

    /// <summary>
    /// Clears all events except for damage/miss events (bullets in flight).
    /// Used when starting a new planning phase to preserve in-flight projectiles.
    /// </summary>
    public void ClearExceptInFlightBullets()
    {
        var inFlightEvents = _events
            .Where(e => e is DamageAppliedEvent || e is ShotMissedEvent)
            .ToList();
        
        _events.Clear();
        
        foreach (var evt in inFlightEvents)
        {
            _events.Add(evt);
        }
        
        // Don't reset sequence number - keep continuity
    }

    /// <summary>
    /// Gets the next sequence number for deterministic ordering.
    /// </summary>
    public int GetNextSequenceNumber()
    {
        return _nextSequenceNumber++;
    }

    private class EventComparer : IComparer<ISimulationEvent>
    {
        public int Compare(ISimulationEvent? x, ISimulationEvent? y)
        {
            if (x == null || y == null)
                return 0;

            // First by timestamp
            int timeCompare = x.EventTimeMs.CompareTo(y.EventTimeMs);
            if (timeCompare != 0)
                return timeCompare;

            // Then by operator ID for consistency
            int operatorCompare = x.OperatorId.CompareTo(y.OperatorId);
            if (operatorCompare != 0)
                return operatorCompare;

            // Finally by sequence number
            return x.SequenceNumber.CompareTo(y.SequenceNumber);
        }
    }
}
