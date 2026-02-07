using GUNRPG.Application.Operators;
using GUNRPG.Core.Operators;
using LiteDB;

namespace GUNRPG.Infrastructure.Persistence;

/// <summary>
/// LiteDB-backed operator event store with hash chain verification and automatic rollback.
/// </summary>
public class LiteDbOperatorEventStore : IOperatorEventStore
{
    private readonly ILiteDatabase _database;
    private const string CollectionName = "operator_events";

    public LiteDbOperatorEventStore(ILiteDatabase database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        EnsureIndexes();
    }

    private void EnsureIndexes()
    {
        var collection = _database.GetCollection<OperatorEventDocument>(CollectionName);
        collection.EnsureIndex(x => x.OperatorId);
        collection.EnsureIndex(x => x.SequenceNumber);
        collection.EnsureIndex(x => new { x.OperatorId, x.SequenceNumber }, unique: true);
    }

    public Task AppendEventAsync(OperatorEvent @event)
    {
        var collection = _database.GetCollection<OperatorEventDocument>(CollectionName);
        var document = OperatorEventDocument.FromEvent(@event);
        collection.Insert(document);
        return Task.CompletedTask;
    }

    public Task<(IReadOnlyList<OperatorEvent> Events, bool RolledBack)> LoadEventsAsync(Guid operatorId)
    {
        var collection = _database.GetCollection<OperatorEventDocument>(CollectionName);
        var documents = collection
            .Find(x => x.OperatorId == operatorId)
            .OrderBy(x => x.SequenceNumber)
            .ToList();

        if (documents.Count == 0)
        {
            return Task.FromResult<(IReadOnlyList<OperatorEvent>, bool)>((Array.Empty<OperatorEvent>(), false));
        }

        // Verify hash chain and collect valid events
        var validEvents = new List<OperatorEvent>();
        string? expectedPreviousHash = null;
        bool rolledBack = false;

        foreach (var doc in documents)
        {
            var @event = doc.ToEvent();

            // Verify hash integrity
            if (!@event.VerifyHash())
            {
                // Hash mismatch - stop here and rollback
                rolledBack = true;
                DeleteInvalidEvents(operatorId, doc.SequenceNumber);
                break;
            }

            // Verify chain linkage
            if (@event.PreviousHash != expectedPreviousHash)
            {
                // Chain broken - stop here and rollback
                rolledBack = true;
                DeleteInvalidEvents(operatorId, doc.SequenceNumber);
                break;
            }

            validEvents.Add(@event);
            expectedPreviousHash = @event.Hash;
        }

        return Task.FromResult<(IReadOnlyList<OperatorEvent>, bool)>((validEvents, rolledBack));
    }

    private void DeleteInvalidEvents(Guid operatorId, long fromSequenceNumber)
    {
        var collection = _database.GetCollection<OperatorEventDocument>(CollectionName);
        collection.DeleteMany(x => x.OperatorId == operatorId && x.SequenceNumber >= fromSequenceNumber);
    }

    public Task<long> GetCurrentSequenceNumberAsync(Guid operatorId)
    {
        var collection = _database.GetCollection<OperatorEventDocument>(CollectionName);
        var lastDoc = collection
            .Find(x => x.OperatorId == operatorId)
            .OrderByDescending(x => x.SequenceNumber)
            .FirstOrDefault();

        return Task.FromResult(lastDoc?.SequenceNumber ?? 0);
    }

    public Task<bool> ExistsAsync(Guid operatorId)
    {
        var collection = _database.GetCollection<OperatorEventDocument>(CollectionName);
        var exists = collection.Exists(x => x.OperatorId == operatorId);
        return Task.FromResult(exists);
    }
}

/// <summary>
/// LiteDB document for storing operator events.
/// Uses a discriminator pattern to store different event types in the same collection.
/// </summary>
internal class OperatorEventDocument
{
    public Guid Id { get; set; }
    public Guid OperatorId { get; set; }
    public long SequenceNumber { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string? PreviousHash { get; set; }
    public string Hash { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
    
    // Payload fields (union of all event types)
    public string? Name { get; set; }
    public int? StartingExfilStreak { get; set; }
    public Guid? CombatSessionId { get; set; }
    public int? ExperienceGained { get; set; }
    public int? NewExfilStreak { get; set; }
    public string? Reason { get; set; }
    public string? CauseOfDeath { get; set; }

    public static OperatorEventDocument FromEvent(OperatorEvent @event)
    {
        var doc = new OperatorEventDocument
        {
            Id = Guid.NewGuid(),
            OperatorId = @event.OperatorId,
            SequenceNumber = @event.SequenceNumber,
            EventType = @event.EventType,
            PreviousHash = @event.PreviousHash,
            Hash = @event.Hash,
            Timestamp = @event.Timestamp
        };

        switch (@event)
        {
            case OperatorCreated created:
                doc.Name = created.Name;
                doc.StartingExfilStreak = created.StartingExfilStreak;
                break;
            case ExfilSucceeded succeeded:
                doc.CombatSessionId = succeeded.CombatSessionId;
                doc.ExperienceGained = succeeded.ExperienceGained;
                doc.NewExfilStreak = succeeded.NewExfilStreak;
                break;
            case ExfilFailed failed:
                doc.CombatSessionId = failed.CombatSessionId;
                doc.Reason = failed.Reason;
                doc.NewExfilStreak = failed.NewExfilStreak;
                break;
            case OperatorDied died:
                doc.CombatSessionId = died.CombatSessionId;
                doc.CauseOfDeath = died.CauseOfDeath;
                doc.NewExfilStreak = died.NewExfilStreak;
                break;
        }

        return doc;
    }

    public OperatorEvent ToEvent()
    {
        return EventType switch
        {
            nameof(OperatorCreated) => new OperatorCreated
            {
                OperatorId = OperatorId,
                Name = Name ?? string.Empty,
                StartingExfilStreak = StartingExfilStreak ?? 0,
                SequenceNumber = SequenceNumber,
                PreviousHash = PreviousHash,
                Hash = Hash,
                Timestamp = Timestamp
            },
            nameof(ExfilSucceeded) => new ExfilSucceeded
            {
                OperatorId = OperatorId,
                CombatSessionId = CombatSessionId ?? Guid.Empty,
                ExperienceGained = ExperienceGained ?? 0,
                NewExfilStreak = NewExfilStreak ?? 0,
                SequenceNumber = SequenceNumber,
                PreviousHash = PreviousHash,
                Hash = Hash,
                Timestamp = Timestamp
            },
            nameof(ExfilFailed) => new ExfilFailed
            {
                OperatorId = OperatorId,
                CombatSessionId = CombatSessionId ?? Guid.Empty,
                Reason = Reason ?? string.Empty,
                NewExfilStreak = NewExfilStreak ?? 0,
                SequenceNumber = SequenceNumber,
                PreviousHash = PreviousHash,
                Hash = Hash,
                Timestamp = Timestamp
            },
            nameof(OperatorDied) => new OperatorDied
            {
                OperatorId = OperatorId,
                CombatSessionId = CombatSessionId ?? Guid.Empty,
                CauseOfDeath = CauseOfDeath ?? string.Empty,
                NewExfilStreak = NewExfilStreak ?? 0,
                SequenceNumber = SequenceNumber,
                PreviousHash = PreviousHash,
                Hash = Hash,
                Timestamp = Timestamp
            },
            _ => throw new InvalidOperationException($"Unknown event type: {EventType}")
        };
    }
}
