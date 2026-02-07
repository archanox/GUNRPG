using GUNRPG.Application.Results;
using GUNRPG.Core.Operators;

namespace GUNRPG.Application.Operators;

/// <summary>
/// Service for managing operator exfil operations.
/// This is the ONLY service allowed to emit operator events.
/// Enforces strict boundary between combat (infil) and exfil phases.
/// </summary>
public class OperatorExfilService
{
    private readonly IOperatorEventStore _eventStore;

    public OperatorExfilService(IOperatorEventStore eventStore)
    {
        _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
    }

    /// <summary>
    /// Creates a new operator.
    /// </summary>
    public async Task<ServiceResult<Guid>> CreateOperatorAsync(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return ServiceResult<Guid>.ValidationError("Operator name cannot be empty.");
        }

        var operatorId = Guid.NewGuid();
        var @event = new OperatorCreated(operatorId, name);
        
        await _eventStore.AppendEventAsync(@event);

        return ServiceResult<Guid>.Success(operatorId);
    }

    /// <summary>
    /// Loads an operator aggregate from its event stream.
    /// Automatically rolls back to last valid event if hash chain is broken.
    /// </summary>
    public async Task<ServiceResult<OperatorAggregate>> LoadOperatorAsync(Guid operatorId)
    {
        var (events, rolledBack) = await _eventStore.LoadEventsAsync(operatorId);

        if (events.Count == 0)
        {
            return ServiceResult<OperatorAggregate>.NotFound($"Operator {operatorId} not found.");
        }

        var aggregate = OperatorAggregate.FromEvents(events);

        if (rolledBack)
        {
            // Log warning about rollback (in production, this should be logged)
            // For now, we just include it in the result
        }

        return ServiceResult<OperatorAggregate>.Success(aggregate);
    }

    /// <summary>
    /// Commits a successful exfil, incrementing the streak.
    /// This is the only way to modify operator state after combat.
    /// </summary>
    public async Task<ServiceResult<OperatorAggregate>> CommitSuccessfulExfilAsync(
        Guid operatorId, 
        Guid combatSessionId,
        int experienceGained)
    {
        var loadResult = await LoadOperatorAsync(operatorId);
        if (!loadResult.IsSuccess)
        {
            return ServiceResult<OperatorAggregate>.NotFound(loadResult.ErrorMessage ?? "Operator not found.");
        }

        var aggregate = loadResult.Value!;
        var newStreak = aggregate.ExfilStreak + 1;
        var sequenceNumber = await _eventStore.GetCurrentSequenceNumberAsync(operatorId) + 1;

        var @event = new ExfilSucceeded(
            operatorId,
            combatSessionId,
            experienceGained,
            newStreak,
            sequenceNumber,
            aggregate.LastEventHash);

        await _eventStore.AppendEventAsync(@event);

        // Reload to get updated state
        return await LoadOperatorAsync(operatorId);
    }

    /// <summary>
    /// Records a failed exfil, resetting the streak to zero.
    /// </summary>
    public async Task<ServiceResult<OperatorAggregate>> CommitFailedExfilAsync(
        Guid operatorId,
        Guid combatSessionId,
        string reason)
    {
        var loadResult = await LoadOperatorAsync(operatorId);
        if (!loadResult.IsSuccess)
        {
            return ServiceResult<OperatorAggregate>.NotFound(loadResult.ErrorMessage ?? "Operator not found.");
        }

        var aggregate = loadResult.Value!;
        var newStreak = 0; // Reset streak on failed exfil
        var sequenceNumber = await _eventStore.GetCurrentSequenceNumberAsync(operatorId) + 1;

        var @event = new ExfilFailed(
            operatorId,
            combatSessionId,
            reason,
            newStreak,
            sequenceNumber,
            aggregate.LastEventHash);

        await _eventStore.AppendEventAsync(@event);

        // Reload to get updated state
        return await LoadOperatorAsync(operatorId);
    }

    /// <summary>
    /// Records operator death, resetting the streak to zero.
    /// </summary>
    public async Task<ServiceResult<OperatorAggregate>> RecordOperatorDeathAsync(
        Guid operatorId,
        Guid combatSessionId,
        string causeOfDeath)
    {
        var loadResult = await LoadOperatorAsync(operatorId);
        if (!loadResult.IsSuccess)
        {
            return ServiceResult<OperatorAggregate>.NotFound(loadResult.ErrorMessage ?? "Operator not found.");
        }

        var aggregate = loadResult.Value!;
        var newStreak = 0; // Reset streak on death
        var sequenceNumber = await _eventStore.GetCurrentSequenceNumberAsync(operatorId) + 1;

        var @event = new OperatorDied(
            operatorId,
            combatSessionId,
            causeOfDeath,
            newStreak,
            sequenceNumber,
            aggregate.LastEventHash);

        await _eventStore.AppendEventAsync(@event);

        // Reload to get updated state
        return await LoadOperatorAsync(operatorId);
    }

    /// <summary>
    /// Gets a read-only combat snapshot for use in combat sessions.
    /// Combat sessions use this snapshot and never mutate the operator aggregate.
    /// </summary>
    public async Task<ServiceResult<OperatorCombatSnapshot>> GetCombatSnapshotAsync(Guid operatorId)
    {
        var loadResult = await LoadOperatorAsync(operatorId);
        if (!loadResult.IsSuccess)
        {
            return ServiceResult<OperatorCombatSnapshot>.NotFound(loadResult.ErrorMessage ?? "Operator not found.");
        }

        var snapshot = loadResult.Value!.GetCombatSnapshot();
        return ServiceResult<OperatorCombatSnapshot>.Success(snapshot);
    }
}
