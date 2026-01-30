using GUNRPG.Core.Combat;
using GUNRPG.Core.Operators;
using GUNRPG.Core.Weapons;

namespace GUNRPG.Core.Events;

/// <summary>
/// Event fired when a shot is taken.
/// This is a commitment unit event that may trigger a reaction window.
/// </summary>
public class ShotFiredEvent : ISimulationEvent
{
    public long EventTimeMs { get; }
    public Guid OperatorId { get; }
    public int SequenceNumber { get; }
    
    private readonly Operator _shooter;
    private readonly Operator _target;
    private readonly Random _random;
    private readonly EventQueue? _eventQueue;

    public ShotFiredEvent(long eventTimeMs, Operator shooter, Operator target, int sequenceNumber, Random random, EventQueue? eventQueue = null)
    {
        EventTimeMs = eventTimeMs;
        OperatorId = shooter.Id;
        SequenceNumber = sequenceNumber;
        _shooter = shooter;
        _target = target;
        _random = random;
        _eventQueue = eventQueue;
    }

    public bool Execute()
    {
        // Validate can still fire
        if (_shooter.CurrentAmmo <= 0 || _shooter.WeaponState != WeaponState.Ready)
            return false;

        // Consume ammo
        _shooter.CurrentAmmo--;
        
        // Get weapon name for logging
        string weaponName = _shooter.EquippedWeapon?.Name ?? "unknown weapon";

        // Always log when the shot is fired
        Console.WriteLine($"[{EventTimeMs}ms] {_shooter.Name} fired {weaponName}");

        // Use new HitResolution system
        var weapon = _shooter.EquippedWeapon;
        if (weapon == null)
        {
            Console.WriteLine($"[{EventTimeMs}ms] {_shooter.Name} has no equipped weapon");
            return false;
        }

        // Determine target body part based on aim state
        // In ADS or transitioning to ADS, aim for head; in hipfire, aim for center mass
        BodyPart targetBodyPart = (_shooter.AimState == AimState.ADS || _shooter.AimState == AimState.TransitioningToADS)
            ? BodyPart.Head 
            : BodyPart.UpperTorso;

        // Scale weapon recoil to fit within the angular band system (0-1 degree range)
        // Original recoil values (0.4-0.6°) are tuned for a different system
        // Scale down to ~0.05-0.10° per shot to allow multiple shots before overshooting
        float scaledRecoil = weapon.VerticalRecoil * 0.2f;

        // Resolve shot using vertical body-part hit resolution model
        var resolution = HitResolution.ResolveShot(
            distance: _shooter.DistanceToOpponent,
            targetBodyPart: targetBodyPart,
            operatorAccuracy: _shooter.Accuracy,
            weaponVerticalRecoil: scaledRecoil,
            currentRecoilY: _shooter.CurrentRecoilY * 0.2f,  // Also scale accumulated recoil
            recoilVariance: scaledRecoil * 0.1f, // 10% variance
            random: _random);

        long travelTime = CalculateTravelTimeMs();
        long impactTime = EventTimeMs + travelTime;
        
        if (resolution.HitLocation != BodyPart.Miss)
        {
            float damage = weapon.GetDamageAtDistance(_shooter.DistanceToOpponent, resolution.HitLocation);

            if (_eventQueue != null)
            {
                _eventQueue.Schedule(new DamageAppliedEvent(
                    impactTime,
                    _shooter,
                    _target,
                    damage,
                    resolution.HitLocation,
                    _eventQueue.GetNextSequenceNumber(),
                    weaponName));
            }
            else
            {
                _target.TakeDamage(damage, impactTime);
                Console.WriteLine($"[{impactTime}ms] {_shooter.Name}'s {weaponName} hit {_target.Name} for {damage:F1} damage ({resolution.HitLocation})");
            }
        }
        else
        {
            // Schedule miss event at impact time (when bullet passes target)
            if (_eventQueue != null)
            {
                _eventQueue.Schedule(new ShotMissedEvent(
                    impactTime,
                    _shooter,
                    _target,
                    _eventQueue.GetNextSequenceNumber(),
                    weaponName));
            }
            else
            {
                Console.WriteLine($"[{impactTime}ms] {_shooter.Name}'s {weaponName} missed {_target.Name}");
            }
        }

        // Apply recoil - recoil now accumulates (scaled to match angular band system)
        _shooter.CurrentRecoilY += scaledRecoil;
        _shooter.RecoilRecoveryStartMs = EventTimeMs + (long)weapon.RecoilRecoveryTimeMs;

        // No longer trigger reaction windows - rounds execute completely
        return false;
    }

