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
    /// <summary>
    /// Minimum valid angle for a body hit (degrees). Shots below this are undershoots.
    /// </summary>
    private const float MinValidHitAngle = 0.0f;

    /// <summary>
    /// Maximum valid angle for a body hit (degrees). Shots above this are overshoots.
    /// </summary>
    private const float MaxValidHitAngle = 1.0f;

    public long EventTimeMs { get; }
    public Guid OperatorId { get; }
    public int SequenceNumber { get; }
    
    private readonly Operator _shooter;
    private readonly Operator _target;
    private readonly Random _random;
    private readonly EventQueue? _eventQueue;
    private readonly CombatDebugOptions? _debugOptions;
    private long? _resolvedTravelTimeMs;
    private sealed class ShotTelemetry
    {
        public int ShotNumber { get; set; }
        public string Stance { get; set; } = string.Empty;
        public BodyPart IntendedBodyPart { get; set; }
        public float DistanceMeters { get; set; }
        public float BaseAimAngle { get; set; }
        public float AimError { get; set; }
        public float RecoilAdded { get; set; }
        public float RecoilRecovered { get; set; }
        public float FinalAimAngle { get; set; }
        public float BaseAccuracyProficiency { get; set; }
        public float FlinchSeverity { get; set; }
        public float EffectiveAccuracyProficiency { get; set; }
        public BodyPart ResolvedBodyPart { get; set; }
        public float Damage { get; set; }
        public bool WasHit { get; set; }

        public string ToLogString()
        {
            string[] lines =
            {
                $"── Shot {ShotNumber} ─────────────────────────────",
                $"Intent: Fire ({Stance})",
                $"Target Band: {IntendedBodyPart}",
                $"Distance: {DistanceMeters:F2}m",
                string.Empty,
                "Aim:",
                $"  Base Aim Angle:   {BaseAimAngle:F2}°",
                $"  Aim Error:       {AimError:+0.00;-0.00}°",
                $"  Recoil Added:    {RecoilAdded:+0.00;-0.00}°",
                $"  Recoil Recovered: {RecoilRecovered:+0.00;-0.00}°",
                $"  Final Aim Angle: {FinalAimAngle:F2}°",
                string.Empty,
                "Accuracy:",
                $"  Base Proficiency:      {BaseAccuracyProficiency:0.00}",
                $"  Flinch Severity:       {FlinchSeverity:0.00}",
                $"  Effective Proficiency: {EffectiveAccuracyProficiency:0.00}",
                string.Empty,
                "Resolution:",
                $"  Hit Band: {ResolvedBodyPart}",
                $"  Damage: {Damage:F1}",
                $"  Target Will Flinch: {(WasHit ? "Yes" : "No")}",
                string.Empty,
                "───────────────────────────────────────────────"
            };

            return string.Join(Environment.NewLine, lines);
        }
    }


    public ShotFiredEvent(
        long eventTimeMs,
        Operator shooter,
        Operator target,
        int sequenceNumber,
        Random random,
        EventQueue? eventQueue = null,
        CombatDebugOptions? debugOptions = null)
    {
        EventTimeMs = eventTimeMs;
        OperatorId = shooter.Id;
        SequenceNumber = sequenceNumber;
        _shooter = shooter;
        _target = target;
        _random = random;
        _eventQueue = eventQueue;
        _debugOptions = debugOptions;
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
        // In ADS, aim for head/neck; in hipfire, aim for center mass
        BodyPart targetBodyPart = _shooter.AimState == AimState.ADS 
            ? BodyPart.Head 
            : BodyPart.UpperTorso;

        // Resolve shot using vertical body-part hit resolution model with proficiency effects
        // AccuracyProficiency is applied AFTER weapon recoil is calculated:
        // - Reduces effective vertical recoil (recoil counteraction)
        // - Tightens aim error distribution (aim stability)
        float baseProficiency = _shooter.AccuracyProficiency;
        float flinchSeverity = _shooter.FlinchSeverity;
        float effectiveProficiency = AccuracyModel.CalculateEffectiveAccuracyProficiency(
            baseProficiency,
            flinchSeverity);
        var details = new ShotResolutionDetails();
        var resolution = HitResolution.ResolveShotWithProficiency(
            targetBodyPart: targetBodyPart,
            operatorAccuracy: _shooter.Accuracy,
            accuracyProficiency: effectiveProficiency,
            weaponVerticalRecoil: weapon.VerticalRecoil,
            currentRecoilY: _shooter.CurrentRecoilY,
            recoilVariance: weapon.VerticalRecoil * 0.1f, // 10% variance
            random: _random,
            details: details,
            movementState: _shooter.CurrentMovement);

        long travelTime = CalculateTravelTimeMs();
        _resolvedTravelTimeMs = travelTime;
        long impactTime = EventTimeMs + travelTime;
        
        bool isHit = resolution.HitLocation != BodyPart.Miss;
        float damage = isHit ? weapon.GetDamageAtDistance(_shooter.DistanceToOpponent, resolution.HitLocation) : 0f;

        if (resolution.HitLocation != BodyPart.Miss)
        {
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
                var targetWeapon = _target.EquippedWeapon;
                float flinchResistance = targetWeapon?.FlinchResistance ?? AccuracyModel.MinFlinchResistance;
                float targetFlinchSeverity = AccuracyModel.CalculateFlinchSeverity(damage, flinchResistance);
                _target.ApplyFlinch(targetFlinchSeverity);
                Console.WriteLine($"[{impactTime}ms] {_shooter.Name}'s {weaponName} hit {_target.Name} for {damage:F1} damage ({resolution.HitLocation})");
            }
        }
        else
        {
            // Calculate angular deviation from target body band for suppression calculation
            // Deviation is how far the shot was from the valid hit zone (0-1 degrees)
            float angularDeviation = CalculateAngularDeviation(resolution.FinalAngleDegrees);

            // Schedule miss event at impact time (when bullet passes target)
            if (_eventQueue != null)
            {
                _eventQueue.Schedule(new ShotMissedEvent(
                    impactTime,
                    _shooter,
                    _target,
                    _eventQueue.GetNextSequenceNumber(),
                    weaponName,
                    angularDeviation,
                    _eventQueue));
            }
            else
            {
                Console.WriteLine($"[{impactTime}ms] {_shooter.Name}'s {weaponName} missed {_target.Name}");
                
                // Apply suppression directly when no event queue
                if (SuppressionModel.ShouldApplySuppression(angularDeviation))
                {
                    float suppressionSeverity = SuppressionModel.CalculateSuppressionSeverity(
                        weapon.SuppressionFactor,
                        weapon.RoundsPerMinute,
                        _shooter.DistanceToOpponent,
                        angularDeviation,
                        _target.CurrentMovement,
                        _target.CurrentPosture);
                    _target.ApplySuppression(suppressionSeverity, impactTime);
                }
            }
        }

        // Apply recoil - recoil now accumulates
        _shooter.CurrentRecoilY += weapon.VerticalRecoil;
        _shooter.RecoilRecoveryStartMs = EventTimeMs + (long)weapon.RecoilRecoveryTimeMs;

        // Apply immediate partial recoil recovery based on proficiency
        // This ensures recoil recovery happens at least once per shot, even if round ends early
        // The recovery amount is based on a fixed time quantum (100ms worth of recovery)
        const float immediateRecoveryTimeMs = 100f;
        float immediateRecoverySeconds = immediateRecoveryTimeMs / 1000f;
        float recoveryMultiplier = AccuracyModel.CalculateRecoveryRateMultiplier(baseProficiency);
        float immediateRecovery = _shooter.RecoilRecoveryRate * immediateRecoverySeconds * recoveryMultiplier;
        float recoilBeforeRecovery = _shooter.CurrentRecoilY;
        
        // Apply immediate recovery to vertical axis only (no horizontal recoil is implemented)
        _shooter.CurrentRecoilY = Math.Max(0, _shooter.CurrentRecoilY - immediateRecovery);
        float recoilRecovered = Math.Max(0f, recoilBeforeRecovery - _shooter.CurrentRecoilY);

        int shotNumber = _shooter.IncrementShotsFired();
        if (_debugOptions?.VerboseShotLogs == true)
        {
            string stance = _shooter.AimState == AimState.ADS ? "ADS" : "Hip";

            var telemetry = new ShotTelemetry
            {
                ShotNumber = shotNumber,
                Stance = stance,
                IntendedBodyPart = targetBodyPart,
                DistanceMeters = _shooter.DistanceToOpponent,
                BaseAimAngle = details.BaseAimAngle,
                AimError = details.TotalAimDeviation, // Include both aim error and sway
                RecoilAdded = details.RecoilAdded,
                RecoilRecovered = recoilRecovered,
                FinalAimAngle = details.FinalAimAngle,
                BaseAccuracyProficiency = baseProficiency,
                FlinchSeverity = flinchSeverity,
                EffectiveAccuracyProficiency = effectiveProficiency,
                ResolvedBodyPart = resolution.HitLocation,
                Damage = damage,
                WasHit = isHit
            };

            Console.WriteLine(telemetry.ToLogString());
        }

        _shooter.ConsumeFlinchShot();

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

    /// <summary>
    /// Calculates how far the shot deviated from the valid hit zone.
    /// Used for suppression calculation - closer misses are more suppressive.
    /// </summary>
    private static float CalculateAngularDeviation(float finalAngleDegrees)
    {
        if (finalAngleDegrees < MinValidHitAngle)
        {
            // Undershoot - deviation is distance below min
            return MinValidHitAngle - finalAngleDegrees;
        }
        else if (finalAngleDegrees > MaxValidHitAngle)
        {
            // Overshoot - deviation is distance above max
            return finalAngleDegrees - MaxValidHitAngle;
        }


        // Shot was within the valid hit range [MinValidHitAngle, MaxValidHitAngle].
        // When this method is used from miss-handling logic, reaching this branch
        // indicates a potential upstream logic error (a hit treated as a miss).
#if DEBUG
        System.Diagnostics.Debug.Fail(
            $"CalculateAngularDeviation was called with an in-range angle ({finalAngleDegrees}°). " +
            "This suggests a hit was passed to miss-handling logic.");
#endif
        return 0f;
    }

    public long TravelTimeMs => _resolvedTravelTimeMs ?? CalculateTravelTimeMs();
}

