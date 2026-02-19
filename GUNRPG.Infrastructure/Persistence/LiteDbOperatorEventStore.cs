using GUNRPG.Application.Operators;
using GUNRPG.Core.Operators;
using LiteDB;

namespace GUNRPG.Infrastructure.Persistence;

/// <summary>
/// LiteDB-backed implementation of IOperatorEventStore.
/// Stores operator events with hash chain integrity verification.
/// Thread-safe for concurrent requests.
/// </summary>
public sealed class LiteDbOperatorEventStore : IOperatorEventStore
{
    private readonly ILiteCollection<OperatorEventDocument> _events;
    private readonly LiteDatabase _database;

    public LiteDbOperatorEventStore(LiteDatabase database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _events = _database.GetCollection<OperatorEventDocument>("operator_events");

        // Create indexes for efficient queries
        _events.EnsureIndex(x => x.OperatorId);
        _events.EnsureIndex(x => x.SequenceNumber);
        // Create unique compound index with explicit name to enforce ordering
        _events.EnsureIndex("idx_op_seq", x => new { x.OperatorId, x.SequenceNumber }, true);
    }

    public Task AppendEventAsync(OperatorEvent evt)
    {
        if (evt == null)
            throw new ArgumentNullException(nameof(evt));

        // Verify hash integrity before storing
        if (!evt.VerifyHash())
            throw new InvalidOperationException("Cannot append event with invalid hash");

        // Validate sequence-0 (genesis event) invariants
        if (evt.SequenceNumber == 0)
        {
            if (evt.PreviousHash != string.Empty)
                throw new InvalidOperationException(
                    "Genesis event (sequence 0) must have empty previous hash");
        }

        // Check if this sequence already exists
        var existing = _events.FindOne(doc =>
            doc.OperatorId == evt.OperatorId.Value &&
            doc.SequenceNumber == evt.SequenceNumber);

        if (existing != null)
            throw new InvalidOperationException(
                $"Event with sequence {evt.SequenceNumber} already exists for operator {evt.OperatorId}");

        // If not the first event, verify chain integrity
        if (evt.SequenceNumber > 0)
        {
            var previousEvent = _events.FindOne(doc =>
                doc.OperatorId == evt.OperatorId.Value &&
                doc.SequenceNumber == evt.SequenceNumber - 1);

            if (previousEvent == null)
                throw new InvalidOperationException(
                    $"Cannot append event at sequence {evt.SequenceNumber}. Previous event not found.");

            if (previousEvent.Hash != evt.PreviousHash)
                throw new InvalidOperationException(
                    $"Hash chain broken at sequence {evt.SequenceNumber}. Previous hash mismatch.");
        }

        // Map to document and insert
        var document = new OperatorEventDocument
        {
            OperatorId = evt.OperatorId.Value,
            SequenceNumber = evt.SequenceNumber,
            EventType = evt.EventType,
            Payload = evt.Payload,
            PreviousHash = evt.PreviousHash,
            Hash = evt.Hash,
            Timestamp = evt.Timestamp
        };

        _events.Insert(document);
        return Task.CompletedTask;
    }

