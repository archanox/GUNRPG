using GUNRPG.Core.Operators;

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

    public ShotFiredEvent(long eventTimeMs, Operator shooter, Operator target, int sequenceNumber, Random random)
    {
        EventTimeMs = eventTimeMs;
        OperatorId = shooter.Id;
        SequenceNumber = sequenceNumber;
        _shooter = shooter;
        _target = target;
        _random = random;
    }

    public bool Execute()
    {
        // Validate can still fire
        if (_shooter.CurrentAmmo <= 0 || _shooter.WeaponState != WeaponState.Ready)
            return false;

        // Consume ammo
        _shooter.CurrentAmmo--;

        // Calculate hit
        bool isHit = CalculateHit();
        
        if (isHit)
        {
            bool isHeadshot = CalculateHeadshot();
            float damage = _shooter.EquippedWeapon!.GetDamageAtDistance(_shooter.DistanceToOpponent, isHeadshot);
            _target.TakeDamage(damage, EventTimeMs);
            
            Console.WriteLine($"[{EventTimeMs}ms] {_shooter.Name} hit {_target.Name} for {damage:F1} damage{(isHeadshot ? " (HEADSHOT)" : "")}");
        }
        else
        {
            Console.WriteLine($"[{EventTimeMs}ms] {_shooter.Name} missed {_target.Name}");
        }

        // Apply recoil
        if (_shooter.EquippedWeapon != null)
        {
            _shooter.CurrentRecoilX += _shooter.EquippedWeapon.HorizontalRecoil;
            _shooter.CurrentRecoilY += _shooter.EquippedWeapon.VerticalRecoil;
            _shooter.RecoilRecoveryStartMs = EventTimeMs + (long)_shooter.EquippedWeapon.RecoilRecoveryTimeMs;
        }

        // Track commitment units
        _shooter.BulletsFiredSinceLastReaction++;

        // Check if this triggers a reaction window
        if (_shooter.EquippedWeapon != null && 
            _shooter.BulletsFiredSinceLastReaction >= _shooter.EquippedWeapon.BulletsPerCommitmentUnit)
        {
            _shooter.BulletsFiredSinceLastReaction = 0;
            return true; // Trigger reaction window
        }

        return false;
    }

    private bool CalculateHit()
    {
        var weapon = _shooter.EquippedWeapon;
        if (weapon == null)
            return false;

        // Get current spread based on ADS progress (interpolated)
        float spreadDegrees = _shooter.GetCurrentSpread(EventTimeMs);

        // Add recoil to spread
        float totalSpread = spreadDegrees + Math.Abs(_shooter.CurrentRecoilX) + Math.Abs(_shooter.CurrentRecoilY);

        // Simple hit calculation: lower spread = higher hit chance
        // At point-blank with perfect accuracy, ~90% hit rate
        // Hit chance decreases with distance and spread
        float baseHitChance = 0.9f;
        float spreadPenalty = totalSpread * 0.05f; // Each degree reduces hit chance
        float distancePenalty = _shooter.DistanceToOpponent * 0.01f; // Each meter reduces hit chance
        
        float hitChance = Math.Max(0.1f, baseHitChance - spreadPenalty - distancePenalty);
        
        return _random.NextDouble() < hitChance;
    }

    private bool CalculateHeadshot()
    {
        // 10% headshot chance on hit (simplified)
        return _random.NextDouble() < 0.1;
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
