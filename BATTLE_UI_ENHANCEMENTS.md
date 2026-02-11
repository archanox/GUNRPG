# Battle System UI Enhancements

## Overview
This document describes the Pokemon Red-style battle system enhancements added to the GUNRPG console client.

## Features Implemented

### 1. Pokemon Red-Style Battle Log 📋
The combat screen now features a dedicated "BATTLE LOG" panel that displays real-time combat events in a Pokemon-style format:

```
┌─── 📋 BATTLE LOG ────────────────────────────┐
│  Player fired a shot!                        │
│  Enemy took 15 damage (Torso)!               │
│  Enemy fired a shot!                         │
│  Player missed!                              │
│  Player started walking.                     │
│  Enemy reloaded.                             │
│                                              │
└──────────────────────────────────────────────┘
```

**Features:**
- Shows last 6 combat events
- Actor names prefixed to messages
- Event types: ShotFired, Damage, Miss, Reload, ADS, Movement, Cover, Suppression
- Updates automatically after each turn advance

### 2. Enhanced Progress Bars

**HP Bars:** Visual health indicators for both player and enemy
```
HP: [████████████████    ] 80/100
```

**Stamina Bar:** Shows player stamina for tactical decisions
```
STA: [███████████     ] 75/100
```

### 3. Cover State Visualization 🛡️

ASCII art representation of cover status:
- `COVER: [   EXPOSED   ]` - No cover, vulnerable
- `COVER: [ ▄ PARTIAL ▄ ]` - Partial cover, some protection
- `COVER: [███  FULL  ███]` - Full cover, maximum protection

### 4. Combat Stats Display

**Player Panel:**
```
┌─── 🎮 PLAYER ─────────────┐
│  PlayerName                │
│                            │
│  HP: [████████████] 100/100│
│  STA: [███████████] 75/100 │
│  AMMO: 28/30 [ADS]         │
│                            │
│  COVER: [ ▄ PARTIAL ▄ ]    │
│  MOVE: Walking             │
└────────────────────────────┘
```

**Enemy Panel:**
```
┌─── 💀 ENEMY ──────────────┐
│  Enemy (LVL 2)             │
│                            │
│  HP: [███████████] 85/100  │
│  AMMO: 25/30               │
│  DIST: 15.0m               │
│  COVER: Partial            │
└────────────────────────────┘
```

### 5. Weapon Stance Indicator

Shows current aiming mode:
- `[ADS]` - Aim Down Sights (better accuracy)
- `[HIP]` - Hip Fire (faster but less accurate)

## Technical Implementation

### Backend Changes

1. **BattleLogFormatter** (`GUNRPG.Application/Combat/BattleLogFormatter.cs`)
   - Converts combat system events into human-readable messages
   - Handles event types: ShotFired, Damage, Miss, Reload, ADS, Movement, Cover
   - Limits to 20 most recent events

2. **BattleLogEntryDto** (`GUNRPG.Application/Dtos/BattleLogEntryDto.cs`)
   - DTO for individual battle log entries
   - Contains: EventType, TimeMs, Message, ActorName

3. **CombatSessionDto Enhancement**
   - Added `List<BattleLogEntryDto> BattleLog` property
   - Automatically populated from combat system ExecutedEvents

### Frontend Changes

1. **ConsoleClient DTO Classes**
   - Added `CombatSessionDto`, `PlayerStateDto`, `PetStateDto`, `BattleLogEntryDto`
   - Enables proper JSON deserialization of API responses

2. **UI Helper Methods** (`UI` class)
   - `CreateBattleLogDisplay()` - Pokemon-style log rendering
   - `CreateCoverVisual()` - ASCII art cover visualization
   - `CreateProgressBar()` - Enhanced with proper clamping

3. **Enhanced Combat Screen**
   - Emoji headers (⚔, 🎮, 💀, 📋) for visual appeal
   - Two-column layout for player vs enemy
   - Dedicated battle log section
   - Additional stats (stamina, cover visual, ADS indicator)

## Event Types Logged

| Event Type | Description | Example Message |
|------------|-------------|-----------------|
| ShotFired | Operator fires weapon | "Player fired a shot!" |
| Damage | Hit lands on target | "Enemy took 15 damage (Torso)!" |
| Miss | Shot misses target | "Player missed!" |
| Reload | Weapon reloaded | "Enemy reloaded." |
| ADS | Aim down sights complete | "Player aimed down sights." |
| Movement | Movement started/stopped | "Enemy started walking." |
| Cover | Enter/exit cover | "Player took partial cover." |
| Suppression | Suppressing fire | "Enemy is suppressing!" |

## Visual Comparison

### Before:
- Basic HP bars only
- No battle log
- Simple text stats
- No cover visualization

### After:
- HP + Stamina bars
- Pokemon-style scrolling battle log (6 most recent events)
- ASCII art cover visualization
- ADS/HIP stance indicator
- Emoji icons for sections
- Enhanced layout with more tactical information

## Usage

1. Start a mission from Base Camp
2. Navigate to Combat Session screen
3. Press "ADVANCE TURN" to execute combat
4. Watch battle log update with events
5. Monitor HP, stamina, cover, and ammo status
6. Use battle log to understand combat flow

## Future Enhancements

Potential additions:
- Color-coded messages (damage in red, heals in green)
- Sound effects for major events
- Animation effects (screen shake on damage)
- Expanded log with scrolling view
- Combat statistics summary
- Replay feature to review previous turns
