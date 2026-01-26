using GUNRPG.Core.Operators;

namespace GUNRPG.Core.VirtualPet;

/// <summary>
/// Manages the virtual pet loop - operator rest, recovery, and readiness.
/// </summary>
public class RestSystem
{
    /// <summary>
    /// Amount of fatigue gained per combat encounter.
    /// </summary>
    public float FatiguePerCombat { get; set; } = 20f;

    /// <summary>
    /// Fatigue recovery rate per hour of rest.
    /// </summary>
    public float FatigueRecoveryPerHour { get; set; } = 30f;

    /// <summary>
    /// Maximum fatigue before operator cannot deploy.
    /// </summary>
    public float MaxDeployableFatigue { get; set; } = 80f;

    /// <summary>
    /// Checks if an operator is ready for combat.
    /// </summary>
    public bool IsReadyForCombat(Operator op)
    {
        return op.Fatigue < MaxDeployableFatigue && op.IsAlive;
    }

    /// <summary>
    /// Applies fatigue after a combat encounter.
    /// </summary>
    public void ApplyPostCombatFatigue(Operator op)
    {
        op.Fatigue = Math.Min(op.MaxFatigue, op.Fatigue + FatiguePerCombat);
    }

    /// <summary>
    /// Recovers fatigue during rest.
    /// </summary>
    /// <param name="op">Operator resting</param>
    /// <param name="hoursRested">Duration of rest in hours</param>
    public void Rest(Operator op, float hoursRested)
    {
        float fatigueRecovered = FatigueRecoveryPerHour * hoursRested;
        op.Fatigue = Math.Max(0, op.Fatigue - fatigueRecovered);
        
        // Fully restore health during rest
        op.Health = op.MaxHealth;
        op.Stamina = op.MaxStamina;
        op.LastDamageTimeMs = null;
    }

    /// <summary>
    /// Gets the minimum rest time required for the operator to be combat-ready.
    /// </summary>
    public float GetMinimumRestHours(Operator op)
    {
        if (IsReadyForCombat(op))
            return 0f;

        float excessFatigue = op.Fatigue - MaxDeployableFatigue;
        return excessFatigue / FatigueRecoveryPerHour;
    }

    /// <summary>
    /// Gets a status summary for the operator.
    /// </summary>
    public string GetOperatorStatus(Operator op)
    {
        if (!op.IsAlive)
            return "❌ Deceased";

        if (IsReadyForCombat(op))
            return "✓ Ready for combat";

        float minRestHours = GetMinimumRestHours(op);
        return $"⚠ Too fatigued (needs {minRestHours:F1}h rest)";
    }
}

/// <summary>
/// Manages operator schedule and availability.
/// </summary>
public class OperatorManager
{
    private readonly RestSystem _restSystem;
    private readonly Dictionary<Guid, DateTime> _restStartTimes;

    public OperatorManager()
    {
        _restSystem = new RestSystem();
        _restStartTimes = new Dictionary<Guid, DateTime>();
    }

    /// <summary>
    /// Sends an operator to rest.
    /// </summary>
    public void SendToRest(Operator op)
    {
        _restStartTimes[op.Id] = DateTime.UtcNow;
    }

    /// <summary>
    /// Checks if an operator is currently resting.
    /// </summary>
    public bool IsResting(Operator op)
    {
        return _restStartTimes.ContainsKey(op.Id);
    }

    /// <summary>
    /// Gets the duration an operator has been resting.
    /// </summary>
    public TimeSpan GetRestDuration(Operator op)
    {
        if (!_restStartTimes.TryGetValue(op.Id, out var startTime))
            return TimeSpan.Zero;

        return DateTime.UtcNow - startTime;
    }

    /// <summary>
    /// Wakes an operator from rest, applying fatigue recovery.
    /// </summary>
    public void WakeFromRest(Operator op)
    {
        if (!_restStartTimes.TryGetValue(op.Id, out var startTime))
            return;

        TimeSpan restDuration = DateTime.UtcNow - startTime;
        float hoursRested = (float)restDuration.TotalHours;
        
        _restSystem.Rest(op, hoursRested);
        _restStartTimes.Remove(op.Id);
    }

    /// <summary>
    /// Prepares an operator for combat deployment.
    /// Fully restores health and stamina.
    /// </summary>
    public bool PrepareForCombat(Operator op)
    {
        if (!_restSystem.IsReadyForCombat(op))
            return false;

        // Reset combat-scoped state
        op.Health = op.MaxHealth;
        op.Stamina = op.MaxStamina;
        op.LastDamageTimeMs = null;
        op.MovementState = MovementState.Idle;
        op.AimState = AimState.Hip;
        op.WeaponState = WeaponState.Ready;
        op.CurrentRecoilX = 0;
        op.CurrentRecoilY = 0;
        op.RecoilRecoveryStartMs = null;
        op.BulletsFiredSinceLastReaction = 0;
        op.MetersMovedSinceLastReaction = 0;
        
        if (op.EquippedWeapon != null)
        {
            op.CurrentAmmo = op.EquippedWeapon.MagazineSize;
        }

        return true;
    }

    /// <summary>
    /// Handles post-combat cleanup.
    /// </summary>
    public void CompleteCombat(Operator op, bool survived)
    {
        if (survived)
        {
            _restSystem.ApplyPostCombatFatigue(op);
        }
    }

    /// <summary>
    /// Gets operator readiness status.
    /// </summary>
    public string GetStatus(Operator op)
    {
        if (IsResting(op))
        {
            var duration = GetRestDuration(op);
            return $"💤 Resting ({duration.Hours}h {duration.Minutes}m)";
        }

        return _restSystem.GetOperatorStatus(op);
    }

    /// <summary>
    /// Gets the rest system for configuration.
    /// </summary>
    public RestSystem RestSystem => _restSystem;
}
