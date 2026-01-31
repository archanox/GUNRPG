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
/// Controls optional debug output for combat simulation.
/// </summary>
public sealed class CombatDebugOptions
{
    public bool VerboseShotLogs { get; set; }
}

/// <summary>
/// Main combat system orchestrator with support for simultaneous intents.
/// Manages event queue, time, and operator state during combat.
/// </summary>
public class CombatSystemV2
{
    private readonly SimulationTime _time;
    private readonly EventQueue _eventQueue;
    private readonly Random _random;

    // Prevent double-scheduling shots at the same timestamp for a single operator.
    private readonly Dictionary<Guid, long> _nextScheduledShotTimeMs = new();
    
    // Prevent double-scheduling movement updates at the same timestamp for a single operator.
    private readonly Dictionary<Guid, long> _nextScheduledMovementTimeMs = new();

    // Track misses in current round to end when both miss
    private readonly HashSet<Guid> _missedInCurrentRound = new();

    public Operator Player { get; }
    public Operator Enemy { get; }
    public CombatPhase Phase { get; private set; }

    private SimultaneousIntents? _playerIntents;
    private SimultaneousIntents? _enemyIntents;

    private const int MOVEMENT_UPDATE_INTERVAL_MS = 100; // Update distance every 100ms
    private const int DISABLE_MOVEMENT_REACTIONS = 10; // High threshold to effectively disable movement-based reactions
    

    public CombatDebugOptions DebugOptions { get; } = new() { VerboseShotLogs = true };

    public CombatSystemV2(Operator player, Operator enemy, int? seed = null, CombatDebugOptions? debugOptions = null)
    {
        _time = new SimulationTime();
        _eventQueue = new EventQueue();
        _random = seed.HasValue ? new Random(seed.Value) : new Random();
        
        Player = player;
        Enemy = enemy;
        if (debugOptions != null)
            DebugOptions = debugOptions;
        Phase = CombatPhase.Planning;
    }

    /// <summary>
    /// Submits simultaneous intents for an operator.
    /// Can only be called during Planning phase.
    /// </summary>
    public (bool success, string? errorMessage) SubmitIntents(Operator op, SimultaneousIntents intents)
    {
        if (Phase != CombatPhase.Planning)
            return (false, "Can only submit intents during planning phase");

        var validation = intents.Validate(op);
        if (!validation.isValid)
            return (false, validation.errorMessage);

        intents.SubmittedAtMs = _time.CurrentTimeMs;

        if (op == Player)
            _playerIntents = intents;
        else if (op == Enemy)
            _enemyIntents = intents;

        return (true, null);
    }

    /// <summary>
    /// Begins execution phase, processing intents and running events.
    /// </summary>
    public void BeginExecution()
    {
        if (Phase != CombatPhase.Planning)
            return;

        // Round execution ends when all events are processed. Starting a new planning->execution
        // cycle should honor ONLY newly submitted intents, but we preserve in-flight bullets
        // (DamageAppliedEvent, ShotMissedEvent) so they can land even across planning phases.
        _eventQueue.ClearExceptInFlightBullets();
        _nextScheduledShotTimeMs.Clear();
        _nextScheduledMovementTimeMs.Clear();
        _missedInCurrentRound.Clear();

        Phase = CombatPhase.Executing;

        // Process both operators' intents to schedule initial events
        if (_playerIntents != null && _playerIntents.HasAnyAction())
            ProcessSimultaneousIntents(Player, _playerIntents);
        
        if (_enemyIntents != null && _enemyIntents.HasAnyAction())
            ProcessSimultaneousIntents(Enemy, _enemyIntents);

        Console.WriteLine($"\n=== EXECUTION PHASE STARTED at {_time.CurrentTimeMs}ms ===\n");
    }

    /// <summary>
    /// Executes events until round end conditions are met:
    /// - Either player or enemy is hit, OR
    /// - Both players miss their shots
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

            // Execute event - round end is determined by specific event types
            bool shouldEndRound = false;
            evt.Execute();

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

            // A hit (damage applied) always ends the round
            if (evt is DamageAppliedEvent)
            {
                shouldEndRound = true;
            }

