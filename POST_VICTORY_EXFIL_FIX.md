# Post-Victory Exfil Fix

## Problem

After winning a combat during an active infil, players were unable to exfil. The issue occurred because:

1. **Victory clears the active combat session** - This is intentional design to allow consecutive battles during a single infil
2. **Exfil required an active combat session** - The `ProcessExfil()` method checked for `ActiveSessionId` and failed if none existed
3. **Players got stuck in Infil mode** - They couldn't exfil despite having successfully completed combat

### User-Reported Symptoms

- "After I won I got sent back to base, rather than infil" (confusing - may be a separate issue)
- "I tried exfilling after infilling again, and I was still in infil mode"
- Error message: "Cannot exfil without completing a combat. You must engage and complete a combat encounter to exfil."

## Root Cause

The exfil flow was designed around a single-combat-per-infil model. When the multi-combat feature was added (allowing consecutive battles), the victory flow properly:
- Emits `ExfilSucceededEvent` (increments streak)
- Clears `ActiveCombatSessionId` (allows starting new combat)
- Keeps operator in `Infil` mode (allows consecutive battles)

However, the exfil logic still required an active combat session, creating a catch-22: players needed to complete combat to exfil, but completing combat cleared the session needed for exfil.

## Solution

### New Endpoint: `/infil/complete`

Added a new flow for completing infil successfully when there's no active combat session.

#### Server-Side Changes

**OperatorExfilService.CompleteInfilSuccessfullyAsync**
- Validates operator is in `Infil` mode
- Validates `ExfilStreak > 0` (must have completed at least one combat)
- Emits `InfilEndedEvent` with `wasSuccessful: true`
- Transitions operator to `Base` mode
- Preserves loot and streak

**OperatorService.CompleteInfilAsync**
- Thin wrapper for API layer
- Delegates to `OperatorExfilService.CompleteInfilSuccessfullyAsync`

**API Controller**
- `POST /operators/{id}/infil/complete`
- Returns 200 OK on success
- Returns 400 BadRequest if validation fails (not in Infil mode, ExfilStreak = 0)

#### Client-Side Changes

**ProcessExfil() in ConsoleClient/Program.cs**

Modified to handle two distinct cases:

1. **No active session + Infil mode** (NEW - post-victory path)
   - Calls `POST /infil/complete`
   - Shows "Exfil successful!" message
   - Returns to base

2. **Active session exists** (LEGACY - in-progress combat path)
   - Validates session is completed
   - Calls `POST /infil/outcome` with sessionId
   - Processes combat outcome

This maintains backward compatibility while enabling the new post-victory exfil flow.

### Validation Logic

The fix includes important validation to prevent abuse:

```csharp
// Must be in Infil mode to complete infil
if (aggregate.CurrentMode != OperatorMode.Infil)
    return ServiceResult.InvalidState("Cannot complete infil when not in Infil mode");

// Must have completed at least one combat successfully to exfil
if (aggregate.ExfilStreak == 0)
    return ServiceResult.InvalidState("Cannot exfil without completing at least one combat encounter");
```

This ensures players can't skip combat by immediately exfiling after starting an infil.

## Testing

### New Test: `AfterVictory_OperatorCanCompleteInfilSuccessfully`

Validates the complete post-victory exfil flow:
1. Create operator and start infil
2. Win combat (emits `ExfilSucceededEvent`, clears `ActiveCombatSessionId`)
3. Verify operator is in Infil mode with no active session
4. Call `CompleteInfilSuccessfullyAsync`
5. Verify operator is in Base mode with preserved `ExfilStreak` and XP

### Test Results

All relevant test suites pass:
- ✅ 3 InfilVictoryFlowTests (including the new test)
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
Server emits ExfilSucceededEvent
    ↓
ActiveCombatSessionId = null (but stays in Infil mode)
    ↓
MissionComplete screen: "MISSION SUCCESS"
    ↓
Player clicks OK → BaseCamp
    ↓
BaseCamp shows: [ENGAGE COMBAT] [EXFIL] [VIEW STATS]
```

### Post-Victory Exfil Flow (NEW)

```
Player clicks EXFIL
    ↓
ProcessExfil() checks for active session
    ↓
No session found, but CurrentMode = "Infil"
    ↓
POST /infil/complete
    ↓
Server validates ExfilStreak > 0
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

### Why Not Modify ProcessCombatOutcomeAsync?

We could have modified the victory path to not clear `ActiveCombatSessionId`, but this would:
- Break the multi-combat feature (can't start new combat with active session)
- Complicate session management (sessions would persist after completion)
- Require changes to auto-resume logic

The separate endpoint approach is cleaner and maintains clear separation of concerns.

### Why Validate ExfilStreak > 0?

This prevents players from:
1. Starting infil
2. Immediately exfiling without engaging in combat
3. Bypassing the risk/reward mechanic

The validation ensures at least one successful combat before allowing exfil.

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

### Migration Path

No migration needed - this is a pure addition. Existing game states will work correctly:
- Operators in Base mode: No change
- Operators in Infil with active session: Uses legacy path
- Operators in Infil after victory: Uses new path

## Related Issues

This fix addresses the core exfil problem. If users still report being "sent back to base" after victory, that would be a separate issue to investigate (possibly related to auto-cleanup or session management).
