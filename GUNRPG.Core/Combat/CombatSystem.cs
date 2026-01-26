using GUNRPG.Core.Events;
using GUNRPG.Core.Intents;
using GUNRPG.Core.Operators;
using GUNRPG.Core.Time;

namespace GUNRPG.Core.Combat;

/// <summary>
/// Combat phase state.
/// </summary>
public enum CombatPhase
{
    Planning,   // Time paused, accepting intents
    Executing,  // Time running, events executing
    Ended       // Combat complete
}

/// <summary>
/// Main combat system orchestrator.
/// Manages event queue, time, and operator state during combat.
/// </summary>
public class CombatSystem
{
    private readonly SimulationTime _time;
    private readonly EventQueue _eventQueue;
    private readonly Random _random;

    public Operator Player { get; }
    public Operator Enemy { get; }
    public CombatPhase Phase { get; private set; }

    private Intent? _playerIntent;
    private Intent? _enemyIntent;

    // Movement interval tracking
    private const int MOVEMENT_INTERVAL_MS = 500; // Process movement every 500ms
    private const int METERS_PER_COMMITMENT_UNIT = 2; // Reaction every 2 meters

    public CombatSystem(Operator player, Operator enemy, int? seed = null)
    {
        _time = new SimulationTime();
        _eventQueue = new EventQueue();
        _random = seed.HasValue ? new Random(seed.Value) : new Random();
        
        Player = player;
        Enemy = enemy;
        Phase = CombatPhase.Planning;
    }

    /// <summary>
    /// Submits an intent for an operator.
    /// Can only be called during Planning phase.
    /// </summary>
    public (bool success, string? errorMessage) SubmitIntent(Operator op, Intent intent)
    {
        if (Phase != CombatPhase.Planning)
            return (false, "Can only submit intents during planning phase");

        var validation = intent.Validate(op);
        if (!validation.isValid)
            return (false, validation.errorMessage);

        intent.SubmittedAtMs = _time.CurrentTimeMs;

        if (op == Player)
            _playerIntent = intent;
        else if (op == Enemy)
            _enemyIntent = intent;

        return (true, null);
    }

    /// <summary>
    /// Begins execution phase, processing intents and running events.
    /// </summary>
    public void BeginExecution()
    {
        if (Phase != CombatPhase.Planning)
            return;

        Phase = CombatPhase.Executing;

        // Process both intents to schedule initial events
        if (_playerIntent != null)
            ProcessIntent(Player, _playerIntent);
        
        if (_enemyIntent != null)
            ProcessIntent(Enemy, _enemyIntent);

        Console.WriteLine($"\n=== EXECUTION PHASE STARTED at {_time.CurrentTimeMs}ms ===\n");
    }

    /// <summary>
    /// Executes events until a reaction window is triggered or no events remain.
    /// </summary>
    public bool ExecuteUntilReactionWindow()
    {
        while (_eventQueue.Count > 0)
        {
            var evt = _eventQueue.DequeueNext();
            if (evt == null)
                break;

            // Advance time to event
            long deltaMs = evt.EventTimeMs - _time.CurrentTimeMs;
            if (deltaMs > 0)
            {
                // Update regeneration for both operators
                Player.UpdateRegeneration(deltaMs, evt.EventTimeMs);
                Enemy.UpdateRegeneration(deltaMs, evt.EventTimeMs);
                
                _time.Advance(deltaMs);
            }

            // Execute event
            bool triggersReactionWindow = evt.Execute();

            // Check for death
            if (!Player.IsAlive || !Enemy.IsAlive)
            {
                Phase = CombatPhase.Ended;
                Console.WriteLine($"\n=== COMBAT ENDED at {_time.CurrentTimeMs}ms ===");
                
                if (!Player.IsAlive)
                    Console.WriteLine($"{Player.Name} was defeated!");
                if (!Enemy.IsAlive)
                    Console.WriteLine($"{Enemy.Name} was defeated!");
                    
                return false;
            }

            // Check if reaction window triggered
            if (triggersReactionWindow)
            {
                Phase = CombatPhase.Planning;
                Console.WriteLine($"\n=== REACTION WINDOW at {_time.CurrentTimeMs}ms ===");
                Console.WriteLine($"Player: HP {Player.Health:F0}/{Player.MaxHealth:F0}, Ammo {Player.CurrentAmmo}, Distance {Player.DistanceToOpponent:F1}m");
                Console.WriteLine($"Enemy:  HP {Enemy.Health:F0}/{Enemy.MaxHealth:F0}, Ammo {Enemy.CurrentAmmo}, Distance {Enemy.DistanceToOpponent:F1}m");
                Console.WriteLine();
                return true;
            }

            // Continue existing continuous intents
            if (_playerIntent != null && ShouldContinueIntent(Player, _playerIntent))
                ScheduleNextEventForIntent(Player, _playerIntent);
            
            if (_enemyIntent != null && ShouldContinueIntent(Enemy, _enemyIntent))
                ScheduleNextEventForIntent(Enemy, _enemyIntent);
        }

        // No more events and no reaction window
        Phase = CombatPhase.Planning;
        return true;
    }

