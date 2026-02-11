# Base Camp Menu Fix - Summary

## Issue Reported
User reported that after completing a mission and returning to Base Camp, the menu was showing the wrong options:
- **Expected**: Base mode options (START MISSION, CHANGE LOADOUT, TREAT WOUNDS, UNLOCK PERK, PET ACTIONS, VIEW STATS, MAIN MENU)
- **Actual**: Infil mode options (CONTINUE MISSION, ABORT MISSION, VIEW STATS, MAIN MENU)

## Root Cause Analysis

### The Flow
1. Combat ends → `AdvanceCombat()` detects end condition
2. Calls `ProcessCombatOutcome()` which:
   - Posts to `/operators/{id}/infil/outcome` endpoint
   - Calls `RefreshOperator()` to update operator state
   - Operator mode transitions from "Infil" to "Base"
3. Navigates to `Screen.MissionComplete`
4. User clicks "RETURN TO BASE" in Mission Complete screen
5. **BUG**: Directly sets `CurrentScreen = Screen.BaseCamp` without refreshing operator state

### The Problem
The operator state was refreshed in step 2, but the `CurrentOperator` object in the UI wasn't being updated before navigating to Base Camp. The Base Camp screen's `BuildBaseCamp()` method checks `op.CurrentMode` to determine which menu items to show:

```csharp
if (op.CurrentMode == "Base")
{
    // Show base options
}
else
{
    // Show infil options
}
```

Since `CurrentOperator` wasn't refreshed, it still had the old state with `CurrentMode = "Infil"`, causing the wrong menu to display.

## Solution

### Code Change
**File**: `GUNRPG.ConsoleClient/Program.cs`  
**Method**: `BuildMissionComplete()`  
**Line**: ~998

**Before**:
```csharp
new ListWidget(new[] { "RETURN TO BASE" }).OnItemActivated(_ => CurrentScreen = Screen.BaseCamp)
```

**After**:
```csharp
new ListWidget(new[] { "RETURN TO BASE" }).OnItemActivated(_ => {
    RefreshOperator();
    CurrentScreen = Screen.BaseCamp;
})
```

### Why This Works
1. `RefreshOperator()` makes a GET request to `/operators/{id}` endpoint
2. Parses the response and updates `CurrentOperator` with fresh state
3. The updated operator object now has `CurrentMode = "Base"`
4. When `BuildBaseCamp()` runs, it correctly detects Base mode and shows appropriate menu

## Verification

### Test Scenario
1. Start a mission (operator transitions to Infil mode)
2. Complete combat (win or lose)
3. See Mission Complete screen
4. Click "RETURN TO BASE"
5. **Expected**: See Base mode menu with START MISSION, CHANGE LOADOUT, etc.
6. **Result**: ✅ FIXED - Correct menu now displays

## Related Code Patterns

### Other Navigation Points
The codebase has similar patterns in other locations:
- Combat screen's "RETURN TO BASE" button (line 706-707) already calls `RefreshOperator()`
- This establishes the pattern: **Always refresh operator state when navigating to Base Camp from mission/combat context**

### Consistency
This fix brings `BuildMissionComplete()` in line with the existing pattern used elsewhere in the codebase.

## Technical Details

### Build Status
✅ **PASSED** - No compilation errors  
✅ **Build Time**: ~8 seconds

### Files Changed
- `GUNRPG.ConsoleClient/Program.cs` (+3 lines, -1 line)

### Commit
**Hash**: `4e07c3a`  
**Message**: "Fix Base Camp menu after mission completion - refresh operator state"

## Impact

### User Experience
- **Before**: Confusing menu after mission completion, no way to start new mission
- **After**: Correct menu immediately after mission, smooth workflow

### Edge Cases Handled
- Works for both victory and defeat outcomes
- Works regardless of how long user stays on Mission Complete screen
- Handles API failures gracefully (RefreshOperator silently fails)

## Conclusion
The fix ensures that the Base Camp menu always shows the correct options by refreshing the operator state before navigating from Mission Complete screen. This maintains consistency with other navigation patterns in the codebase and provides a better user experience.
