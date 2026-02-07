using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GUNRPG.Core.Operators;

/// <summary>
/// Base class for all operator events in the event-sourced operator aggregate.
/// Events are immutable, hash-chained, and ordered by sequence number.
/// Each event contains a cryptographic hash of its contents plus the previous event's hash,
/// creating a tamper-evident chain.
/// </summary>
public abstract class OperatorEvent
{
    /// <summary>
    /// The operator this event applies to.
    /// </summary>
    public OperatorId OperatorId { get; }

    /// <summary>
    /// Sequential number of this event in the operator's event stream.
    /// Must be monotonically increasing with no gaps.
    /// </summary>
    public long SequenceNumber { get; }

    /// <summary>
    /// Discriminator for the event type (e.g., "XpGained", "WoundsTreated").
    /// </summary>
    public string EventType { get; }

    /// <summary>
    /// JSON-serialized payload containing event-specific data.
    /// </summary>
    public string Payload { get; }

    /// <summary>
    /// Hash of the previous event in the chain.
    /// Empty for the first event (sequence 0).
    /// </summary>
    public string PreviousHash { get; }

    /// <summary>
    /// Hash of this event's content (OperatorId + SequenceNumber + EventType + Payload + PreviousHash).
    /// Computed deterministically using SHA256.
    /// </summary>
    public string Hash { get; }

    /// <summary>
    /// When this event was created (UTC).
    /// </summary>
    public DateTimeOffset Timestamp { get; }

    protected OperatorEvent(
        OperatorId operatorId,
        long sequenceNumber,
        string eventType,
        string payload,
        string previousHash,
        DateTimeOffset? timestamp = null)
    {
        if (operatorId.IsEmpty)
            throw new ArgumentException("Operator ID cannot be empty", nameof(operatorId));
        
        if (sequenceNumber < 0)
            throw new ArgumentException("Sequence number must be non-negative", nameof(sequenceNumber));
        
        if (string.IsNullOrWhiteSpace(eventType))
            throw new ArgumentException("Event type cannot be empty", nameof(eventType));
        
        if (payload == null)
            throw new ArgumentNullException(nameof(payload));
        
        if (previousHash == null)
            throw new ArgumentNullException(nameof(previousHash));

        OperatorId = operatorId;
        SequenceNumber = sequenceNumber;
        EventType = eventType;
        Payload = payload;
        PreviousHash = previousHash;
        Timestamp = timestamp ?? DateTimeOffset.UtcNow;

        // Compute hash deterministically
        Hash = ComputeHash(operatorId, sequenceNumber, eventType, payload, previousHash);
    }

    /// <summary>
    /// Computes a deterministic SHA256 hash of the event contents.
    /// No cryptographic keys are used - this is for integrity verification only.
    /// </summary>
    private static string ComputeHash(
        OperatorId operatorId,
        long sequenceNumber,
        string eventType,
        string payload,
        string previousHash)
    {
        var hashInput = $"{operatorId.Value}|{sequenceNumber}|{eventType}|{payload}|{previousHash}";
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(hashInput));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <summary>
    /// Verifies that this event's hash matches its computed hash.
    /// </summary>
    public bool VerifyHash()
    {
        var computedHash = ComputeHash(OperatorId, SequenceNumber, EventType, Payload, PreviousHash);
        return Hash == computedHash;
    }

    /// <summary>
    /// Verifies that this event follows the previous event in the chain.
    /// Checks that sequence numbers are consecutive and hashes match.
    /// </summary>
    public bool VerifyChain(OperatorEvent? previousEvent)
    {
        if (previousEvent == null)
        {
            // First event: sequence must be 0, previous hash must be empty
            return SequenceNumber == 0 && PreviousHash == string.Empty;
        }

        // Subsequent events: sequence must increment by 1, previous hash must match
        return SequenceNumber == previousEvent.SequenceNumber + 1 &&
               PreviousHash == previousEvent.Hash;
    }
}

/// <summary>
/// Event emitted when an operator is created.
/// This is always the first event (sequence 0) for a new operator.
/// </summary>
public sealed class OperatorCreatedEvent : OperatorEvent
{
    public OperatorCreatedEvent(
        OperatorId operatorId,
        string name,
        DateTimeOffset? timestamp = null)
        : base(
            operatorId,
            sequenceNumber: 0,
            eventType: "OperatorCreated",
            payload: JsonSerializer.Serialize(new { Name = ValidateName(name) }),
            previousHash: string.Empty,
            timestamp: timestamp)
    {
    }

    private static string ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Operator name cannot be empty or whitespace", nameof(name));
        return name.Trim();
    }

    public string GetName() => JsonSerializer.Deserialize<CreatedPayload>(Payload)!.Name;

    /// <summary>
    /// Rehydrates an OperatorCreatedEvent from storage.
    /// </summary>
    public static OperatorCreatedEvent Rehydrate(
        OperatorId operatorId,
        string payload,
        DateTimeOffset timestamp)
    {
        var data = JsonSerializer.Deserialize<CreatedPayload>(payload)!;
        return new OperatorCreatedEvent(operatorId, data.Name, timestamp);
    }

    private record CreatedPayload(string Name);
}