    /// <summary>
    /// Cancels the current intent for an operator.
    /// </summary>
    public void CancelIntent(Operator op)
    {
        _eventQueue.RemoveEventsForOperator(op.Id);
        
        if (op == Player)
            _playerIntent = null;
        else if (op == Enemy)
            _enemyIntent = null;
    }

    private void ProcessIntent(Operator op, Intent intent)
    {
        switch (intent.Type)
        {
            case IntentType.FireWeapon:
                ProcessFireIntent(op, (FireWeaponIntent)intent);
                break;
            
            case IntentType.Reload:
                ProcessReloadIntent(op);
                break;
            
            case IntentType.EnterADS:
                ProcessEnterADSIntent(op);
                break;
            
            case IntentType.ExitADS:
                ProcessExitADSIntent(op);
                break;
            
            case IntentType.WalkToward:
            case IntentType.WalkAway:
                ProcessWalkIntent(op, (WalkIntent)intent);
                break;
            
            case IntentType.SprintToward:
            case IntentType.SprintAway:
                ProcessSprintIntent(op, (SprintIntent)intent);
                break;
            
            case IntentType.SlideToward:
            case IntentType.SlideAway:
                ProcessSlideIntent(op, (SlideIntent)intent);
                break;
            
            case IntentType.Stop:
                ProcessStopIntent(op);
                break;
        }
    }

    private void ProcessFireIntent(Operator shooter, FireWeaponIntent intent)
    {
        var weapon = shooter.EquippedWeapon;
        if (weapon == null || shooter.CurrentAmmo <= 0)
            return;

        // Handle sprint-to-fire delay
        long fireTime = _time.CurrentTimeMs;
        if (shooter.MovementState == MovementState.Sprinting)
        {
            fireTime += (long)weapon.SprintToFireTimeMs;
            shooter.MovementState = MovementState.Idle;
        }

        // Schedule first shot
        var target = (shooter == Player) ? Enemy : Player;
        var shotEvent = new ShotFiredEvent(fireTime, shooter, target, _eventQueue.GetNextSequenceNumber(), _random);
        _eventQueue.Schedule(shotEvent);
    }

    private void ScheduleNextEventForIntent(Operator op, Intent intent)
    {
        if (intent.Type == IntentType.FireWeapon && op.CurrentAmmo > 0 && op.WeaponState == WeaponState.Ready && op.EquippedWeapon != null)
        {
            var target = (op == Player) ? Enemy : Player;
            long nextShotTime = _time.CurrentTimeMs + (long)op.EquippedWeapon.GetTimeBetweenShotsMs();
            var shotEvent = new ShotFiredEvent(nextShotTime, op, target, _eventQueue.GetNextSequenceNumber(), _random);
            _eventQueue.Schedule(shotEvent);
        }
        else if ((intent.Type == IntentType.WalkToward || intent.Type == IntentType.WalkAway) && 
                 op.MovementState == MovementState.Walking)
        {
            ScheduleMovementInterval(op, ((MovementIntent)intent).TowardOpponent, op.WalkSpeed);
        }
        else if ((intent.Type == IntentType.SprintToward || intent.Type == IntentType.SprintAway) && 
                 op.MovementState == MovementState.Sprinting && op.Stamina > 0)
        {
            ScheduleMovementInterval(op, ((MovementIntent)intent).TowardOpponent, op.SprintSpeed);
        }
    }