/// <summary>
/// Event fired when a hit's damage is applied to the target (after bullet travel time).
/// </summary>
public sealed class DamageAppliedEvent : ISimulationEvent
{
    public const int DefaultDurationMs = 2;
    public long EventTimeMs { get; }
    public Guid OperatorId { get; }
    public Guid TargetId { get; }
    public string TargetName { get; }
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
        TargetId = target.Id;
        TargetName = target.Name;
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
        var targetWeapon = _target.EquippedWeapon;
        float flinchResistance = targetWeapon?.FlinchResistance ?? AccuracyModel.MinFlinchResistance;
        float flinchSeverity = AccuracyModel.CalculateFlinchSeverity(_damage, flinchResistance);
        _target.ApplyFlinch(flinchSeverity);
        Console.WriteLine($"[{EventTimeMs}ms] {_shooter.Name}'s {_weaponName} hit {_target.Name} for {_damage:F1} damage ({_bodyPart})");
        
        // Round end is detected by CombatSystemV2 via event type checking
        return false;
    }

    public int ActionDurationMs => DefaultDurationMs;
}

/// <summary>
/// Event fired when a shot misses (after bullet travel time).
/// Applies suppression to the target based on how close the shot came.
/// </summary>
public sealed class ShotMissedEvent : ISimulationEvent
{
    public const int DefaultDurationMs = 2;
    public long EventTimeMs { get; }
    public Guid OperatorId { get; }
    public int SequenceNumber { get; }