    private long CalculateTravelTimeMs()
    {
        var weapon = _shooter.EquippedWeapon;
        if (weapon == null || weapon.BulletVelocityMetersPerSecond <= 0)
            return 0;

        double travelSeconds = _shooter.DistanceToOpponent / weapon.BulletVelocityMetersPerSecond;
        return (long)Math.Round(travelSeconds * 1000d, MidpointRounding.AwayFromZero);
    }
}

/// <summary>
/// Event fired when a hit's damage is applied to the target (after bullet travel time).
/// </summary>
public sealed class DamageAppliedEvent : ISimulationEvent
{
    public long EventTimeMs { get; }
    public Guid OperatorId { get; }
    public int SequenceNumber { get; }

    private readonly Operator _shooter;
    private readonly Operator _target;
    private readonly float _damage;
    private readonly Weapons.BodyPart _bodyPart;
    private readonly string _weaponName;

    public DamageAppliedEvent(long eventTimeMs, Operator shooter, Operator target, float damage, Weapons.BodyPart bodyPart, int sequenceNumber, string weaponName)
    {
        EventTimeMs = eventTimeMs;
        OperatorId = shooter.Id;
        SequenceNumber = sequenceNumber;
        _shooter = shooter;
        _target = target;
        _damage = damage;
        _bodyPart = bodyPart;
        _weaponName = weaponName;
    }

    public bool Execute()
    {
        _target.TakeDamage(_damage, EventTimeMs);
        Console.WriteLine($"[{EventTimeMs}ms] {_shooter.Name}'s {_weaponName} hit {_target.Name} for {_damage:F1} damage ({_bodyPart})");
        
        // Round end is detected by CombatSystemV2 via event type checking
        return false;
    }
}

/// <summary>
/// Event fired when a shot misses (after bullet travel time).
/// </summary>
public sealed class ShotMissedEvent : ISimulationEvent
{
    public long EventTimeMs { get; }
    public Guid OperatorId { get; }
    public int SequenceNumber { get; }

    private readonly Operator _shooter;
    private readonly Operator _target;
    private readonly string _weaponName;

    public ShotMissedEvent(long eventTimeMs, Operator shooter, Operator target, int sequenceNumber, string weaponName)
    {
        EventTimeMs = eventTimeMs;
        OperatorId = shooter.Id;
        SequenceNumber = sequenceNumber;
        _shooter = shooter;
        _target = target;
        _weaponName = weaponName;
    }

    public bool Execute()
    {
        Console.WriteLine($"[{EventTimeMs}ms] {_shooter.Name}'s {_weaponName} missed {_target.Name}");
        return false;
    }
}

/// <summary>
/// Event fired when reload completes.
/// </summary>
public class ReloadCompleteEvent : ISimulationEvent
{
    public long EventTimeMs { get; }
    public Guid OperatorId { get; }
    public int SequenceNumber { get; }
    
    private readonly Operator _operator;

    public ReloadCompleteEvent(long eventTimeMs, Operator op, int sequenceNumber)
    {
        EventTimeMs = eventTimeMs;
        OperatorId = op.Id;
        SequenceNumber = sequenceNumber;
        _operator = op;
    }

    public bool Execute()
    {
        if (_operator.EquippedWeapon != null)
        {
            _operator.CurrentAmmo = _operator.EquippedWeapon.MagazineSize;
            _operator.WeaponState = WeaponState.Ready;
            Console.WriteLine($"[{EventTimeMs}ms] {_operator.Name} finished reloading");
            return true; // Trigger reaction window
        }
        return false;
    }
}

/// <summary>
/// Event fired when ADS transition completes.
/// </summary>
public class ADSCompleteEvent : ISimulationEvent
{
    public long EventTimeMs { get; }
    public Guid OperatorId { get; }
    public int SequenceNumber { get; }
    
    private readonly Operator _operator;

    public ADSCompleteEvent(long eventTimeMs, Operator op, int sequenceNumber)
    {
        EventTimeMs = eventTimeMs;
        OperatorId = op.Id;
        SequenceNumber = sequenceNumber;
        _operator = op;
    }

    public bool Execute()
    {
        _operator.AimState = AimState.ADS;
        Console.WriteLine($"[{EventTimeMs}ms] {_operator.Name} entered ADS");
        return true; // Trigger reaction window
    }
}

