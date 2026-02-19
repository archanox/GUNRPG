# Post-Victory Exfil Fix & Combat-Free Exfil

## Problem

After winning a combat during an active infil, players were unable to exfil. The issue occurred because:

1. **Victory clears the active combat session** - This is intentional design to allow consecutive battles during a single infil
2. **Exfil required an active combat session** - The `ProcessExfil()` method checked for `ActiveSessionId` and failed if none existed
3. **Players got stuck in Infil mode** - They couldn't exfil despite having successfully completed combat

Additionally, the original design incorrectly required players to complete combat before exfiling, but the intended design is to allow exfil at any time during an infil.

### User-Reported Symptoms

- "After I won I got sent back to base, rather than infil" (confusing - may be a separate issue)
- "I tried exfilling after infilling again, and I was still in infil mode"
- Error message: "Cannot exfil without completing a combat. You must engage and complete a combat encounter to exfil."

## Root Cause

The exfil flow was designed around a single-combat-per-infil model. When the multi-combat feature was added (allowing consecutive battles), the victory flow properly:
- Emits `CombatVictoryEvent` (clears `ActiveCombatSessionId`)
- Clears `ActiveCombatSessionId` (allows starting new combat)
- Keeps operator in `Infil` mode (allows consecutive battles)

However, the exfil logic still required an active combat session, creating a catch-22: players needed to complete combat to exfil, but completing combat cleared the session needed for exfil.

## Solution

### New Endpoint: `POST /operators/{id}/infil/complete`

Added a new flow for completing infil successfully at any time, with or without combat.

#### Server-Side Changes

**OperatorExfilService.CompleteInfilSuccessfullyAsync**
- Validates operator is in `Infil` mode
- **No combat requirement** - Players can exfil immediately after starting infil
- Emits `InfilEndedEvent` with `wasSuccessful: true`
- Transitions operator to `Base` mode
- Preserves loot and streak (if any combat was completed)

**OperatorService.CompleteInfilAsync**
- Thin wrapper for API layer
- Delegates to `OperatorExfilService.CompleteInfilSuccessfullyAsync`

**API Controller**
- `POST /operators/{id}/infil/complete`
- Returns 200 OK on success
- Returns 400 BadRequest if validation fails (not in Infil mode)

#### Client-Side Changes

**ProcessExfil() in ConsoleClient/Program.cs**

Modified to handle two distinct cases:

1. **No active session + Infil mode** (NEW - post-victory or no-combat path)
   - Calls `POST /operators/{id}/infil/complete`
   - Shows "Exfil successful!" message
   - Returns to base

2. **Active session exists** (LEGACY - in-progress combat path)
   - Validates session is completed
   - Calls `POST /infil/outcome` with sessionId
   - Processes combat outcome

This maintains backward compatibility while enabling the new exfil flow.

### Validation Logic

The fix includes minimal validation:

```csharp
// Must be in Infil mode to complete infil
if (aggregate.CurrentMode != OperatorMode.Infil)
    return ServiceResult.InvalidState("Cannot complete infil when not in Infil mode");
```

No combat requirement - players can exfil at any time during an active infil.

## Testing

### New Test: `OperatorCanExfilImmediatelyWithoutCombat`

Validates that players can exfil immediately after starting infil:
1. Create operator and start infil
2. Verify operator is in Infil mode with ExfilStreak = 0 (no combat)
3. Call `CompleteInfilSuccessfullyAsync`
4. Verify operator is in Base mode with ExfilStreak still 0

### Existing Test: `AfterVictory_OperatorCanCompleteInfilSuccessfully`

Validates the complete post-victory exfil flow:
1. Create operator and start infil
2. Win combat (emits `CombatVictoryEvent`, clears `ActiveCombatSessionId`)
3. Verify operator is in Infil mode with no active session
4. Call `CompleteInfilSuccessfullyAsync`
5. Verify operator is in Base mode with preserved `ExfilStreak` and XP

### Test Results

All relevant test suites pass:
- ✅ 4 InfilVictoryFlowTests (including the new test)
- ✅ 39 OperatorExfilServiceTests
- ✅ 19 OperatorAggregateTests
- ✅ 112 Mode-related tests

## Flow Diagrams

### Victory Flow (Corrected)

```
Player wins combat
    ↓
ProcessCombatOutcome() → POST /infil/outcome
    ↓
Server emits CombatVictoryEvent
    ↓
ActiveCombatSessionId = null (but stays in Infil mode)
    ↓
MissionComplete screen: "MISSION SUCCESS"
    ↓
Player clicks OK → BaseCamp
    ↓
BaseCamp shows: [ENGAGE COMBAT] [EXFIL] [VIEW STATS]
```

### Exfil Flow (NEW - No Combat Required)

```
Player starts infil
    ↓
BaseCamp shows: [ENGAGE COMBAT] [EXFIL] [VIEW STATS]
    ↓
Player clicks EXFIL (without engaging in combat)
    ↓
ProcessExfil() checks for active session
    ↓
No session found, CurrentMode = "Infil"
    ↓
POST /operators/{id}/infil/complete
    ↓
Server validates CurrentMode = Infil
    ↓
Emit InfilEndedEvent (wasSuccessful: true)
    ↓
Operator → Base mode
    ↓
Client shows "Exfil successful!" → BaseCamp
    ↓
BaseCamp shows: [INFIL] [CHANGE LOADOUT] [TREAT WOUNDS] etc.
```

## Design Notes

### Why Allow Exfil Without Combat?

The design allows players full agency over their infil experience:
- **Risk assessment** - Players can scout and decide to exfil if conditions are unfavorable
- **Resource management** - Exit before taking damage or using consumables
- **Time management** - Quick in/out without forced engagement
- **Learning/Training** - New players can practice infil mechanics without combat pressure

This creates a more flexible and player-friendly system.

### Why Not Modify ProcessCombatOutcomeAsync?

We could have modified the victory path to not clear `ActiveCombatSessionId`, but this would:
- Break the multi-combat feature (can't start new combat with active session)
- Complicate session management (sessions would persist after completion)
- Require changes to auto-resume logic

The separate endpoint approach is cleaner and maintains clear separation of concerns.

### Backward Compatibility

The legacy path (with active session) is preserved for edge cases:
- Old game states loaded from database
- Potential race conditions during combat completion
- Future features that might use different session management

## Future Considerations

### Potential Enhancements

1. **Multiple combats before exfil** - Already supported! Players can win multiple combats and only exfil when ready
2. **Exfil bonus for longer streaks** - Could be added by checking `ExfilStreak` in `CompleteInfilSuccessfullyAsync`
3. **Time-based exfil restrictions** - Could validate minimum time in infil before allowing exfil
4. **Exfil cost/penalty for no combat** - Could apply a resource cost for exfiling without engaging

### Migration Path

No migration needed - this is a pure addition. Existing game states will work correctly:
- Operators in Base mode: No change
- Operators in Infil with active session: Uses legacy path
- Operators in Infil after victory: Uses new path
- Operators in Infil without combat: Uses new path

## Related Issues

This fix addresses the core exfil problem. If users still report being "sent back to base" after victory, that would be a separate issue to investigate (possibly related to auto-cleanup or session management).
