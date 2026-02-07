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

    public LiteDbOperatorEventStore(LiteDatabase database)
    {
        _events = (database ?? throw new ArgumentNullException(nameof(database)))
            .GetCollection<OperatorEventDocument>("operator_events");

        // Create indexes for efficient queries
        _events.EnsureIndex(x => x.OperatorId);
        _events.EnsureIndex(x => x.SequenceNumber);
    }

    public Task AppendEventAsync(OperatorEvent evt)
    {
        if (evt == null)
            throw new ArgumentNullException(nameof(evt));

        // Verify hash integrity before storing
        if (!evt.VerifyHash())
            throw new InvalidOperationException("Cannot append event with invalid hash");

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

    public Task<IReadOnlyList<OperatorEvent>> LoadEventsAsync(OperatorId operatorId)
    {
        // Load all events for this operator, ordered by sequence
        var documents = _events
            .Find(doc => doc.OperatorId == operatorId.Value)
            .OrderBy(doc => doc.SequenceNumber)
            .ToList();

        // Map to domain events and verify chain
        var events = new List<OperatorEvent>();
        OperatorEvent? previousEvent = null;

        foreach (var doc in documents)
        {
            var evt = MapToDomainEvent(doc);

            // Verify hash
            if (!evt.VerifyHash())
                throw new InvalidOperationException(
                    $"Corrupted event detected at sequence {evt.SequenceNumber}. Hash verification failed.");

            // Verify chain
            if (!evt.VerifyChain(previousEvent))
                throw new InvalidOperationException(
                    $"Event chain broken at sequence {evt.SequenceNumber}. Chain verification failed.");

            events.Add(evt);
            previousEvent = evt;
        }

        IReadOnlyList<OperatorEvent> result = events;
        return Task.FromResult(result);
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
    /// Uses FormatterServices to reconstruct events without calling constructors,
    /// preserving the original hash values from storage.
    /// </summary>
#pragma warning disable SYSLIB0050 // FormatterServices is obsolete but needed for event reconstruction
    private static OperatorEvent MapToDomainEvent(OperatorEventDocument doc)
    {
        var operatorId = OperatorId.FromGuid(doc.OperatorId);

        // Determine the concrete type based on EventType
        Type concreteType = doc.EventType switch
        {
            "OperatorCreated" => typeof(OperatorCreatedEvent),
            "XpGained" => typeof(XpGainedEvent),
            "WoundsTreated" => typeof(WoundsTreatedEvent),
            "LoadoutChanged" => typeof(LoadoutChangedEvent),
            "PerkUnlocked" => typeof(PerkUnlockedEvent),
            "ExfilSucceeded" => typeof(ExfilSucceededEvent),
            "ExfilFailed" => typeof(ExfilFailedEvent),
            "OperatorDied" => typeof(OperatorDiedEvent),
            _ => throw new InvalidOperationException($"Unknown event type: {doc.EventType}")
        };

        // Create instance without calling constructor (like serializers do)
        var instance = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(concreteType);
        
        // Set properties by directly accessing backing fields
        // Note: Coalesce null strings to empty string to handle LiteDB serialization
        SetBackingField(instance, nameof(OperatorEvent.OperatorId), operatorId);
        SetBackingField(instance, nameof(OperatorEvent.SequenceNumber), doc.SequenceNumber);
        SetBackingField(instance, nameof(OperatorEvent.EventType), doc.EventType ?? string.Empty);
        SetBackingField(instance, nameof(OperatorEvent.Payload), doc.Payload ?? string.Empty);
        SetBackingField(instance, nameof(OperatorEvent.PreviousHash), doc.PreviousHash ?? string.Empty);
        SetBackingField(instance, nameof(OperatorEvent.Hash), doc.Hash ?? string.Empty);
        SetBackingField(instance, nameof(OperatorEvent.Timestamp), doc.Timestamp);

        return (OperatorEvent)instance;
    }
#pragma warning restore SYSLIB0050

    /// <summary>
    /// Sets a backing field for an auto-property using reflection.
    /// </summary>
    private static void SetBackingField(object instance, string propertyName, object? value)
    {
        var backingField = typeof(OperatorEvent).GetField($"<{propertyName}>k__BackingField",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        if (backingField == null)
            throw new InvalidOperationException($"Could not find backing field for property {propertyName}");

        backingField.SetValue(instance, value);
    }
}
