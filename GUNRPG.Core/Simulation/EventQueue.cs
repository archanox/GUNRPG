namespace GUNRPG.Core.Simulation;

/// <summary>
/// Deterministic in-memory event queue ordered by tick and sequence number.
/// Events are executed only when explicitly dequeued by the simulation loop.
/// </summary>
public sealed class EventQueue<TEvent>
{
    private readonly SortedSet<ScheduledEvent<TEvent>> _events = new(new ScheduledEventComparer<TEvent>());

    public int Count => _events.Count;

    public void Schedule(long tick, int sequence, TEvent value)
    {
        _events.Add(new ScheduledEvent<TEvent>(tick, sequence, value));
    }

    public ScheduledEvent<TEvent>? PeekNext() => _events.Count == 0 ? null : _events.Min;

    public ScheduledEvent<TEvent>? DequeueNext()
    {
        var next = PeekNext();
        if (next is not null)
        {
            _events.Remove(next);
        }

        return next;
    }
}

public sealed record ScheduledEvent<TEvent>(long Tick, int Sequence, TEvent Value);

internal sealed class ScheduledEventComparer<TEvent> : IComparer<ScheduledEvent<TEvent>>
{
    public int Compare(ScheduledEvent<TEvent>? x, ScheduledEvent<TEvent>? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return -1;
        }

        if (y is null)
        {
            return 1;
        }

        var tickCompare = x.Tick.CompareTo(y.Tick);
        if (tickCompare != 0)
        {
            return tickCompare;
        }

        return x.Sequence.CompareTo(y.Sequence);
    }
}
