# GUNRPG - Text-Based Tactical Combat Simulator

A deterministic, time-based combat simulation system inspired by Call of Duty weapon mechanics, implementing a discrete event simulation model with real-time physics and tactical decision-making.

## Overview

GUNRPG is a **text-based tactical combat simulator** that features:

* **Real-time simulation** with millisecond precision
* **Raw weapon stats** used 1:1 (fire rate, recoil, damage falloff, etc.)
* **Discrete event simulation** with priority queue event execution
* **Turn-based player interaction** with continuous-time simulation
* **Commitment-based reaction windows** (every N bullets fired / meters moved)
* **Deterministic gameplay** with seed-based randomization and replay
* **Virtual pet mechanics** for operator readiness management
* **Event-sourced operator progression** with tamper-evident hash chaining
* **Self-hosted WebAuthn + JWT authentication**

## Architecture

### Project Structure

```
GUNRPG.Core/           # Domain: combat engine, operators, weapons, simulation
GUNRPG.Application/    # Business logic: session service, DTOs, operator exfil
GUNRPG.Infrastructure/ # Persistence: LiteDB stores, identity, ledger, security
GUNRPG.Api/            # REST API: controllers, WebAuthn, JWT, device code auth
GUNRPG.ConsoleClient/  # Pokemon-style TUI using hex1b
GUNRPG.WebClient/      # Blazor WASM web client (GitHub Pages compatible)
GUNRPG.ClientModels/   # Shared DTOs between clients
GUNRPG.Tests/          # Unit and integration tests
```

### Core Domain (`GUNRPG.Core`)

```
GUNRPG.Core/
├── Time/              # Global simulation clock (millisecond precision)
├── Events/            # Event queue and combat event types
├── Operators/         # Operator aggregate, event sourcing, state machines
├── Weapons/           # Weapon configurations and stats
├── Intents/           # Player/AI action declarations
├── Combat/            # Combat orchestration, hit resolution, cover, suppression
├── Simulation/        # Deterministic replay engine
├── AI/                # AI decision making
└── VirtualPet/        # Rest, fatigue, and readiness system
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
- **Movement State**: Idle, Walking, Sprinting, Sliding, Crouching
- **Aim State**: Hip, ADS, Transitioning
- **Weapon State**: Ready, Reloading
- **Physical State**: Health, Stamina, Fatigue
- **Cover State**: None, Partial, Full
- **Suppression State**: tracks suppression exposure

#### Intent System

Intents are **declarative action plans** that schedule future events:
- `FireWeapon` - Fire equipped weapon
- `Reload` - Reload weapon magazine
- `EnterADS` / `ExitADS` - Aim down sights
- `Walk/Sprint/Slide/Crouch` - Movement intents (toward/away)
- `EnterCover/ExitCover` - Take or leave cover
- `Stop` - Cease all actions

Intents are validated before execution and can be cancelled during planning phases.

#### Commitment Units & Reaction Windows

Combat flows through **Planning** and **Execution** phases:

1. **Planning Phase**: Both sides submit intents (time paused)
2. **Execution Phase**: Events execute in order (time runs)
3. **Reaction Window**: Triggered after commitment units complete
   - Weapons: Every N bullets fired (configurable per weapon)
   - Movement: Every N meters moved

This creates tactical decision points without traditional "turns".

#### Hit Resolution

- Weapon spread depends on aim state (Hip vs ADS) and movement state
- Recoil accumulates and recovers over time
- Distance affects both accuracy and damage
- Per-body-part damage with weapon-specific overrides
- Suppression degrades enemy accuracy

#### Cover & Suppression

- Operators can take partial or full cover
- Cover reduces incoming damage
- Suppressive fire degrades opponent's combat effectiveness
- Cover transitions take time and can be interrupted

## Weapon System

Weapons use **real stats** from tactical shooters:

- Fire rate (RPM) → exact time between shots
- Magazine size and reload time
- Per-body-part damage at multiple range brackets
- Hipfire/ADS/slide/dive/jump spread angles
- Vertical and horizontal recoil with recovery
- ADS transition time and sprint-to-fire delay
- Suppression factor

### Included Weapons

| Weapon | Type | RPM | Magazine | Base Damage |
|---|---|---|---|---|
| **SOKOL 545** | Light machine gun | 583 | 102 | 32 |
| **STURMWOLF 45** | Submachine gun | 667 | 32 | 30 |
| **M15 MOD 0** | Assault rifle | 769 | 30 | 21 |

## Operator System

Operators are **event-sourced aggregates** that persist through an append-only event log with SHA-256 hash chaining for tamper detection.

- **Infil/Exfil boundary**: combat uses a snapshot of operator stats; only the exfil path can commit progression events
- **XP and perks**: gained on successful exfil
- **Virtual pet / fatigue**: readiness mechanics between missions
- **Complete audit trail**: all state changes are recorded and verifiable

See [INFIL_EXFIL_BOUNDARY.md](INFIL_EXFIL_BOUNDARY.md) for architectural details.

## Authentication

GUNRPG uses self-hosted **WebAuthn (FIDO2) + Ed25519 JWT** authentication with no dependency on a centralized identity provider.

- **Browser clients**: register and login with a passkey (YubiKey, Face ID, etc.)
- **Console clients**: device code flow (RFC 8628) — visit a URL, enter a short code, tokens arrive automatically

See [docs/IDENTITY.md](docs/IDENTITY.md) for configuration and API reference.

## Health & Regeneration

Implements Call of Duty-style health regeneration:
- Health regenerates after a delay following damage
- Configurable regen delay and rate
- Stamina regenerates when not sprinting
- Stamina drains during sprint/slide

## AI System

AI decision-making:
- Prioritizes survival when low health
- Reloads tactically (when safe or out of ammo)
- Engages at optimal range
- Uses cover and suppressive fire
- Makes use of reaction windows

## Running the Game

### Build

```bash
dotnet build
```

### Run

```bash
# Start the headless Web API (http://localhost:5209 by default)
dotnet run --project GUNRPG.Api

