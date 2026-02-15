using System.Collections.Concurrent;
using GUNRPG.Core.Operators;

namespace GUNRPG.Application.Operators;

/// <summary>
/// In-memory implementation of IOperatorEventStore for testing and InMemory configuration.
/// This is a simple no-op store that returns empty event lists.
/// </summary>
public sealed class InMemoryOperatorEventStore : IOperatorEventStore
{
    private readonly ConcurrentDictionary<OperatorId, List<OperatorEvent>> _eventsByOperator = new();

    public Task<IReadOnlyList<OperatorEvent>> LoadEventsAsync(OperatorId operatorId)
    {
        if (_eventsByOperator.TryGetValue(operatorId, out var events))
        {
            return Task.FromResult<IReadOnlyList<OperatorEvent>>(events);
        }
        return Task.FromResult<IReadOnlyList<OperatorEvent>>(new List<OperatorEvent>());
    }

    public Task AppendEventAsync(OperatorEvent evt)
    {
        var operatorId = evt.OperatorId;
        _eventsByOperator.AddOrUpdate(
            operatorId,
            _ => new List<OperatorEvent> { evt },
            (_, events) => { events.Add(evt); return events; });
        return Task.CompletedTask;
    }

    public Task AppendEventsAsync(IReadOnlyList<OperatorEvent> events)
    {
        if (events.Count == 0) return Task.CompletedTask;
        
        var operatorId = events[0].OperatorId;
        _eventsByOperator.AddOrUpdate(
            operatorId,
            _ => new List<OperatorEvent>(events),
            (_, existingEvents) => { existingEvents.AddRange(events); return existingEvents; });
        return Task.CompletedTask;
    }

    public Task<bool> OperatorExistsAsync(OperatorId operatorId)
    {
        return Task.FromResult(_eventsByOperator.ContainsKey(operatorId) && _eventsByOperator[operatorId].Count > 0);
    }

    public Task<long> GetCurrentSequenceAsync(OperatorId operatorId)
    {
        if (_eventsByOperator.TryGetValue(operatorId, out var events) && events.Count > 0)
        {
            return Task.FromResult(events[^1].SequenceNumber);
        }
        return Task.FromResult(-1L);
    }

    public Task<IReadOnlyList<OperatorId>> ListOperatorIdsAsync()
    {
        var operatorIds = _eventsByOperator.Keys.ToList();
        return Task.FromResult<IReadOnlyList<OperatorId>>(operatorIds);
    }
}
