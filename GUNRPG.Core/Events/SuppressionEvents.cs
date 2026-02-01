using GUNRPG.Core.Combat;
using GUNRPG.Core.Operators;

namespace GUNRPG.Core.Events;

/// <summary>
/// Event emitted when an operator becomes suppressed (crosses the suppression threshold).
/// </summary>
public sealed class SuppressionStartedEvent : ISimulationEvent
{
    public const int DefaultDurationMs = 2;
    public long EventTimeMs { get; }
    public Guid OperatorId { get; }
    public int SequenceNumber { get; }

    /// <summary>
    /// The target operator who became suppressed.
    /// </summary>
    public Operator Target { get; }

    /// <summary>
    /// Initial suppression severity when suppression started.
    /// </summary>
    public float InitialSeverity { get; }

    /// <summary>
    /// The shooter who caused the suppression.
    /// </summary>
    public Operator Shooter { get; }

    /// <summary>
    /// Name of the weapon that caused the suppression.
    /// </summary>
    public string WeaponName { get; }

    public SuppressionStartedEvent(
        long eventTimeMs,
        Operator target,
        Operator shooter,
        float initialSeverity,
        int sequenceNumber,
        string weaponName)
    {
        EventTimeMs = eventTimeMs;
        OperatorId = target.Id;
        Target = target;
        Shooter = shooter;
        InitialSeverity = initialSeverity;
        SequenceNumber = sequenceNumber;
        WeaponName = weaponName;
    }

    public bool Execute()
    {
        Console.WriteLine($"[{EventTimeMs}ms] {Target.Name} became suppressed by {Shooter.Name}'s {WeaponName} (severity: {InitialSeverity:F2})");
        return false; // Does not trigger reaction window
    }
}

/// <summary>
/// Event emitted when suppression level is updated (refreshed or increased).
/// </summary>
public sealed class SuppressionUpdatedEvent : ISimulationEvent
{
    public const int DefaultDurationMs = 2;
    public long EventTimeMs { get; }
    public Guid OperatorId { get; }
    public int SequenceNumber { get; }

    /// <summary>
    /// The target operator whose suppression was updated.
    /// </summary>
    public Operator Target { get; }

    /// <summary>
    /// Previous suppression level before update.
    /// </summary>
    public float PreviousLevel { get; }

    /// <summary>
    /// New suppression level after update.
    /// </summary>
    public float NewLevel { get; }

    /// <summary>
    /// The shooter who caused the suppression update.
    /// </summary>
    public Operator Shooter { get; }

    /// <summary>
    /// Name of the weapon that caused the suppression update.
    /// </summary>
    public string WeaponName { get; }

    public SuppressionUpdatedEvent(
        long eventTimeMs,
        Operator target,
        Operator shooter,
        float previousLevel,
        float newLevel,
        int sequenceNumber,
        string weaponName)
    {
        EventTimeMs = eventTimeMs;
        OperatorId = target.Id;
        Target = target;
        Shooter = shooter;
        PreviousLevel = previousLevel;
        NewLevel = newLevel;
        SequenceNumber = sequenceNumber;
        WeaponName = weaponName;
    }

    public bool Execute()
    {
        Console.WriteLine($"[{EventTimeMs}ms] {Target.Name}'s suppression updated: {PreviousLevel:F2} -> {NewLevel:F2} (by {Shooter.Name}'s {WeaponName})");
        return false; // Does not trigger reaction window
    }
}

/// <summary>
/// Event emitted when suppression ends (decays below threshold).
/// </summary>
public sealed class SuppressionEndedEvent : ISimulationEvent
{
    public const int DefaultDurationMs = 2;
    public long EventTimeMs { get; }
    public Guid OperatorId { get; }
    public int SequenceNumber { get; }

    /// <summary>
    /// The target operator whose suppression ended.
    /// </summary>
    public Operator Target { get; }

    /// <summary>
    /// Total duration of suppression in milliseconds.
    /// </summary>
    public long DurationMs { get; }

    /// <summary>
    /// Peak suppression level reached during this suppression period.
    /// </summary>
    public float PeakSeverity { get; }

    public SuppressionEndedEvent(
        long eventTimeMs,
        Operator target,
        long durationMs,
        float peakSeverity,
        int sequenceNumber)
    {
        EventTimeMs = eventTimeMs;
        OperatorId = target.Id;
        Target = target;
        DurationMs = durationMs;
        PeakSeverity = peakSeverity;
        SequenceNumber = sequenceNumber;
    }

    public bool Execute()
    {
        Console.WriteLine($"[{EventTimeMs}ms] {Target.Name}'s suppression ended (duration: {DurationMs}ms, peak: {PeakSeverity:F2})");
        return false; // Does not trigger reaction window
    }
}
