# GUNRPG Console UI - Pokemon Style

## Overview
The GUNRPG console client has been redesigned with a retro Pokemon-style interface using the [hex1b](https://hex1b.dev) terminal UI library. The interface is optimized for an 80x43 character viewport.

## Features

### Pokemon-Style Aesthetic
- Clean box-drawing borders using ASCII characters (┌─┐│└┘)
- Button-based navigation with cursor indicators (►)
- Health bars using block characters (█░)
- Status displays for operator information
- Retro color scheme inspired by Pokemon Red/Crystal

### Screens

#### 1. Main Menu
- Create new operator
- Continue with existing operator
- Exit application

#### 2. Create Operator
- Enter operator name
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
- Returns to base camp when complete

#### 6. Mission Complete
- Shows combat outcome
- XP gained
- Returns operator to Base mode

#### 7. Message Dialog
- Generic popup for information and errors
- Returns to previous screen

## State Machine Enforcement

The UI respects the operator state machine:
- **Base Mode**: Full access to loadout, wounds, XP, perks, and mission start
- **Infil Mode**: Only mission continuation available; other actions disabled
- Invalid actions are clearly marked as unavailable
- API calls only happen when actions are valid

## Technical Details

### Architecture
- Uses hex1b declarative widget API
- State management via `GameState` class
- Screen-based navigation system
- API integration via HttpClient

### Dependencies
- hex1b 0.75.0
- .NET 10.0
- GUNRPG Application layer DTOs

### API Endpoints Used
- `POST /operators` - Create operator
- `GET /operators/{id}` - Get operator state
- `POST /operators/{id}/infil/start` - Start mission
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

## Known Limitations

1. **Text Input**: The create operator screen uses a placeholder for text input. hex1b's TextBoxWidget needs further investigation for proper keyboard input handling.

2. **Intent Submission**: The combat session screen advances turns automatically but doesn't allow manual intent selection yet. This would require a complex multi-choice UI.

3. **Operator Persistence**: The "Continue" option is not yet implemented. Operators are created per-session only.

## Future Enhancements

- Implement proper text input widget
- Add intent submission UI for combat
- Add operator list/selection screen
- Add color theming (Game Boy Color palette)
- Add sound effects (if hex1b supports it)
- Add animation for health bars
- Add more detailed status displays