            // Track misses for "both miss" round end condition
            if (evt is ShotMissedEvent)
            {
                _missedInCurrentRound.Add(evt.OperatorId);
                
                // If both operators have missed, end the round
                if (_missedInCurrentRound.Contains(Player.Id) && _missedInCurrentRound.Contains(Enemy.Id))
                {
                    shouldEndRound = true;
                }
            }

            // Check if round should end (hit occurred or both missed)
            if (shouldEndRound)
            {
                Phase = CombatPhase.Planning;
                
                Console.WriteLine($"\n=== ROUND COMPLETE at {_time.CurrentTimeMs}ms ===");
                float plADS = Player.GetADSProgress(_time.CurrentTimeMs);
                float enADS = Enemy.GetADSProgress(_time.CurrentTimeMs);
                Console.WriteLine($"Player: HP {Player.Health:F0}/{Player.MaxHealth:F0}, Ammo {Player.CurrentAmmo}, Distance {Player.DistanceToOpponent:F1}m, ADS {plADS*100:F0}%");
                Console.WriteLine($"Enemy:  HP {Enemy.Health:F0}/{Enemy.MaxHealth:F0}, Ammo {Enemy.CurrentAmmo}, Distance {Enemy.DistanceToOpponent:F1}m, ADS {enADS*100:F0}%");
                Console.WriteLine();
                
                return true;
            }