    public Task AppendEventsAsync(IReadOnlyList<OperatorEvent> events)
    {
        if (events == null)
            throw new ArgumentNullException(nameof(events));

        if (events.Count == 0)
            return Task.CompletedTask;

        // Validate all events belong to same operator
        var operatorId = events[0].OperatorId;
        if (events.Any(e => e.OperatorId != operatorId))
            throw new InvalidOperationException("All events in batch must belong to the same operator");

        // Validate events are in sequence order
        for (int i = 1; i < events.Count; i++)
        {
            if (events[i].SequenceNumber != events[i - 1].SequenceNumber + 1)
                throw new InvalidOperationException(
                    $"Events must be in sequential order. Expected sequence {events[i - 1].SequenceNumber + 1}, got {events[i].SequenceNumber}");
        }

        // Use transaction to ensure atomicity
        _database.BeginTrans();
        try
        {
            foreach (var evt in events)
            {
                // Verify hash integrity before storing
                if (!evt.VerifyHash())
                    throw new InvalidOperationException($"Cannot append event with invalid hash at sequence {evt.SequenceNumber}");

                // Validate sequence-0 (genesis event) invariants
                if (evt.SequenceNumber == 0)
                {
                    if (evt.PreviousHash != string.Empty)
                        throw new InvalidOperationException(
                            "Genesis event (sequence 0) must have empty previous hash");
                }

                // Check if this sequence already exists
                var existing = _events.FindOne(doc =>
                    doc.OperatorId == evt.OperatorId.Value &&
                    doc.SequenceNumber == evt.SequenceNumber);

                if (existing != null)
                    throw new InvalidOperationException(
                        $"Event with sequence {evt.SequenceNumber} already exists for operator {evt.OperatorId}");

                // If not the first event, verify chain integrity
                if (evt.SequenceNumber > 0)
                {
                    var previousEvent = _events.FindOne(doc =>
                        doc.OperatorId == evt.OperatorId.Value &&
                        doc.SequenceNumber == evt.SequenceNumber - 1);

                    if (previousEvent == null)
                        throw new InvalidOperationException(
                            $"Cannot append event at sequence {evt.SequenceNumber}. Previous event not found.");

                    if (previousEvent.Hash != evt.PreviousHash)
                        throw new InvalidOperationException(
                            $"Hash chain broken at sequence {evt.SequenceNumber}. Previous hash mismatch.");
                }

                // Map to document and insert
                var document = new OperatorEventDocument
                {
                    OperatorId = evt.OperatorId.Value,
                    SequenceNumber = evt.SequenceNumber,
                    EventType = evt.EventType,
                    Payload = evt.Payload,
                    PreviousHash = evt.PreviousHash,
                    Hash = evt.Hash,
                    Timestamp = evt.Timestamp
                };

                _events.Insert(document);
            }

            _database.Commit();
        }
        catch
        {
            _database.Rollback();
            throw;
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<OperatorEvent>> LoadEventsAsync(OperatorId operatorId)
    {
        // Load all events for this operator, ordered by sequence
        var documents = _events
            .Find(doc => doc.OperatorId == operatorId.Value)
            .OrderBy(doc => doc.SequenceNumber)
            .ToList();

        // Map to domain events and verify chain, with automatic rollback on corruption
        var events = new List<OperatorEvent>();
        OperatorEvent? previousEvent = null;

        foreach (var doc in documents)
        {
            var evt = MapToDomainEvent(doc);

            // Verify the rehydrated event's hash matches what was stored
            if (evt.Hash != doc.Hash)
            {
                // Corruption detected - rollback to last valid event
                // TODO: Add logging/metrics to track rollback incidents for diagnostics
                RollbackInvalidEvents(operatorId, doc.SequenceNumber);
                break;
            }

            // Verify hash computation is correct
            if (!evt.VerifyHash())
            {
                // Corruption detected - rollback to last valid event
                // TODO: Add logging/metrics to track rollback incidents for diagnostics
                RollbackInvalidEvents(operatorId, doc.SequenceNumber);
                break;
            }

            // Verify chain
            if (!evt.VerifyChain(previousEvent))
            {
                // Chain broken - rollback to last valid event
                // TODO: Add logging/metrics to track rollback incidents for diagnostics
                RollbackInvalidEvents(operatorId, doc.SequenceNumber);
                break;
            }

            events.Add(evt);
            previousEvent = evt;
        }

        IReadOnlyList<OperatorEvent> result = events;
        return Task.FromResult(result);
    }

    /// <summary>
    /// Rolls back (deletes) all events at or after the specified sequence number.
    /// This is called when hash chain verification fails to discard corrupted events.
    /// </summary>
    private void RollbackInvalidEvents(OperatorId operatorId, long fromSequence)
    {
        // Delete all events from this sequence onwards
        _events.DeleteMany(doc =>
            doc.OperatorId == operatorId.Value &&
            doc.SequenceNumber >= fromSequence);
    }

    public Task<bool> OperatorExistsAsync(OperatorId operatorId)
    {
        var exists = _events.Exists(doc => doc.OperatorId == operatorId.Value);
        return Task.FromResult(exists);
    }

    public Task<long> GetCurrentSequenceAsync(OperatorId operatorId)
    {
        var maxSequence = _events
            .Find(doc => doc.OperatorId == operatorId.Value)
            .Select(doc => doc.SequenceNumber)
            .DefaultIfEmpty(-1L)
            .Max();

        return Task.FromResult(maxSequence);
    }

    public Task<IReadOnlyList<OperatorId>> ListOperatorIdsAsync()
    {
        var operatorIds = _events
            .FindAll()
            .Select(doc => doc.OperatorId)
            .Distinct()
            .Select(guid => OperatorId.FromGuid(guid))
            .ToList();

        IReadOnlyList<OperatorId> result = operatorIds;
        return Task.FromResult(result);
    }

    /// <summary>
    /// Maps a persistence document to a domain event.
    /// Uses factory methods on concrete event types to reconstruct events from storage.
    /// </summary>
    private static OperatorEvent MapToDomainEvent(OperatorEventDocument doc)
    {
        var operatorId = OperatorId.FromGuid(doc.OperatorId);

        // Recreate the appropriate event type using their rehydration factory methods
        return doc.EventType switch
        {
            "OperatorCreated" => OperatorCreatedEvent.Rehydrate(operatorId, doc.Payload, doc.Timestamp),
            "XpGained" => XpGainedEvent.Rehydrate(operatorId, doc.SequenceNumber, doc.Payload, doc.PreviousHash, doc.Timestamp),
            "WoundsTreated" => WoundsTreatedEvent.Rehydrate(operatorId, doc.SequenceNumber, doc.Payload, doc.PreviousHash, doc.Timestamp),
            "LoadoutChanged" => LoadoutChangedEvent.Rehydrate(operatorId, doc.SequenceNumber, doc.Payload, doc.PreviousHash, doc.Timestamp),
            "PerkUnlocked" => PerkUnlockedEvent.Rehydrate(operatorId, doc.SequenceNumber, doc.Payload, doc.PreviousHash, doc.Timestamp),
            "CombatVictory" => CombatVictoryEvent.Rehydrate(operatorId, doc.SequenceNumber, doc.Payload, doc.PreviousHash, doc.Timestamp),
            "ExfilFailed" => ExfilFailedEvent.Rehydrate(operatorId, doc.SequenceNumber, doc.Payload, doc.PreviousHash, doc.Timestamp),
            "OperatorDied" => OperatorDiedEvent.Rehydrate(operatorId, doc.SequenceNumber, doc.Payload, doc.PreviousHash, doc.Timestamp),
            "InfilStarted" => InfilStartedEvent.Rehydrate(operatorId, doc.SequenceNumber, doc.Payload, doc.PreviousHash, doc.Timestamp),
            "InfilEnded" => InfilEndedEvent.Rehydrate(operatorId, doc.SequenceNumber, doc.Payload, doc.PreviousHash, doc.Timestamp),
            "CombatSessionStarted" => CombatSessionStartedEvent.Rehydrate(operatorId, doc.SequenceNumber, doc.Payload, doc.PreviousHash, doc.Timestamp),
            "PetActionApplied" => PetActionAppliedEvent.Rehydrate(operatorId, doc.SequenceNumber, doc.Payload, doc.PreviousHash, doc.Timestamp),
            _ => throw new InvalidOperationException($"Unknown event type: {doc.EventType}")
        };
    }
}