    private bool ShouldContinueIntent(Operator op, Intent intent)
    {
        switch (intent.Type)
        {
            case IntentType.FireWeapon:
                return op.CurrentAmmo > 0 && op.WeaponState == WeaponState.Ready && op.EquippedWeapon != null;
            
            case IntentType.WalkToward:
            case IntentType.WalkAway:
                return op.MovementState == MovementState.Walking;
            
            case IntentType.SprintToward:
            case IntentType.SprintAway:
                return op.MovementState == MovementState.Sprinting && op.Stamina > 0;
            
            default:
                return false; // One-shot intents don't continue
        }
    }

    private void ProcessReloadIntent(Operator op)
    {
        if (op.EquippedWeapon == null)
            return;

        op.WeaponState = WeaponState.Reloading;
        long completionTime = _time.CurrentTimeMs + op.EquippedWeapon.ReloadTimeMs;
        var reloadEvent = new ReloadCompleteEvent(completionTime, op, _eventQueue.GetNextSequenceNumber());
        _eventQueue.Schedule(reloadEvent);
        
        Console.WriteLine($"[{_time.CurrentTimeMs}ms] {op.Name} started reloading (will complete at {completionTime}ms)");
    }

    private void ProcessEnterADSIntent(Operator op)
    {
        if (op.EquippedWeapon == null)
            return;

        op.AimState = AimState.TransitioningToADS;
        long completionTime = _time.CurrentTimeMs + op.EquippedWeapon.ADSTimeMs;
        var adsEvent = new ADSCompleteEvent(completionTime, op, _eventQueue.GetNextSequenceNumber());
        _eventQueue.Schedule(adsEvent);
        
        Console.WriteLine($"[{_time.CurrentTimeMs}ms] {op.Name} started entering ADS");
    }

    private void ProcessExitADSIntent(Operator op)
    {
        op.AimState = AimState.Hip;
        Console.WriteLine($"[{_time.CurrentTimeMs}ms] {op.Name} exited ADS");
    }

    private void ProcessWalkIntent(Operator op, WalkIntent intent)
    {
        op.MovementState = MovementState.Walking;
        ScheduleMovementInterval(op, intent.TowardOpponent, op.WalkSpeed);
    }

    private void ProcessSprintIntent(Operator op, SprintIntent intent)
    {
        // Sprinting auto-exits ADS
        if (op.AimState == AimState.ADS || op.AimState == AimState.TransitioningToADS)
        {
            op.AimState = AimState.Hip;
        }
        
        op.MovementState = MovementState.Sprinting;
        ScheduleMovementInterval(op, intent.TowardOpponent, op.SprintSpeed);
    }

    private void ProcessSlideIntent(Operator op, SlideIntent intent)
    {
        // Consume stamina
        op.Stamina -= op.SlideStaminaCost;
        
        // Calculate slide distance
        float slideDistance = op.SlideDistance * (intent.TowardOpponent ? -1 : 1);
        
        op.MovementState = MovementState.Sliding;
        
        // Schedule slide completion
        long completionTime = _time.CurrentTimeMs + (long)op.SlideDurationMs;
        var slideEvent = new SlideCompleteEvent(completionTime, op, _eventQueue.GetNextSequenceNumber());
        _eventQueue.Schedule(slideEvent);
        
        // Immediately apply distance change
        op.DistanceToOpponent += slideDistance;
        
        Console.WriteLine($"[{_time.CurrentTimeMs}ms] {op.Name} started sliding {(intent.TowardOpponent ? "toward" : "away")} (will complete at {completionTime}ms)");
    }

    private void ProcessStopIntent(Operator op)
    {
        op.MovementState = MovementState.Idle;
        Console.WriteLine($"[{_time.CurrentTimeMs}ms] {op.Name} stopped");
    }

    private void ScheduleMovementInterval(Operator op, bool towardOpponent, float speed)
    {
        // Calculate distance moved in this interval
        float intervalSeconds = MOVEMENT_INTERVAL_MS / 1000f;
        float distanceMoved = speed * intervalSeconds;
        
        // Negative if toward, positive if away
        float signedDistance = distanceMoved * (towardOpponent ? -1 : 1);
        
        long nextIntervalTime = _time.CurrentTimeMs + MOVEMENT_INTERVAL_MS;
        var moveEvent = new MovementIntervalEvent(nextIntervalTime, op, signedDistance, 
            _eventQueue.GetNextSequenceNumber(), METERS_PER_COMMITMENT_UNIT);
        _eventQueue.Schedule(moveEvent);
    }

    public long CurrentTimeMs => _time.CurrentTimeMs;
}