            // Continue existing continuous intents only for repeating action events (shots, movement)
            // Only continue for the operator whose event just executed
            if (evt is ShotFiredEvent || evt is MovementIntervalEvent)
            {
                Operator eventOp = evt.OperatorId == Player.Id ? Player : Enemy;
                SimultaneousIntents? intents = evt.OperatorId == Player.Id ? _playerIntents : _enemyIntents;
                
                if (intents != null)
                {
                    ContinueOperatorIntents(eventOp, intents);
                }
            }
        }

        // All events processed, round complete
        Phase = CombatPhase.Planning;
        
        // Show round summary
        Console.WriteLine($"\n=== ROUND COMPLETE at {_time.CurrentTimeMs}ms ===");
        float playerADS = Player.GetADSProgress(_time.CurrentTimeMs);
        float enemyADS = Enemy.GetADSProgress(_time.CurrentTimeMs);
        Console.WriteLine($"Player: HP {Player.Health:F0}/{Player.MaxHealth:F0}, Ammo {Player.CurrentAmmo}, Distance {Player.DistanceToOpponent:F1}m, ADS {playerADS*100:F0}%");
        Console.WriteLine($"Enemy:  HP {Enemy.Health:F0}/{Enemy.MaxHealth:F0}, Ammo {Enemy.CurrentAmmo}, Distance {Enemy.DistanceToOpponent:F1}m, ADS {enemyADS*100:F0}%");
        Console.WriteLine();
        
        return true;
    }

    /// <summary>
    /// Cancels the current intents for an operator.
    /// </summary>
    public void CancelIntents(Operator op)
    {
        _eventQueue.RemoveEventsForOperator(op.Id);
        
        if (op == Player)
            _playerIntents = null;
        else if (op == Enemy)
            _enemyIntents = null;
            
        // Stop active firing
        op.IsActivelyFiring = false;
    }

    private void ProcessSimultaneousIntents(Operator op, SimultaneousIntents intents)
    {
        // Process stance first (ADS changes)
        if (intents.Stance != StanceAction.None)
        {
            ProcessStanceAction(op, intents.Stance);
        }

        // Process movement
        if (intents.Movement != MovementAction.Stand)
        {
            ProcessMovementAction(op, intents.Movement);
        }

        // Process primary action last
        if (intents.Primary != PrimaryAction.None)
        {
            ProcessPrimaryAction(op, intents.Primary);
        }
    }

    private void ProcessStanceAction(Operator op, StanceAction stance)
    {
        switch (stance)
        {
            case StanceAction.EnterADS:
                if (op.EquippedWeapon == null)
                    return;

                // Cannot initiate ADS while actively firing
                if (op.IsActivelyFiring)
                {
                    Console.WriteLine($"[{_time.CurrentTimeMs}ms] {op.Name} cannot enter ADS while firing");
                    return;
                }

                op.AimState = AimState.TransitioningToADS;
                op.ADSTransitionStartMs = _time.CurrentTimeMs;
                op.ADSTransitionDurationMs = op.EquippedWeapon.ADSTimeMs;
                
                // Schedule completion event
                long completionTime = _time.CurrentTimeMs + (long)op.ADSTransitionDurationMs;
                var adsEvent = new ADSTransitionUpdateEvent(completionTime, op, _eventQueue.GetNextSequenceNumber());
                _eventQueue.Schedule(adsEvent);
                
                Console.WriteLine($"[{_time.CurrentTimeMs}ms] {op.Name} started entering ADS (will complete at {completionTime}ms)");
                break;

            case StanceAction.ExitADS:
                op.AimState = AimState.Hip;
                op.ADSTransitionStartMs = null;
                Console.WriteLine($"[{_time.CurrentTimeMs}ms] {op.Name} exited ADS");
                break;
        }
    }

    private void ProcessMovementAction(Operator op, MovementAction movement)
    {
        // Sprinting auto-exits ADS
        if ((movement == MovementAction.SprintToward || movement == MovementAction.SprintAway) &&
            (op.AimState == AimState.ADS || op.AimState == AimState.TransitioningToADS))
        {
            op.AimState = AimState.Hip;
            op.ADSTransitionStartMs = null;
            Console.WriteLine($"[{_time.CurrentTimeMs}ms] {op.Name} auto-exited ADS due to sprint");
        }

        switch (movement)
        {
            case MovementAction.WalkToward:
                op.MovementState = MovementState.Walking;
                ScheduleMovementUpdate(op, true, op.WalkSpeed);
                break;

            case MovementAction.WalkAway:
                op.MovementState = MovementState.Walking;
                ScheduleMovementUpdate(op, false, op.WalkSpeed);
                break;

            case MovementAction.SprintToward:
                op.MovementState = MovementState.Sprinting;
                ScheduleMovementUpdate(op, true, op.SprintSpeed);
                break;

            case MovementAction.SprintAway:
                op.MovementState = MovementState.Sprinting;
                ScheduleMovementUpdate(op, false, op.SprintSpeed);
                break;

            case MovementAction.SlideToward:
            case MovementAction.SlideAway:
                ProcessSlide(op, movement == MovementAction.SlideToward);
                break;
        }
    }

    private void ProcessPrimaryAction(Operator op, PrimaryAction primary)
    {
        switch (primary)
        {
            case PrimaryAction.Fire:
                ProcessFireAction(op);
                break;

            case PrimaryAction.Reload:
                ProcessReloadAction(op);
                break;
        }
    }

    private void ProcessFireAction(Operator op)
    {
        var weapon = op.EquippedWeapon;
        if (weapon == null || op.CurrentAmmo <= 0)
            return;

        // Mark as actively firing
        op.IsActivelyFiring = true;

        // Handle sprint-to-fire delay
        long fireTime = _time.CurrentTimeMs;
        if (op.MovementState == MovementState.Sprinting)
        {
            fireTime += (long)weapon.SprintToFireTimeMs;
            op.MovementState = MovementState.Walking; // Transition from sprint to walk when firing
        }

        ScheduleShotIfNeeded(op, fireTime);
    }

    private void ProcessReloadAction(Operator op)
    {
        if (op.EquippedWeapon == null)
            return;

        op.WeaponState = WeaponState.Reloading;
        long completionTime = _time.CurrentTimeMs + op.EquippedWeapon.ReloadTimeMs;
        var reloadEvent = new ReloadCompleteEvent(completionTime, op, _eventQueue.GetNextSequenceNumber());
        _eventQueue.Schedule(reloadEvent);
        
        Console.WriteLine($"[{_time.CurrentTimeMs}ms] {op.Name} started reloading (will complete at {completionTime}ms)");
    }

    private void ProcessSlide(Operator op, bool towardOpponent)
    {
        // Consume stamina
        op.Stamina -= op.SlideStaminaCost;
        
        // Calculate slide distance
        float slideDistance = op.SlideDistance * (towardOpponent ? -1 : 1);
        
        op.MovementState = MovementState.Sliding;
        
        // Schedule slide completion
        long completionTime = _time.CurrentTimeMs + (long)op.SlideDurationMs;
        var slideEvent = new SlideCompleteEvent(completionTime, op, _eventQueue.GetNextSequenceNumber());
        _eventQueue.Schedule(slideEvent);
        
        // Immediately apply distance change
        op.DistanceToOpponent += slideDistance;
        
        Console.WriteLine($"[{_time.CurrentTimeMs}ms] {op.Name} slid {slideDistance:F1}m (distance now: {op.DistanceToOpponent:F1}m)");
    }

    private void ScheduleMovementUpdate(Operator op, bool towardOpponent, float speed)
    {
        long nextUpdateTime = _time.CurrentTimeMs + MOVEMENT_UPDATE_INTERVAL_MS;
        
        // Check if we've already scheduled a movement for this operator at this time
        if (_nextScheduledMovementTimeMs.TryGetValue(op.Id, out long scheduledTime) && scheduledTime >= nextUpdateTime)
        {
            // Already have a movement scheduled at or after this time, don't double-schedule
            return;
        }
        
        // Calculate distance moved in this interval
        float intervalSeconds = MOVEMENT_UPDATE_INTERVAL_MS / 1000f;
        float distanceMoved = speed * intervalSeconds;
        
        // Negative if toward, positive if away
        float signedDistance = distanceMoved * (towardOpponent ? -1 : 1);
        
        var moveEvent = new MovementIntervalEvent(nextUpdateTime, op, signedDistance, 
            _eventQueue.GetNextSequenceNumber(), metersPerCommitmentUnit: DISABLE_MOVEMENT_REACTIONS);
        _eventQueue.Schedule(moveEvent);
        
        // Track that we've scheduled this movement
        _nextScheduledMovementTimeMs[op.Id] = nextUpdateTime;
    }

    private void ContinueActiveIntents()
    {
        // Continue player intents
        if (_playerIntents != null)
        {
            ContinueOperatorIntents(Player, _playerIntents);
        }

        // Continue enemy intents
        if (_enemyIntents != null)
        {
            ContinueOperatorIntents(Enemy, _enemyIntents);
        }
    }

    private void ContinueOperatorIntents(Operator op, SimultaneousIntents intents)
    {
        // Continue primary action (firing)
        if (intents.Primary == PrimaryAction.Fire && 
            op.CurrentAmmo > 0 && 
            op.WeaponState == WeaponState.Ready && 
            op.EquippedWeapon != null &&
            op.IsActivelyFiring)
        {
            long nextShotTime = _time.CurrentTimeMs + (long)op.EquippedWeapon.GetTimeBetweenShotsMs();
            ScheduleShotIfNeeded(op, nextShotTime);
        }
        else if (intents.Primary == PrimaryAction.Fire)
        {
            // Stop firing if conditions no longer met
            op.IsActivelyFiring = false;
        }

        // Continue movement
        if (intents.Movement != MovementAction.Stand && 
            intents.Movement != MovementAction.SlideToward && 
            intents.Movement != MovementAction.SlideAway)
        {
            bool towardOpponent = intents.Movement == MovementAction.WalkToward || 
                                  intents.Movement == MovementAction.SprintToward;
            float speed = (intents.Movement == MovementAction.SprintToward || 
                          intents.Movement == MovementAction.SprintAway) ? op.SprintSpeed : op.WalkSpeed;
            
            // Check if still valid
            if ((intents.Movement == MovementAction.SprintToward || intents.Movement == MovementAction.SprintAway) &&
                (op.Stamina <= 0 || op.MovementState != MovementState.Sprinting))
            {
                return; // Stop sprinting
            }

            ScheduleMovementUpdate(op, towardOpponent, speed);
        }
    }

    public long CurrentTimeMs => _time.CurrentTimeMs;

    private void ScheduleShotIfNeeded(Operator op, long shotTime)
    {
        if (op.EquippedWeapon == null || op.CurrentAmmo <= 0 || op.WeaponState != WeaponState.Ready)
            return;

        if (_nextScheduledShotTimeMs.TryGetValue(op.Id, out long existingTime) && shotTime <= existingTime)
            return;

        _nextScheduledShotTimeMs[op.Id] = shotTime;

        var target = (op == Player) ? Enemy : Player;
        var shotEvent = new ShotFiredEvent(shotTime, op, target, _eventQueue.GetNextSequenceNumber(), _random, _eventQueue, DebugOptions);
        _eventQueue.Schedule(shotEvent);
    }
}