/// <summary>
/// Event fired when movement interval completes.
/// </summary>
public class MovementIntervalEvent : ISimulationEvent
{
    public long EventTimeMs { get; }
    public Guid OperatorId { get; }
    public int SequenceNumber { get; }
    
    private readonly Operator _mover;
    private readonly float _distance;
    private readonly int _metersPerCommitmentUnit;

    public MovementIntervalEvent(long eventTimeMs, Operator mover, float distance, int sequenceNumber, int metersPerCommitmentUnit = 2)
    {
        EventTimeMs = eventTimeMs;
        OperatorId = mover.Id;
        SequenceNumber = sequenceNumber;
        _mover = mover;
        _distance = distance;
        _metersPerCommitmentUnit = metersPerCommitmentUnit;
    }

    public bool Execute()
    {
        _mover.DistanceToOpponent += _distance; // Positive = away, negative = toward
        _mover.MetersMovedSinceLastReaction += Math.Abs(_distance);
        
        Console.WriteLine($"[{EventTimeMs}ms] {_mover.Name} moved {_distance:F1}m (distance now: {_mover.DistanceToOpponent:F1}m)");

        // Check if this triggers a reaction window
        if (_mover.MetersMovedSinceLastReaction >= _metersPerCommitmentUnit)
        {
            _mover.MetersMovedSinceLastReaction = 0;
            return true; // Trigger reaction window
        }

        return false;
    }
}

/// <summary>
/// Event fired when slide completes.
/// </summary>
public class SlideCompleteEvent : ISimulationEvent
{
    public long EventTimeMs { get; }
    public Guid OperatorId { get; }
    public int SequenceNumber { get; }
    
    private readonly Operator _operator;

    public SlideCompleteEvent(long eventTimeMs, Operator op, int sequenceNumber)
    {
        EventTimeMs = eventTimeMs;
        OperatorId = op.Id;
        SequenceNumber = sequenceNumber;
        _operator = op;
    }

    public bool Execute()
    {
        _operator.MovementState = MovementState.Idle;
        Console.WriteLine($"[{EventTimeMs}ms] {_operator.Name} finished sliding");
        return true; // Trigger reaction window
    }
}

/// <summary>
/// Micro-reaction event that triggers frequent reaction windows.
/// Allows for quick responses and tactical adjustments.
/// </summary>
public class MicroReactionEvent : ISimulationEvent
{
    public long EventTimeMs { get; }
    public Guid OperatorId { get; }
    public int SequenceNumber { get; }

    public MicroReactionEvent(long eventTimeMs, Guid operatorId, int sequenceNumber)
    {
        EventTimeMs = eventTimeMs;
        OperatorId = operatorId;
        SequenceNumber = sequenceNumber;
    }

    public bool Execute()
    {
        // This event simply triggers a reaction window
        // No state changes
        return true; // Always trigger reaction window
    }
}

/// <summary>
/// Event fired when ADS transition updates.
/// Tracks continuous ADS progress.
/// </summary>
public class ADSTransitionUpdateEvent : ISimulationEvent
{
    public long EventTimeMs { get; }
    public Guid OperatorId { get; }
    public int SequenceNumber { get; }
    
    private readonly Operator _operator;

    public ADSTransitionUpdateEvent(long eventTimeMs, Operator op, int sequenceNumber)
    {
        EventTimeMs = eventTimeMs;
        OperatorId = op.Id;
        SequenceNumber = sequenceNumber;
        _operator = op;
    }

    public bool Execute()
    {
        // Update ADS state based on progress
        if (_operator.AimState == AimState.TransitioningToADS && _operator.ADSTransitionStartMs.HasValue)
        {
            float elapsed = EventTimeMs - _operator.ADSTransitionStartMs.Value;
            if (elapsed >= _operator.ADSTransitionDurationMs)
            {
                _operator.AimState = AimState.ADS;
                Console.WriteLine($"[{EventTimeMs}ms] {_operator.Name} completed ADS transition");
            }
        }
        else if (_operator.AimState == AimState.TransitioningToHip && _operator.ADSTransitionStartMs.HasValue)
        {
            float elapsed = EventTimeMs - _operator.ADSTransitionStartMs.Value;
            if (elapsed >= _operator.ADSTransitionDurationMs)
            {
                _operator.AimState = AimState.Hip;
                _operator.ADSTransitionStartMs = null;
                Console.WriteLine($"[{EventTimeMs}ms] {_operator.Name} exited ADS");
            }
        }

        return false; // Don't trigger reaction window for ADS updates
    }
}