# In another terminal, launch the Pokemon-style console client
GUNRPG_API_BASE=http://localhost:5209 dotnet run --project GUNRPG.ConsoleClient
```

The console client defaults to `http://localhost:5209`. See [GUNRPG.ConsoleClient/README.md](GUNRPG.ConsoleClient/README.md) for full usage details.

### Test

```bash
dotnet test
```

### Web Client

The Blazor WASM web client can be built and served statically (GitHub Pages compatible):

```bash
dotnet publish GUNRPG.WebClient -c Release
```

## Headless Architecture

- **Domain (`GUNRPG.Core`)**: Combat engine, operators, weapons, intents, virtual pet rules — no UI or HTTP dependencies.
- **Application (`GUNRPG.Application`)**: `CombatSessionService`, `OperatorExfilService`, DTOs, session store interface, offline sync, distributed authority.
- **Infrastructure (`GUNRPG.Infrastructure`)**: LiteDB persistence, identity (WebAuthn, JWT, device code), ledger, gossip/P2P transport, session authority signing.
- **Web API (`GUNRPG.Api`)**: Stateless controllers. Key endpoint groups:
  - **Sessions** (`/sessions`): create, get state, submit intents, advance, pet actions.
  - **Operators** (`/operators`): create, get state, start/complete infil, process outcome, loadout, wounds, XP, perks.
  - **Auth** (`/auth`): WebAuthn register/login, token refresh, device code flow, public key.
  - **Weapons** (`/weapons`): list available weapons.
- **Console client (`GUNRPG.ConsoleClient`)**: Pokemon Red-style TUI using [hex1b](https://hex1b.dev). Full operator lifecycle including combat, loadout management, healing, perks, and mission abort.
- **Web client (`GUNRPG.WebClient`)**: Blazor WASM SPA for browser-based play, including offline mode and SSE-based live updates.

## Design Philosophy

### Core Principles

1. **No derived combat stats** — Use raw weapon data directly
2. **Time is absolute** — Milliseconds are the single source of truth
3. **Deterministic simulation** — Same seed = same outcome, full replay support
4. **Emergent gameplay** — Complex behavior from simple rules
5. **Player agency** — React to observable events, not hidden timers
6. **Event sourcing** — Operator state is derived from an append-only event log

### Non-Goals

- Multiplayer networking (lockstep P2P foundation is in place but not a gameplay feature yet)
- 3D graphics or animation
- Complex ballistics simulation (no wind, no penetration)

## Project Status

### Implemented

- [x] Core time model
- [x] Event queue system
- [x] Operator state machines with event sourcing
- [x] Weapon system with real per-body-part damage at multiple ranges
- [x] Intent system
- [x] Combat orchestration
- [x] Commitment units & reaction windows
- [x] Hit resolution with spread/recoil
- [x] Movement system (distance-based)
- [x] Health/stamina regeneration
- [x] Cover system (partial/full)
- [x] Suppression model
- [x] Basic AI
- [x] Virtual pet/rest system and fatigue mechanics
- [x] Infil/Exfil boundary with hash-chained event store
- [x] Deterministic replay with hash verification
- [x] Self-hosted WebAuthn + Ed25519 JWT authentication
- [x] Device code flow for console clients (RFC 8628)
- [x] Pokemon-style console TUI (hex1b)
- [x] Blazor WASM web client
- [x] Offline play mode
- [x] Comprehensive test suite (1000+ tests)

### Planned

- [ ] Multiple simultaneous opponents
- [ ] Campaign mode with persistent mission progression
- [ ] More weapons
- [ ] Advanced AI behaviors (flanking, grenade use)

## Technical Details

- **Language**: C# (.NET 10.0)
- **Testing**: xUnit
- **Platform**: Cross-platform (Windows, Linux, macOS)
- **Persistence**: LiteDB (embedded, no external DB required)
- **TUI**: hex1b
- **Web client**: Blazor WebAssembly

## License

See LICENSE file for details.

## Credits

Design inspired by:
- Call of Duty weapon mechanics
- X-COM tactical gameplay
- Virtual pet management games
- Discrete event simulation theory