    private readonly Operator _shooter;
    private readonly Operator _target;
    private readonly string _weaponName;
    private readonly float _angularDeviation;
    private readonly EventQueue? _eventQueue;

    /// <summary>
    /// Angular deviation from target in degrees. Used to calculate suppression severity.
    /// </summary>
    public float AngularDeviation => _angularDeviation;

    public ShotMissedEvent(
        long eventTimeMs,
        Operator shooter,
        Operator target,
        int sequenceNumber,
        string weaponName,
        float angularDeviation = 0f,
        EventQueue? eventQueue = null)
    {
        EventTimeMs = eventTimeMs;
        OperatorId = shooter.Id;
        SequenceNumber = sequenceNumber;
        _shooter = shooter;
        _target = target;
        _weaponName = weaponName;
        _angularDeviation = angularDeviation;
        _eventQueue = eventQueue;
    }

    public bool Execute()
    {
        Console.WriteLine($"[{EventTimeMs}ms] {_shooter.Name}'s {_weaponName} missed {_target.Name}");

        // Apply suppression if the shot was close enough to be threatening
        var weapon = _shooter.EquippedWeapon;
        if (weapon != null && SuppressionModel.ShouldApplySuppression(_angularDeviation))
        {
            float suppressionSeverity = SuppressionModel.CalculateSuppressionSeverity(
                weapon.SuppressionFactor,
                weapon.RoundsPerMinute,
                _shooter.DistanceToOpponent,
                _angularDeviation,
                _target.CurrentMovement,
                _target.CurrentPosture);

            if (suppressionSeverity > 0f)
            {
                float previousLevel = _target.SuppressionLevel;
                bool becameSuppressed = _target.ApplySuppression(suppressionSeverity, EventTimeMs);

                // Emit appropriate suppression event
                if (_eventQueue != null)
                {
                    if (becameSuppressed)
                    {
                        _eventQueue.Schedule(new SuppressionStartedEvent(
                            EventTimeMs,
                            _target,
                            _shooter,
                            _target.SuppressionLevel,
                            _eventQueue.GetNextSequenceNumber(),
                            _weaponName));
                    }
                    else if (previousLevel > 0f)
                    {
                        _eventQueue.Schedule(new SuppressionUpdatedEvent(
                            EventTimeMs,
                            _target,
                            _shooter,
                            previousLevel,
                            _target.SuppressionLevel,
                            _eventQueue.GetNextSequenceNumber(),
                            _weaponName));
                    }
                }
            }
        }

        return false;
    }

