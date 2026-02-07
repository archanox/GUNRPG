using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GUNRPG.Core.Operators;

/// <summary>
/// Base class for all operator events in the event-sourced operator aggregate.
/// Events are append-only, hash-chained, and tamper-evident.
/// </summary>
public abstract class OperatorEvent
{
    /// <summary>
    /// Unique identifier of the operator this event belongs to.
    /// </summary>
    public Guid OperatorId { get; init; }

    /// <summary>
    /// Monotonically increasing sequence number for this operator's event stream.
    /// </summary>
    public long SequenceNumber { get; init; }

    /// <summary>
    /// Type of event (derived from class name).
    /// </summary>
    public string EventType => GetType().Name;

    /// <summary>
    /// SHA256 hash of the previous event in the chain.
    /// Null for the first event (genesis).
    /// </summary>
    public string? PreviousHash { get; init; }

    /// <summary>
    /// SHA256 hash of this event's content.
    /// </summary>
    public string Hash { get; set; }

    /// <summary>
    /// Timestamp when this event was created.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; }

    protected OperatorEvent(Guid operatorId, long sequenceNumber, string? previousHash)
    {
        OperatorId = operatorId;
        SequenceNumber = sequenceNumber;
        PreviousHash = previousHash;
        Timestamp = DateTimeOffset.UtcNow;
        Hash = string.Empty; // Will be computed by derived class
    }

    // Parameterless constructor for deserialization
    protected OperatorEvent()
    {
        Hash = string.Empty; // Will be set by init
    }

    /// <summary>
    /// Computes the SHA256 hash of this event's content.
    /// Must be called by derived classes after all properties are set.
    /// </summary>
    protected string ComputeHash()
    {
        var content = GetHashContent();
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(hashBytes);
    }

    /// <summary>
    /// Verifies that this event's hash matches its content.
    /// </summary>
    public bool VerifyHash()
    {
        var expectedHash = ComputeHash();
        return Hash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Gets the string content to hash. Override to include event-specific data.
    /// </summary>
    protected virtual string GetHashContent()
    {
        return $"{OperatorId}|{SequenceNumber}|{EventType}|{PreviousHash ?? "GENESIS"}|{Timestamp:O}|{GetPayloadJson()}";
    }

    /// <summary>
    /// Gets the JSON representation of the event payload for hashing.
    /// Override to include event-specific data.
    /// </summary>
    protected abstract string GetPayloadJson();
}

/// <summary>
/// Event emitted when an operator is created.
/// </summary>
public class OperatorCreated : OperatorEvent
{
    public string Name { get; init; }
    public int StartingExfilStreak { get; init; }

    public OperatorCreated(Guid operatorId, string name, long sequenceNumber = 1, string? previousHash = null)
        : base(operatorId, sequenceNumber, previousHash)
    {
        Name = name;
        StartingExfilStreak = 0;
        Hash = ComputeHash(); // Compute after all properties are set
    }

    // Parameterless constructor for deserialization
    public OperatorCreated() : base()
    {
        Name = string.Empty;
    }

    protected override string GetPayloadJson()
    {
        return JsonSerializer.Serialize(new { Name, StartingExfilStreak });
    }
}

/// <summary>
/// Event emitted when an operator successfully completes exfil.
/// </summary>
public class ExfilSucceeded : OperatorEvent
{
    public Guid CombatSessionId { get; init; }
    public int ExperienceGained { get; init; }
    public int NewExfilStreak { get; init; }

    public ExfilSucceeded(Guid operatorId, Guid combatSessionId, int experienceGained, int newExfilStreak, 
        long sequenceNumber, string? previousHash)
        : base(operatorId, sequenceNumber, previousHash)
    {
        CombatSessionId = combatSessionId;
        ExperienceGained = experienceGained;
        NewExfilStreak = newExfilStreak;
        Hash = ComputeHash(); // Compute after all properties are set
    }

    // Parameterless constructor for deserialization
    public ExfilSucceeded() : base()
    {
    }

    protected override string GetPayloadJson()
    {
        return JsonSerializer.Serialize(new { CombatSessionId, ExperienceGained, NewExfilStreak });
    }
}

/// <summary>
/// Event emitted when an operator fails exfil (abandoned or failed to extract).
/// </summary>
public class ExfilFailed : OperatorEvent
{
    public Guid CombatSessionId { get; init; }
    public string Reason { get; init; }
    public int NewExfilStreak { get; init; }

    public ExfilFailed(Guid operatorId, Guid combatSessionId, string reason, int newExfilStreak, 
        long sequenceNumber, string? previousHash)
        : base(operatorId, sequenceNumber, previousHash)
    {
        CombatSessionId = combatSessionId;
        Reason = reason;
        NewExfilStreak = newExfilStreak;
        Hash = ComputeHash(); // Compute after all properties are set
    }

    // Parameterless constructor for deserialization
    public ExfilFailed() : base()
    {
        Reason = string.Empty;
    }

    protected override string GetPayloadJson()
    {
        return JsonSerializer.Serialize(new { CombatSessionId, Reason, NewExfilStreak });
    }
}

/// <summary>
/// Event emitted when an operator dies during combat.
/// </summary>
public class OperatorDied : OperatorEvent
{
    public Guid CombatSessionId { get; init; }
    public string CauseOfDeath { get; init; }
    public int NewExfilStreak { get; init; }

    public OperatorDied(Guid operatorId, Guid combatSessionId, string causeOfDeath, int newExfilStreak, 
        long sequenceNumber, string? previousHash)
        : base(operatorId, sequenceNumber, previousHash)
    {
        CombatSessionId = combatSessionId;
        CauseOfDeath = causeOfDeath;
        NewExfilStreak = newExfilStreak;
        Hash = ComputeHash(); // Compute after all properties are set
    }

    // Parameterless constructor for deserialization
    public OperatorDied() : base()
    {
        CauseOfDeath = string.Empty;
    }

    protected override string GetPayloadJson()
    {
        return JsonSerializer.Serialize(new { CombatSessionId, CauseOfDeath, NewExfilStreak });
    }
}
