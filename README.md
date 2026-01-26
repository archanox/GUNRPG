# GUNRPG - Text-Based Tactical Combat Simulator

A deterministic, time-based combat simulation system inspired by Call of Duty weapon mechanics, implementing a discrete event simulation model with real-time physics and tactical decision-making.

## Overview

GUNRPG is a **text-based tactical combat simulator** that features:

* **Real-time simulation** with millisecond precision
* **Raw weapon stats** used 1:1 (fire rate, recoil, damage falloff, etc.)
* **Discrete event simulation** with priority queue event execution
* **Turn-based player interaction** with continuous-time simulation
* **Commitment-based reaction windows** (every N bullets fired / meters moved)
* **Deterministic gameplay** with optional seed-based randomization
* **Virtual pet mechanics** for operator management (planned)

## Architecture

### Core Systems

The project is built on a modular architecture with clear separation of concerns:

```
GUNRPG.Core/
├── Time/              # Global simulation clock
├── Events/            # Event queue and event types
├── Operators/         # Operator state and behavior
├── Weapons/           # Weapon configurations and stats
├── Intents/           # Player/AI action declarations
├── Combat/            # Combat orchestration
└── AI/                # AI decision making
```

### Key Concepts

#### Time Model

- All time measured in **milliseconds** (`long`)
- Monotonic global clock (`SimulationTime`)
- Events execute at exact timestamps

#### Event System

- Priority queue sorted by: timestamp → operator ID → sequence number
- Events are **atomic** and **irreversible**
- Deterministic ordering ensures reproducibility

#### Actor/Operator Model

Each operator maintains:
- **Movement State**: Idle, Walking, Sprinting, Sliding
- **Aim State**: Hip, ADS, Transitioning
- **Weapon State**: Ready, Reloading, Jammed
- **Physical State**: Health, Stamina, Fatigue

#### Intent System

Intents are **declarative action plans** that schedule future events:
- `FireWeapon` - Fire equipped weapon
- `Reload` - Reload weapon magazine
- `EnterADS` / `ExitADS` - Aim down sights
- `Walk/Sprint/Slide` - Movement intents (toward/away)
- `Stop` - Cease all actions

Intents are validated before execution and can be cancelled during planning phases.

#### Commitment Units & Reaction Windows

Combat flows through **Planning** and **Execution** phases:

1. **Planning Phase**: Both sides submit intents (time paused)
2. **Execution Phase**: Events execute in order (time runs)
3. **Reaction Window**: Triggered after commitment units complete
   - Weapons: Every N bullets fired (default: 3)
   - Movement: Every N meters moved (default: 2)

This creates tactical decision points without traditional "turns".

#### Hit Resolution

- Weapon spread depends on aim state (Hip vs ADS)
- Recoil accumulates and recovers over time
- Distance affects both accuracy and damage
- Damage falloff based on weapon-specific ranges
- Headshot multipliers (10% chance)

## Weapon System

Weapons use **real stats** from Call of Duty and other tactical shooters:

- Fire rate (RPM) → exact time between shots
- Magazine size and reload time
- Base damage and headshot multipliers
- Distance-based damage falloff curves
- Hipfire and ADS spread angles
- Vertical and horizontal recoil
- ADS transition time
- Sprint-to-fire delays

### Included Weapons

- **M4A1**: Balanced assault rifle (833 RPM, 28 damage)
- **AK-47**: High damage, slower fire (600 RPM, 35 damage)
- **MP5**: High ROF SMG (857 RPM, 22 damage)

## Health & Regeneration

Implements Call of Duty-style health regeneration:
- Health regenerates after delay following damage
- Configurable regen delay and rate
- Stamina regenerates when not sprinting
- Stamina drains during sprint/slide

## AI System

Simple but effective AI decision-making:
- Prioritizes survival when low health
- Reloads tactically (when safe or out of ammo)
- Engages at optimal range
- Makes use of reaction windows

## Running the Demo

### Build

```bash
dotnet build
```

### Run

```bash
cd GUNRPG.Core
dotnet run
```

### Test

```bash
dotnet test
```

All tests should pass (38 tests covering core systems).

## Usage Example

```csharp
// Create operators
var player = new Operator("Player")
{
    EquippedWeapon = WeaponFactory.CreateM4A1(),
    CurrentAmmo = 30,
    DistanceToOpponent = 15f
};

var enemy = new Operator("Enemy")
{
    EquippedWeapon = WeaponFactory.CreateAK47(),
    CurrentAmmo = 30,
    DistanceToOpponent = 15f
};

// Initialize combat
var combat = new CombatSystem(player, enemy, seed: 42);

// Submit intents (Planning Phase)
combat.SubmitIntent(player, new FireWeaponIntent(player.Id));
combat.SubmitIntent(enemy, new FireWeaponIntent(enemy.Id));

// Execute (Execution Phase)
combat.BeginExecution();
bool hasReactionWindow = combat.ExecuteUntilReactionWindow();

// Repeat until combat ends
```

## Design Philosophy

### Core Principles

1. **No derived combat stats** - Use raw weapon data directly
2. **Time is absolute** - Milliseconds are the single source of truth
3. **Deterministic simulation** - Same seed = same outcome
4. **Emergent gameplay** - Complex behavior from simple rules
5. **Player agency** - React to observable events, not hidden timers

### Non-Goals (v1)

Explicitly out of scope:
- Multiplayer networking
- 3D graphics or animation
- Complex ballistics simulation
- Persistent injuries or equipment degradation
- Cover system (planned for later)

## Project Status

**Current Status**: ✅ Foundation Complete (Draft 1)

### Implemented

- [x] Core time model
- [x] Event queue system
- [x] Operator state machines
- [x] Weapon system with real stats
- [x] Intent system
- [x] Combat orchestration
- [x] Commitment units & reaction windows
- [x] Hit resolution with spread/recoil
- [x] Movement system (distance-based)
- [x] Health/stamina regeneration
- [x] Basic AI
- [x] Unit tests for all core systems (38 tests)
- [x] Interactive demo
- [x] Virtual pet/rest system
- [x] Fatigue mechanics

### Planned

- [ ] Cover system
- [ ] Multiple opponents
- [ ] Campaign mode
- [ ] Enhanced TUI with better visualization
- [ ] More weapons
- [ ] Advanced AI behaviors

## Technical Details

- **Language**: C# (.NET 8.0)
- **Testing**: xUnit
- **Platform**: Cross-platform (Windows, Linux, macOS)

## License

See LICENSE file for details.

## Credits

Design inspired by:
- Call of Duty weapon mechanics
- X-COM tactical gameplay
- Virtual pet management games
- Discrete event simulation theory