    public int ActionDurationMs => DefaultDurationMs;
}

/// <summary>
/// Event fired when reload completes.
/// </summary>
public class ReloadCompleteEvent : ISimulationEvent
{
    public const int DefaultDurationMs = 2;
    public long EventTimeMs { get; }
    public Guid OperatorId { get; }
    public int SequenceNumber { get; }
    
    private readonly Operator _operator;
    private readonly int _actionDurationMs;

    public ReloadCompleteEvent(long eventTimeMs, Operator op, int sequenceNumber, int actionDurationMs = DefaultDurationMs)
    {
        EventTimeMs = eventTimeMs;
        OperatorId = op.Id;
        SequenceNumber = sequenceNumber;
        _operator = op;
        _actionDurationMs = actionDurationMs;
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

    public int ActionDurationMs => _actionDurationMs;
}

/// <summary>
/// Event fired when ADS transition completes.
/// </summary>
public class ADSCompleteEvent : ISimulationEvent
{
    public const int DefaultDurationMs = 2;
    public long EventTimeMs { get; }
    public Guid OperatorId { get; }
    public int SequenceNumber { get; }
    
    private readonly Operator _operator;
    private readonly int _actionDurationMs;

    public ADSCompleteEvent(long eventTimeMs, Operator op, int sequenceNumber, int actionDurationMs = DefaultDurationMs)
    {
        EventTimeMs = eventTimeMs;
        OperatorId = op.Id;
        SequenceNumber = sequenceNumber;
        _operator = op;
        _actionDurationMs = actionDurationMs;
    }

    public bool Execute()
    {
        _operator.AimState = AimState.ADS;
        Console.WriteLine($"[{EventTimeMs}ms] {_operator.Name} entered ADS");
        return true; // Trigger reaction window
    }

    public int ActionDurationMs => _actionDurationMs;
}

/// <summary>
/// Event fired when movement interval completes.
/// </summary>
public class MovementIntervalEvent : ISimulationEvent
{
    public const int DefaultIntervalMs = 100;
    public long EventTimeMs { get; }
    public Guid OperatorId { get; }
    public int SequenceNumber { get; }
    
