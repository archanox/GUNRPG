using GUNRPG.Core.Events;
using GUNRPG.Core.Intents;
using GUNRPG.Core.Operators;
using GUNRPG.Core.Time;
using GUNRPG.Core.Rendering;

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
    private readonly List<ISimulationEvent> _executedEvents = new();
    private readonly List<CombatEventTimelineEntry> _timelineEntries = new();
    private readonly Dictionary<Guid, long> _activeAdsStarts = new();

    // Prevent double-scheduling shots at the same timestamp for a single operator.
    private readonly Dictionary<Guid, long> _nextScheduledShotTimeMs = new();
    
    // Prevent double-scheduling movement updates at the same timestamp for a single operator.
    private readonly Dictionary<Guid, long> _nextScheduledMovementTimeMs = new();

    // Track misses in current round to end when both miss
    private readonly HashSet<Guid> _missedInCurrentRound = new();

    public Operator Player { get; }
    public Operator Enemy { get; }
    public CombatPhase Phase { get; private set; }
    public IReadOnlyList<ISimulationEvent> ExecutedEvents => _executedEvents;
    public IReadOnlyList<CombatEventTimelineEntry> TimelineEntries => _timelineEntries;

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
        _activeAdsStarts.Clear();

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

                // Update suppression decay for both operators
                UpdateSuppressionDecay(Player, deltaMs, evt.EventTimeMs);
                UpdateSuppressionDecay(Enemy, deltaMs, evt.EventTimeMs);
                
                _time.Advance(deltaMs);
            }

            // Execute event - round end is determined by specific event types
            bool shouldEndRound = false;
            evt.Execute();
            _executedEvents.Add(evt);

            // Check for death
            if (!Player.IsAlive || !Enemy.IsAlive)
            {
                Phase = CombatPhase.Ended;
                
                // Synchronize distance at end of combat
                float averageDistance = (Player.DistanceToOpponent + Enemy.DistanceToOpponent) / 2f;
                Player.DistanceToOpponent = averageDistance;
                Enemy.DistanceToOpponent = averageDistance;
                
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

            if (evt is DamageAppliedEvent damageEvent)
            {
                var target = damageEvent.TargetId == Player.Id ? Player : Enemy;
                if (target.FlinchShotsRemaining > 0)
                {
                    float flinchWindowMs = target.EquippedWeapon?.GetTimeBetweenShotsMs() ?? 100f;
                    var flinchEnd = damageEvent.EventTimeMs + (long)flinchWindowMs;
                    _timelineEntries.Add(new CombatEventTimelineEntry(
                        "Flinch",
                        (int)damageEvent.EventTimeMs,
                        (int)flinchEnd,
                        target.Name,
                        $"Severity {target.FlinchSeverity:0.00}"));
                }
            }

            // Check if round should end (hit occurred or both missed)
            if (shouldEndRound)
            {
                Phase = CombatPhase.Planning;
                
                // Synchronize distance between both operators at end of round
                // In 1v1 combat, both operators should have the same distance value
                // Use the average in case of any drift during event processing
                float averageDistance = (Player.DistanceToOpponent + Enemy.DistanceToOpponent) / 2f;
                Player.DistanceToOpponent = averageDistance;
                Enemy.DistanceToOpponent = averageDistance;
                
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

            if (evt is ADSTransitionUpdateEvent && _activeAdsStarts.TryGetValue(evt.OperatorId, out long adsStart))
            {
                var eventOp = evt.OperatorId == Player.Id ? Player : Enemy;
                _timelineEntries.Add(new CombatEventTimelineEntry(
                    "ADS",
                    (int)adsStart,
                    (int)_time.CurrentTimeMs,
                    eventOp.Name,
                    "Complete"));
                _activeAdsStarts.Remove(evt.OperatorId);
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
        // Process movement cancellation first (immediate)
        if (intents.CancelMovement && op.IsMoving)
        {
            op.CancelMovement(_time.CurrentTimeMs, _eventQueue);
            Console.WriteLine($"[{_time.CurrentTimeMs}ms] {op.Name} cancelled movement");
        }

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

        // Process cover actions
        if (intents.Cover != CoverAction.None)
        {
            ProcessCoverAction(op, intents.Cover);
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
                _activeAdsStarts[op.Id] = _time.CurrentTimeMs;
                
                // Schedule completion event
                long completionTime = _time.CurrentTimeMs + (long)op.ADSTransitionDurationMs;
                var adsEvent = new ADSTransitionUpdateEvent(
                    completionTime,
                    op,
                    _eventQueue.GetNextSequenceNumber(),
                    actionDurationMs: (int)op.ADSTransitionDurationMs);
                _eventQueue.Schedule(adsEvent);
                _timelineEntries.Add(new CombatEventTimelineEntry(
                    "ADS",
                    (int)_time.CurrentTimeMs,
                    (int)completionTime,
                    op.Name));
                
                Console.WriteLine($"[{_time.CurrentTimeMs}ms] {op.Name} started entering ADS (will complete at {completionTime}ms)");
                break;

            case StanceAction.ExitADS:
                if (_activeAdsStarts.TryGetValue(op.Id, out long adsStart))
                {
                    _timelineEntries.Add(new CombatEventTimelineEntry(
                        "ADS",
                        (int)adsStart,
                        (int)_time.CurrentTimeMs,
                        op.Name,
                        "ExitADS"));
                    _activeAdsStarts.Remove(op.Id);
                }

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
            // Directional movement
            case MovementAction.WalkToward:
                op.MovementState = MovementState.Walking;
                op.CurrentDirection = MovementDirection.Advancing;
                ScheduleMovementUpdate(op, true, op.WalkSpeed);
                break;

            case MovementAction.WalkAway:
                op.MovementState = MovementState.Walking;
                op.CurrentDirection = MovementDirection.Retreating;
                ScheduleMovementUpdate(op, false, op.WalkSpeed);
                break;

            case MovementAction.SprintToward:
                op.MovementState = MovementState.Sprinting;
                op.CurrentDirection = MovementDirection.Advancing;
                ScheduleMovementUpdate(op, true, op.SprintSpeed);
                break;

            case MovementAction.SprintAway:
                op.MovementState = MovementState.Sprinting;
                op.CurrentDirection = MovementDirection.Retreating;
                ScheduleMovementUpdate(op, false, op.SprintSpeed);
                break;

            case MovementAction.SlideToward:
                op.CurrentDirection = MovementDirection.Advancing;
                ProcessSlide(op, true);
                break;
                
            case MovementAction.SlideAway:
                op.CurrentDirection = MovementDirection.Retreating;
                ProcessSlide(op, false);
                break;

            // State-based movement (non-directional)
            case MovementAction.Crouch:
                op.CurrentDirection = MovementDirection.Holding;
                op.StartMovement(MovementState.Crouching, -1, _time.CurrentTimeMs, _eventQueue);
                Console.WriteLine($"[{_time.CurrentTimeMs}ms] {op.Name} started crouching");
                break;
                
            case MovementAction.Stand:
                op.CurrentDirection = MovementDirection.Holding;
                break;
        }
    }

    private void ProcessCoverAction(Operator op, CoverAction cover)
    {
        switch (cover)
        {
            case CoverAction.EnterPartial:
                if (op.EnterCover(CoverState.Partial, _time.CurrentTimeMs, _eventQueue))
                {
                    Console.WriteLine($"[{_time.CurrentTimeMs}ms] {op.Name} entered partial cover");
                }
                break;

            case CoverAction.EnterFull:
                if (op.EnterCover(CoverState.Full, _time.CurrentTimeMs, _eventQueue))
                {
                    Console.WriteLine($"[{_time.CurrentTimeMs}ms] {op.Name} entered full cover");
                }
                break;

            case CoverAction.Exit:
                if (op.ExitCover(_time.CurrentTimeMs, _eventQueue))
                {
                    Console.WriteLine($"[{_time.CurrentTimeMs}ms] {op.Name} exited cover");
                }
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
        var reloadEvent = new ReloadCompleteEvent(
            completionTime,
            op,
            _eventQueue.GetNextSequenceNumber(),
            actionDurationMs: op.EquippedWeapon.ReloadTimeMs);
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
        var slideEvent = new SlideCompleteEvent(
            completionTime,
            op,
            _eventQueue.GetNextSequenceNumber(),
            actionDurationMs: (int)op.SlideDurationMs);
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
        
        // Determine opponent for distance synchronization
        Operator opponent = (op == Player) ? Enemy : Player;
        
        var moveEvent = new MovementIntervalEvent(
            nextUpdateTime,
            op,
            signedDistance,
            _eventQueue.GetNextSequenceNumber(),
            intervalDurationMs: MOVEMENT_UPDATE_INTERVAL_MS,
            metersPerCommitmentUnit: DISABLE_MOVEMENT_REACTIONS,
            opponent: opponent);
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

    // Suppression tracking
    private readonly Dictionary<Guid, long> _suppressionStartTimes = new();
    private readonly Dictionary<Guid, float> _peakSuppressionLevels = new();

    /// <summary>
    /// Updates suppression decay and emits suppression ended events when appropriate.
    /// </summary>
    private void UpdateSuppressionDecay(Operator op, long deltaMs, long currentTimeMs)
    {
        if (op.SuppressionLevel <= 0f)
            return;

        // Track suppression start time for timeline - use the first suppression application time
        if (!_suppressionStartTimes.ContainsKey(op.Id))
        {
            // Use LastSuppressionApplicationMs as the start time if available,
            // otherwise fall back to current time (shouldn't happen normally)
            _suppressionStartTimes[op.Id] = op.LastSuppressionApplicationMs ?? currentTimeMs;
            _peakSuppressionLevels[op.Id] = op.SuppressionLevel;
        }
        else
        {
            // Track peak suppression
            if (op.SuppressionLevel > _peakSuppressionLevels[op.Id])
            {
                _peakSuppressionLevels[op.Id] = op.SuppressionLevel;
            }
        }

        bool suppressionEnded = op.UpdateSuppressionDecay(deltaMs, currentTimeMs);

        if (suppressionEnded && _suppressionStartTimes.TryGetValue(op.Id, out long startTime))
        {
            // Emit suppression ended event
            long duration = currentTimeMs - startTime;
            float peakSeverity = _peakSuppressionLevels.GetValueOrDefault(op.Id, 0f);

            _eventQueue.Schedule(new SuppressionEndedEvent(
                currentTimeMs,
                op,
                duration,
                peakSeverity,
                _eventQueue.GetNextSequenceNumber()));

            // Add timeline entry for suppression period
            _timelineEntries.Add(new CombatEventTimelineEntry(
                "Suppression",
                (int)startTime,
                (int)currentTimeMs,
                op.Name,
                $"Peak {peakSeverity:0.00}"));

            _suppressionStartTimes.Remove(op.Id);
            _peakSuppressionLevels.Remove(op.Id);
        }
        else if (op.SuppressionLevel <= 0f)
        {
            // Suppression fully decayed without ever crossing the suppression threshold.
            // Clean up tracking dictionaries without emitting an event or timeline entry.
            _suppressionStartTimes.Remove(op.Id);
            _peakSuppressionLevels.Remove(op.Id);
        }
    }
}
