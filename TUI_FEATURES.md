# GUNRPG Console TUI - New Features Documentation

This document showcases the new features added to the GUNRPG console TUI.

## Overview

The following features have been added to complete the infil/exfil flow:

1. **Change Loadout** - Switch weapons at base
2. **Treat Wounds** - Heal operator at base
3. **Unlock Perk** - Unlock new perks at base
4. **Abort Mission** - Explicitly abort mission while on infil

All features respect the Base/Infil state machine:
- Base mode: Full access to loadout, healing, and perk management
- Infil mode: Only mission continuation or abort options

## Feature 1: Change Loadout

**When:** Available in Base mode only  
**Purpose:** Switch between available weapons  
**Weapons Available:**
- SOKOL 545 (LMG)
- STURMWOLF 45 (SMG)
- M15 MOD 0 (Assault Rifle)

### UI Flow:
```
┌─ CHANGE LOADOUT ─────────────────────────┐
│                                           │
└───────────────────────────────────────────┘

┌─ AVAILABLE WEAPONS ──────────────────────┐
│  Select a weapon to equip:                │
│                                            │
│  Current: SOKOL 545                        │
│                                            │
│  ► SOKOL 545                               │
│    STURMWOLF 45                            │
│    M15 MOD 0                               │
│    --- CANCEL ---                          │
└────────────────────────────────────────────┘

  Choose a weapon
```

**API Endpoint:** `POST /operators/{id}/loadout`  
**Request Body:** `{ "weaponName": "SOKOL 545" }`  
**Validation:** Only allowed in Base mode

## Feature 2: Treat Wounds

**When:** Available in Base mode only  
**Purpose:** Heal operator health  
**Options:** 25 HP, 50 HP, 100 HP, or Full Heal

### UI Flow:
```
┌─ TREAT WOUNDS ───────────────────────────┐
│                                           │
└───────────────────────────────────────────┘

┌─ MEDICAL ────────────────────────────────┐
│  Current Health: 60/100                   │
│                                            │
│  Select healing amount:                    │
│                                            │
│  ► HEAL 25 HP                              │
│    HEAL 50 HP                              │
│    HEAL ALL (40 HP)                        │
│    --- CANCEL ---                          │
└────────────────────────────────────────────┘

  Choose healing amount
```

**API Endpoint:** `POST /operators/{id}/wounds/treat`  
**Request Body:** `{ "healthAmount": 25 }`  
**Validation:** Only allowed in Base mode, health amount must be positive

### Smart Options:
- Only shows heal amounts that fit within remaining health
- If at full health, shows "ALREADY AT FULL HEALTH" message
- "HEAL ALL" option dynamically shows exact amount needed

## Feature 3: Unlock Perk

**When:** Available in Base mode only  
**Purpose:** Unlock new operator perks  
**Perks Available:**
- Iron Lungs
- Quick Draw
- Toughness
- Fast Reload
- Steady Aim

### UI Flow:
```
┌─ UNLOCK PERK ────────────────────────────┐
│                                           │
└───────────────────────────────────────────┘

┌─ AVAILABLE PERKS ────────────────────────┐
│  Select a perk to unlock:                 │
│                                            │
│  Unlocked: Iron Lungs, Fast Reload        │
│                                            │
│  ► Iron Lungs                              │
│    Quick Draw                              │
│    Toughness                               │
│    Fast Reload                             │
│    Steady Aim                              │
│    --- CANCEL ---                          │
└────────────────────────────────────────────┘

  Choose a perk
```

**API Endpoint:** `POST /operators/{id}/perks`  
**Request Body:** `{ "perkName": "Iron Lungs" }`  
**Validation:** Only allowed in Base mode, perk must not already be unlocked

## Feature 4: Abort Mission

**When:** Available in Infil mode only  
**Purpose:** Explicitly abort mission and return to base  
**Consequences:**
- Mission is marked as failed
- Exfil streak is reset to 0
- No XP is awarded
- Operator returns to Base mode

### UI Flow:
```
┌─ ABORT MISSION ──────────────────────────┐
│                                           │
└───────────────────────────────────────────┘

┌─ WARNING ────────────────────────────────┐
│  Are you sure you want to abort the      │
│  mission?                                 │
│                                            │
│  - Mission will be failed                 │
│  - Exfil streak will be reset             │
│  - No XP will be awarded                  │
│                                            │
│  Select action:                            │
│                                            │
│  ► CONFIRM ABORT                           │
│    CANCEL                                  │
└────────────────────────────────────────────┘

  Confirm mission abort
```