    private readonly Operator _mover;
    private readonly float _distance;
    private readonly int _metersPerCommitmentUnit;
    private readonly int _intervalDurationMs;

    public MovementIntervalEvent(long eventTimeMs, Operator mover, float distance, int sequenceNumber, int intervalDurationMs = DefaultIntervalMs, int metersPerCommitmentUnit = 2)
    {
        EventTimeMs = eventTimeMs;
        OperatorId = mover.Id;
        SequenceNumber = sequenceNumber;
        _mover = mover;
        _distance = distance;
        _intervalDurationMs = intervalDurationMs;
        _metersPerCommitmentUnit = metersPerCommitmentUnit;
    }

    public bool Execute()
    {
        // Movement no longer changes distance - it only affects combat modifiers
        // Distance is now static unless explicitly changed by player actions
        _mover.MetersMovedSinceLastReaction += Math.Abs(_distance);
        
        // Tactical commitment affects reaction windows, not spatial position
        Console.WriteLine($"[{EventTimeMs}ms] {_mover.Name} committed {_distance:F1}m of tactical movement (position unchanged: {_mover.DistanceToOpponent:F1}m)");

        // Check if this triggers a reaction window
        if (_mover.MetersMovedSinceLastReaction >= _metersPerCommitmentUnit)
        {
            _mover.MetersMovedSinceLastReaction = 0;
            return true; // Trigger reaction window
        }

        return false;
    }

    public int IntervalDurationMs => _intervalDurationMs;
}

/// <summary>
/// Event fired when slide completes.
/// </summary>
public class SlideCompleteEvent : ISimulationEvent
{
    public const int DefaultDurationMs = 2;
    public long EventTimeMs { get; }
    public Guid OperatorId { get; }
    public int SequenceNumber { get; }
    
    private readonly Operator _operator;
    private readonly int _actionDurationMs;

    public SlideCompleteEvent(long eventTimeMs, Operator op, int sequenceNumber, int actionDurationMs = DefaultDurationMs)
    {
        EventTimeMs = eventTimeMs;
        OperatorId = op.Id;
        SequenceNumber = sequenceNumber;
        _operator = op;
        _actionDurationMs = actionDurationMs;
    }

    public bool Execute()
    {
        _operator.MovementState = MovementState.Idle;
        Console.WriteLine($"[{EventTimeMs}ms] {_operator.Name} finished sliding");
        return true; // Trigger reaction window
    }

    public int ActionDurationMs => _actionDurationMs;
}

/// <summary>
/// Micro-reaction event that triggers frequent reaction windows.
/// Allows for quick responses and tactical adjustments.
/// </summary>
public class MicroReactionEvent : ISimulationEvent
{
    public const int DefaultDurationMs = 2;
    public long EventTimeMs { get; }
    public Guid OperatorId { get; }
    public int SequenceNumber { get; }
    private readonly int _actionDurationMs;

    public MicroReactionEvent(long eventTimeMs, Guid operatorId, int sequenceNumber, int actionDurationMs = DefaultDurationMs)
    {
        EventTimeMs = eventTimeMs;
        OperatorId = operatorId;
        SequenceNumber = sequenceNumber;
        _actionDurationMs = actionDurationMs;
    }

    public bool Execute()
    {
        // This event simply triggers a reaction window
        // No state changes
        return true; // Always trigger reaction window
    }

    public int ActionDurationMs => _actionDurationMs;
}

/// <summary>
/// Event fired when ADS transition updates.
/// Tracks continuous ADS progress.
/// </summary>
public class ADSTransitionUpdateEvent : ISimulationEvent
{
    public const int DefaultDurationMs = 2;
    public long EventTimeMs { get; }
    public Guid OperatorId { get; }
    public int SequenceNumber { get; }
    
    private readonly Operator _operator;
    private readonly int _actionDurationMs;

    public ADSTransitionUpdateEvent(long eventTimeMs, Operator op, int sequenceNumber, int actionDurationMs = DefaultDurationMs)
    {
        EventTimeMs = eventTimeMs;
        OperatorId = op.Id;
        SequenceNumber = sequenceNumber;
        _operator = op;
        _actionDurationMs = actionDurationMs;
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

    public int ActionDurationMs => _actionDurationMs;
}
