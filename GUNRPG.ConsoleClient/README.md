# GUNRPG Console UI - Pokemon Style

## Overview
The GUNRPG console client has been redesigned with a retro Pokemon-style interface using the [hex1b](https://hex1b.dev) terminal UI library. The interface is optimized for an 80x43 character viewport.

## Features

### Pokemon-Style Aesthetic
- Clean box-drawing borders using hex1b's BorderWidget
- Button-based navigation with cursor indicators (►)
- Health bars using block characters (███████░░░)
- Status displays for operator information
- Retro color scheme inspired by Pokemon Red/Crystal

### Screens

#### 1. Main Menu
- Create new operator
- Continue with existing operator
- Exit application

#### 2. Create Operator
- Generate random operator names (Operative-XXXX format)
- Create new operator profile
- Returns to main menu

#### 3. Base Camp
- Displays operator status:
  - Name and XP
  - Health bar
  - Equipped weapon
  - Unlocked perks
  - Exfil streak
  - Current mode (Base/Infil)
- Action menu (state-aware):
  - **Base Mode Actions:**
    - Change Loadout
    - Treat Wounds
    - Apply XP
    - Unlock Perk
    - Start Mission
  - **Infil Mode Actions:**
    - Continue Mission (goes to combat session)
    - Disabled actions with explanations

#### 4. Mission Briefing
- Shows infiltration warnings
- Confirms mission start
- Starts infil mode and creates combat session

#### 5. Combat Session
- Shows player and enemy stats side-by-side:
  - Health bars
  - Ammo count
  - Distance
  - Cover status
  - Movement state
- Turn-by-turn combat advancement
- **Automatic outcome processing when combat ends**
- Returns to base camp when complete

#### 6. Mission Complete
- Shows combat outcome
- Displays debriefing message
- **Operator automatically returned to Base mode with XP applied**

#### 7. Message Dialog
- Generic popup for information and errors
- Returns to previous screen

## State Machine Enforcement

The UI respects the operator state machine:
- **Base Mode**: Full access to loadout, wounds, XP, perks, and mission start
- **Infil Mode**: Only mission continuation available; other actions disabled
- **Combat Completion**: Automatically processes outcome via server-side validation
- Invalid actions are clearly marked as unavailable
- API calls only happen when actions are valid

## Technical Details

### Architecture
- Uses hex1b declarative widget API with BorderWidget for proper borders
- State management via `GameState` class
- Screen-based navigation system
- API integration via HttpClient

### Combat Outcome Flow
1. Combat ends (player or enemy eliminated)
2. Client calls `POST /operators/{id}/infil/outcome` with session ID
3. Server loads combat session and computes deterministic outcome
4. Server applies XP, updates mode, and saves operator state
5. Client refreshes operator state and returns to Base mode

This ensures outcomes are **server-authoritative** and cannot be manipulated by clients.

### Dependencies
- hex1b 0.75.0
- .NET 10.0
- GUNRPG Application layer DTOs

### API Endpoints Used
- `POST /operators` - Create operator
- `GET /operators/{id}` - Get operator state
- `POST /operators/{id}/infil/start` - Start mission
- `POST /operators/{id}/infil/outcome` - Process combat outcome (server-side validation)
- `GET /sessions/{id}/state` - Get combat state
- `POST /sessions/{id}/advance` - Progress combat

## Running the Console Client

```bash
# Start the API server (in one terminal)
cd GUNRPG.Api
dotnet run

# Start the console client (in another terminal)
cd GUNRPG.ConsoleClient
dotnet run
```

The client defaults to connecting to `http://localhost:5209`. You can override this with the `GUNRPG_API_BASE` environment variable.

## Controls

- **Arrow Keys / Tab**: Navigate between buttons
- **Enter / Space**: Select button
- **Ctrl+C**: Exit application

## Current Implementation

### Working Features
✅ Operator creation with random name generation
✅ Base camp with state-aware menus
✅ Mission start with infil mode transition
✅ Turn-based combat with stat display
✅ **Automatic combat outcome processing**
✅ **Server-side outcome validation (no client manipulation)**
✅ **Operator mode transitions (Infil → Base after combat)**
✅ Proper hex1b BorderWidget usage

### Known Limitations
- Text input widget not implemented (hex1b TextBoxWidget needs focus management in reactive UI)
- Intent submission UI not implemented (complex multi-choice system)
- Operator list/selection screen not yet added

## Future Enhancements

- Implement focus-aware text input for operator names
- Add intent submission UI for combat
- Add operator list/selection screen
- Add color theming (Game Boy Color palette)
- Add animation for health bars
- Add more detailed status displays