**API Endpoint:** `POST /operators/{id}/infil/outcome`  
**Request Body:** `{ "sessionId": "{current-session-id}" }`  
**Note:** Posts current session ID to trigger exfil failure, returning operator to Base mode

## Updated Base Camp Menu

### Base Mode Menu:
```
┌─ BASE CAMP ──────────────────────────────┐
│  ► START MISSION                          │
│    CHANGE LOADOUT                          │
│    TREAT WOUNDS                            │
│    UNLOCK PERK                             │
│    VIEW STATS                              │
│    MAIN MENU                               │
└────────────────────────────────────────────┘
```

### Infil Mode Menu:
```
┌─ BASE CAMP ──────────────────────────────┐
│  ► CONTINUE MISSION                        │
│    ABORT MISSION                           │
│    VIEW STATS                              │
│    MAIN MENU                               │
└────────────────────────────────────────────┘
```

## Complete Infil/Exfil Flow

The TUI now supports the complete operator lifecycle:

### 1. Pre-Mission (Base Mode)
```
Base Camp
  ↓
Change Loadout (optional)
  ↓
Treat Wounds (optional)
  ↓
Unlock Perk (optional)
  ↓
START MISSION
```

### 2. Mission (Infil Mode)
```
Mission Briefing
  ↓
BEGIN INFILTRATION
  ↓
Combat Session
  ├─→ ADVANCE TURN (repeat)
  ├─→ VIEW DETAILS
  └─→ Combat ends (player or enemy dies)
```

### 3. Post-Mission (Exfil)
```
Mission Complete
  ↓
Process Combat Outcome (automatic)
  ↓
Return to Base Camp (Base Mode)
```

### Alternative: Mission Abort
```
Combat Session (Infil Mode)
  ↓
RETURN TO BASE
  ↓
ABORT MISSION
  ↓
Confirm Abort
  ↓
Return to Base Camp (Base Mode)
  ├─→ Streak reset to 0
  └─→ No XP awarded
```

## State Machine Enforcement

All new features properly enforce the operator state machine:

| Feature | Base Mode | Infil Mode |
|---------|-----------|------------|
| Start Mission | ✓ | ✗ |
| Continue Mission | ✗ | ✓ |
| Abort Mission | ✗ | ✓ |
| Change Loadout | ✓ | ✗ |
| Treat Wounds | ✓ | ✗ |
| Unlock Perk | ✓ | ✗ |
| View Stats | ✓ | ✓ |

**API Validation:** Server rejects invalid operations with appropriate error messages:
- "Cannot change loadout while in Infil mode"
- "Cannot treat wounds while in Infil mode"
- "Cannot unlock perk while in Infil mode"

## Testing

All features have been tested:
- ✓ Build succeeds with 0 errors
- ✓ All 167 tests pass
- ✓ CodeQL security scan: 0 vulnerabilities
- ✓ Manual API endpoint testing
- ✓ Code review completed and feedback addressed

## Implementation Details

**Files Modified:**
- `GUNRPG.ConsoleClient/Program.cs` - Added 4 new screens and helper methods

**New Screens Added:**
1. `BuildChangeLoadout()` - Weapon selection screen
2. `BuildTreatWounds()` - Healing selection screen
3. `BuildUnlockPerk()` - Perk selection screen
4. `BuildAbortMission()` - Mission abort confirmation screen

**Helper Methods Added:**
1. `ChangeLoadout(string weaponName)` - API call to change loadout
2. `TreatWounds(float healthAmount)` - API call to heal operator
3. `UnlockPerk(string perkName)` - API call to unlock perk
4. `AbortMission()` - API call to abort mission and trigger exfil failure

**Code Quality:**
- Proper error handling with user-friendly messages
- Consistent UI patterns with existing screens
- Smart option filtering (e.g., heal amounts based on remaining health)
- Clear confirmation dialogs for destructive actions
- Explanatory comments for non-obvious API usage

## Conclusion

The GUNRPG console TUI now has complete support for:
- ✓ Full infil/exfil flow
- ✓ Mission sessions with combat
- ✓ All Base mode operator management features
- ✓ Mission abort capability
- ✓ State machine enforcement at UI level

All requirements from the problem statement are met!
