# GUNRPG Console TUI - Features

This document describes the features available in the GUNRPG console TUI.

## Overview

The following features are available in the console client to support the full infil/exfil flow:

1. **Change Loadout** - Switch weapons at base
2. **Treat Wounds** - Heal operator at base
3. **Abort Mission** - Explicitly abort mission while on infil

All features respect the Base/Infil state machine:
- Base mode: Full access to loadout and healing
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

## Feature 3: Abort Mission

**When:** Available in Infil mode only  
**Purpose:** Initiate exfil and return to base  

The abort flow uses `POST /operators/{id}/infil/complete` to exfil, abandoning any in-progress combat session. If a completed combat session already exists, it processes the outcome via `POST /operators/{id}/infil/outcome` instead (preserving XP and streak earned in that combat).

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

**API Endpoints:**
- `POST /operators/{id}/infil/complete` — Exfil when there is no completed combat session (abandons any in-progress session)
- `POST /operators/{id}/infil/outcome` — Submit outcome when a completed combat session exists

## Updated Base Camp Menu

### Base Mode Menu:
```
┌─ BASE CAMP ──────────────────────────────┐
│  ► START MISSION                          │
│    CHANGE LOADOUT                          │
│    TREAT WOUNDS                            │
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
| View Stats | ✓ | ✓ |

**API Validation:** Server rejects invalid operations with appropriate error messages:
- "Cannot change loadout while in Infil mode"
- "Cannot treat wounds while in Infil mode"

## Implementation Details

### Screens

- `BuildChangeLoadout()` — Weapon selection screen
- `BuildTreatWounds()` — Healing selection screen
- `BuildAbortMission()` — Mission abort confirmation screen

### API Endpoints

- `POST /operators/{id}/loadout` — Change loadout
- `POST /operators/{id}/wounds/treat` — Treat wounds
- `POST /operators/{id}/infil/outcome` — Abort mission (triggers exfil failure)
