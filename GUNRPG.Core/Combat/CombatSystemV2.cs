using GUNRPG.Core.Events;
using GUNRPG.Core.Intents;
using GUNRPG.Core.Operators;
using GUNRPG.Core.Time;

namespace GUNRPG.Core.Combat;

/// <summary>
/// Main combat system orchestrator with support for simultaneous intents.
/// Manages event queue, time, and operator state during combat.
/// </summary>
public class CombatSystemV2
{
    private readonly SimulationTime _time;
    private readonly EventQueue _eventQueue;
    private readonly Random _random;

    public Operator Player { get; }
    public Operator Enemy { get; }
    public CombatPhase Phase { get; private set; }

    private SimultaneousIntents? _playerIntents;
    private SimultaneousIntents? _enemyIntents;

    // Reaction window configuration (much shorter now)
    private const int MICRO_REACTION_INTERVAL_MS = 75; // 75ms micro-reactions
    private const int MOVEMENT_UPDATE_INTERVAL_MS = 100; // Update distance every 100ms
    
    // Track last reaction time for scheduling
    private long _lastReactionTimeMs = 0;

    public CombatSystemV2(Operator player, Operator enemy, int? seed = null)
    {
        _time = new SimulationTime();
        _eventQueue = new EventQueue();
        _random = seed.HasValue ? new Random(seed.Value) : new Random();
        
        Player = player;
        Enemy = enemy;
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

        Phase = CombatPhase.Executing;

        // Process both operators' intents to schedule initial events
        if (_playerIntents != null && _playerIntents.HasAnyAction())
            ProcessSimultaneousIntents(Player, _playerIntents);
        
        if (_enemyIntents != null && _enemyIntents.HasAnyAction())
            ProcessSimultaneousIntents(Enemy, _enemyIntents);

        // Schedule first micro-reaction window
        ScheduleNextMicroReaction();

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
                _lastReactionTimeMs = _time.CurrentTimeMs;
                
                Console.WriteLine($"\n=== REACTION WINDOW at {_time.CurrentTimeMs}ms ===");
                
                // Show detailed status with ADS progress
                float playerADS = Player.GetADSProgress(_time.CurrentTimeMs);
                float enemyADS = Enemy.GetADSProgress(_time.CurrentTimeMs);
                
                Console.WriteLine($"Player: HP {Player.Health:F0}/{Player.MaxHealth:F0}, Ammo {Player.CurrentAmmo}, Distance {Player.DistanceToOpponent:F1}m, ADS {playerADS*100:F0}%");
                Console.WriteLine($"Enemy:  HP {Enemy.Health:F0}/{Enemy.MaxHealth:F0}, Ammo {Enemy.CurrentAmmo}, Distance {Enemy.DistanceToOpponent:F1}m, ADS {enemyADS*100:F0}%");
                Console.WriteLine();
                return true;
            }

            // Continue existing continuous intents
            ContinueActiveIntents();
        }

        // No more events and no reaction window
        Phase = CombatPhase.Planning;
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
        if (intents.Movement != MovementAction.None)
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
        if (movement == MovementAction.SprintToward || movement == MovementAction.SprintAway)
        {
            if (op.AimState == AimState.ADS || op.AimState == AimState.TransitioningToADS)
            {
                op.AimState = AimState.Hip;
                op.ADSTransitionStartMs = null;
                Console.WriteLine($"[{_time.CurrentTimeMs}ms] {op.Name} auto-exited ADS due to sprint");
            }
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
            op.MovementState = MovementState.Walking; // Reduce to walk
        }

        // Schedule first shot
        var target = (op == Player) ? Enemy : Player;
        var shotEvent = new ShotFiredEvent(fireTime, op, target, _eventQueue.GetNextSequenceNumber(), _random);
        _eventQueue.Schedule(shotEvent);
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
        // Calculate distance moved in this interval
        float intervalSeconds = MOVEMENT_UPDATE_INTERVAL_MS / 1000f;
        float distanceMoved = speed * intervalSeconds;
        
        // Negative if toward, positive if away
        float signedDistance = distanceMoved * (towardOpponent ? -1 : 1);
        
        long nextUpdateTime = _time.CurrentTimeMs + MOVEMENT_UPDATE_INTERVAL_MS;
        var moveEvent = new MovementIntervalEvent(nextUpdateTime, op, signedDistance, 
            _eventQueue.GetNextSequenceNumber(), metersPerCommitmentUnit: 10); // Disable movement-based reactions
        _eventQueue.Schedule(moveEvent);
    }

    private void ScheduleNextMicroReaction()
    {
        long nextReactionTime = _lastReactionTimeMs + MICRO_REACTION_INTERVAL_MS;
        
        // Schedule for both operators
        var playerReactionEvent = new MicroReactionEvent(nextReactionTime, Player.Id, _eventQueue.GetNextSequenceNumber());
        _eventQueue.Schedule(playerReactionEvent);
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
            var target = (op == Player) ? Enemy : Player;
            long nextShotTime = _time.CurrentTimeMs + (long)op.EquippedWeapon.GetTimeBetweenShotsMs();
            var shotEvent = new ShotFiredEvent(nextShotTime, op, target, _eventQueue.GetNextSequenceNumber(), _random);
            _eventQueue.Schedule(shotEvent);
        }
        else if (intents.Primary == PrimaryAction.Fire)
        {
            // Stop firing if conditions no longer met
            op.IsActivelyFiring = false;
        }

        // Continue movement
        if (intents.Movement != MovementAction.None && 
            intents.Movement != MovementAction.SlideToward && 
            intents.Movement != MovementAction.SlideAway)
        {
            bool towardOpponent = intents.Movement == MovementAction.WalkToward || 
                                  intents.Movement == MovementAction.SprintToward;
            float speed = (intents.Movement == MovementAction.SprintToward || 
                          intents.Movement == MovementAction.SprintAway) ? op.SprintSpeed : op.WalkSpeed;
            
            // Check if still valid
            if (intents.Movement == MovementAction.SprintToward || intents.Movement == MovementAction.SprintAway)
            {
                if (op.Stamina <= 0 || op.MovementState != MovementState.Sprinting)
                {
                    return; // Stop sprinting
                }
            }

            ScheduleMovementUpdate(op, towardOpponent, speed);
        }
    }

    public long CurrentTimeMs => _time.CurrentTimeMs;
}
