using GUNRPG.Application.Operators;
using GUNRPG.Core.Operators;

namespace GUNRPG.Tests.Stubs;

/// <summary>
/// Stub implementation of IOperatorEventStore for testing.
/// Allows tests to specify operator mode and control validation behavior.
/// </summary>
public class StubOperatorEventStore : IOperatorEventStore
{
    private readonly Dictionary<OperatorId, List<OperatorEvent>> _eventsByOperator = new();

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
        throw new NotImplementedException("AppendEventAsync not needed for current tests");
    }

    public Task AppendEventsAsync(IReadOnlyList<OperatorEvent> events)
    {
        throw new NotImplementedException("AppendEventsAsync not needed for current tests");
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

    /// <summary>
    /// Sets up an operator with the specified mode for testing.
    /// </summary>
    public void SetupOperatorWithMode(OperatorId operatorId, OperatorMode mode, string operatorName = "TestOperator", Guid? activeCombatSessionId = null)
    {
        var events = new List<OperatorEvent>
        {
            new OperatorCreatedEvent(operatorId, operatorName)
        };

        // Add InfilStartedEvent if mode is Infil
        if (mode == OperatorMode.Infil)
        {
            var createHash = events[0].Hash;
            events.Add(new InfilStartedEvent(
                operatorId, 
                1, 
                Guid.NewGuid(), 
                "SOKOL 545", // lockedLoadout
                DateTimeOffset.UtcNow, // infilStartTime
                createHash));

            // Add CombatSessionStartedEvent if an active combat session ID is specified
            if (activeCombatSessionId.HasValue)
            {
                var infilHash = events[^1].Hash;
                events.Add(new CombatSessionStartedEvent(
                    operatorId,
                    2,
                    activeCombatSessionId.Value,
                    infilHash));
            }
        }

        _eventsByOperator[operatorId] = events;
    }

    /// <summary>
    /// Sets up an operator that doesn't exist (returns empty event list).
    /// </summary>
    public void SetupNonExistentOperator(OperatorId operatorId)
    {
        _eventsByOperator[operatorId] = new List<OperatorEvent>();
    }
}