/// <summary>
/// Event emitted when an operator gains experience points.
/// </summary>
public sealed class XpGainedEvent : OperatorEvent
{
    public XpGainedEvent(
        OperatorId operatorId,
        long sequenceNumber,
        long xpAmount,
        string reason,
        string previousHash,
        DateTimeOffset? timestamp = null)
        : base(
            operatorId,
            sequenceNumber,
            eventType: "XpGained",
            payload: JsonSerializer.Serialize(new { XpAmount = xpAmount, Reason = reason }),
            previousHash: previousHash,
            timestamp: timestamp)
    {
    }

    public (long XpAmount, string Reason) GetPayload()
    {
        var data = JsonSerializer.Deserialize<XpPayload>(Payload)!;
        return (data.XpAmount, data.Reason);
    }

    /// <summary>
    /// Rehydrates an XpGainedEvent from storage.
    /// </summary>
    public static XpGainedEvent Rehydrate(
        OperatorId operatorId,
        long sequenceNumber,
        string payload,
        string previousHash,
        DateTimeOffset timestamp)
    {
        var data = JsonSerializer.Deserialize<XpPayload>(payload)!;
        return new XpGainedEvent(operatorId, sequenceNumber, data.XpAmount, data.Reason, previousHash, timestamp);
    }

    private record XpPayload(long XpAmount, string Reason);
}

/// <summary>
/// Event emitted when an operator's wounds are treated (health restored).
/// </summary>
public sealed class WoundsTreatedEvent : OperatorEvent
{
    public WoundsTreatedEvent(
        OperatorId operatorId,
        long sequenceNumber,
        float healthRestored,
        string previousHash,
        DateTimeOffset? timestamp = null)
        : base(
            operatorId,
            sequenceNumber,
            eventType: "WoundsTreated",
            payload: JsonSerializer.Serialize(new { HealthRestored = healthRestored }),
            previousHash: previousHash,
            timestamp: timestamp)
    {
    }

    public float GetHealthRestored()
    {
        var data = JsonSerializer.Deserialize<WoundsPayload>(Payload)!;
        return data.HealthRestored;
    }

    /// <summary>
    /// Rehydrates a WoundsTreatedEvent from storage.
    /// </summary>
    public static WoundsTreatedEvent Rehydrate(
        OperatorId operatorId,
        long sequenceNumber,
        string payload,
        string previousHash,
        DateTimeOffset timestamp)
    {
        var data = JsonSerializer.Deserialize<WoundsPayload>(payload)!;
        return new WoundsTreatedEvent(operatorId, sequenceNumber, data.HealthRestored, previousHash, timestamp);
    }

    private record WoundsPayload(float HealthRestored);
}

/// <summary>
/// Event emitted when an operator's loadout is changed.
/// </summary>
public sealed class LoadoutChangedEvent : OperatorEvent
{
    public LoadoutChangedEvent(
        OperatorId operatorId,
        long sequenceNumber,
        string weaponName,
        string previousHash,
        DateTimeOffset? timestamp = null)
        : base(
            operatorId,
            sequenceNumber,
            eventType: "LoadoutChanged",
            payload: JsonSerializer.Serialize(new { WeaponName = weaponName }),
            previousHash: previousHash,
            timestamp: timestamp)
    {
    }

    public string GetWeaponName()
    {
        var data = JsonSerializer.Deserialize<LoadoutPayload>(Payload)!;
        return data.WeaponName;
    }

    /// <summary>
    /// Rehydrates a LoadoutChangedEvent from storage.
    /// </summary>
    public static LoadoutChangedEvent Rehydrate(
        OperatorId operatorId,
        long sequenceNumber,
        string payload,
        string previousHash,
        DateTimeOffset timestamp)
    {
        var data = JsonSerializer.Deserialize<LoadoutPayload>(payload)!;
        return new LoadoutChangedEvent(operatorId, sequenceNumber, data.WeaponName, previousHash, timestamp);
    }

    private record LoadoutPayload(string WeaponName);
}

/// <summary>
/// Event emitted when an operator unlocks a new perk or skill.
/// </summary>
public sealed class PerkUnlockedEvent : OperatorEvent
{
    public PerkUnlockedEvent(
        OperatorId operatorId,
        long sequenceNumber,
        string perkName,
        string previousHash,
        DateTimeOffset? timestamp = null)
        : base(
            operatorId,
            sequenceNumber,
            eventType: "PerkUnlocked",
            payload: JsonSerializer.Serialize(new { PerkName = perkName }),
            previousHash: previousHash,
            timestamp: timestamp)
    {
    }

    public string GetPerkName()
    {
        var data = JsonSerializer.Deserialize<PerkPayload>(Payload)!;
        return data.PerkName;
    }

    /// <summary>
    /// Rehydrates a PerkUnlockedEvent from storage.
    /// </summary>
    public static PerkUnlockedEvent Rehydrate(
        OperatorId operatorId,
        long sequenceNumber,
        string payload,
        string previousHash,
        DateTimeOffset timestamp)
    {
        var data = JsonSerializer.Deserialize<PerkPayload>(payload)!;
        return new PerkUnlockedEvent(operatorId, sequenceNumber, data.PerkName, previousHash, timestamp);
    }

    private record PerkPayload(string PerkName);
}
