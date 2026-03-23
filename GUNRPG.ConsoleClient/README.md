# GUNRPG Console UI - Pokemon Style

## Overview
The GUNRPG console client has been redesigned with a retro Pokemon-style interface using the [hex1b](https://hex1b.dev) terminal UI library. The interface is optimized for an 80x43 character viewport and uses proper Hex1b widgets for authentic menu navigation.

## Features

### Pokemon-Style Aesthetic
- Clean box-drawing borders with proper titles using hex1b's BorderWidget
- List-based navigation with Up/Down arrow keys
- Automatic selection indicators (►) managed by Hex1b theme
- Native progress bars using hex1b's ProgressWidget
- Status displays for operator information
- Retro color scheme inspired by Pokemon Red/Crystal

### Navigation
- **Up/Down Arrows**: Navigate menu items
- **Enter/Space**: Select current item
- **Tab**: Move focus between widgets (if multiple)
- **Escape**: Return to previous screen
- All navigation is handled by Hex1b's ListWidget - no manual cursor management

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
    - Start Mission (infil)
    - Change Loadout (switch weapons)
    - Treat Wounds (restore health)
    - Unlock Perk
    - Apply XP
    - View Stats
  - **Infil Mode Actions:**
    - Continue Mission (goes to combat session)
    - Abort Mission (returns to base, resets streak)
    - View Stats

#### 4. Mission Briefing
- Shows infiltration warnings
- Confirms mission start
- Starts infil mode and creates combat session

#### 5. Combat Session
- Shows player and enemy stats side-by-side:
  - Health bars (HP + Stamina)
  - Ammo count and weapon stance ([ADS]/[HIP]/[TRANS])
  - Distance to opponent
  - Cover state visualization (Exposed / Partial / Full)
  - Movement state
- Pokemon-style scrolling battle log (last 6 events)
- Turn-by-turn combat advancement
- Intent submission (Primary, Movement, Stance, Cover)
- **Automatic outcome processing when combat ends**
- Returns to base camp when complete

#### 6. Change Loadout
- Available in Base mode only
- Select from SOKOL 545, STURMWOLF 45, or M15 MOD 0

#### 7. Treat Wounds
- Available in Base mode only
- Heal 25 HP, 50 HP, or full heal based on remaining health

#### 8. Unlock Perk
- Available in Base mode only
- Select from available perks (Iron Lungs, Quick Draw, Toughness, Fast Reload, Steady Aim)

#### 9. Abort Mission
- Available in Infil mode only
- Confirmation dialog; resets exfil streak, no XP awarded

#### 10. Mission Complete
- Shows combat outcome
- Displays debriefing message
- **Operator automatically returned to Base mode with XP applied**

#### 11. Message Dialog
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
- hex1b 0.79.0
- .NET 10.0
- GUNRPG Application layer DTOs

### UI Architecture
- **ListWidget**: Used for all menu navigation with OnItemActivated event handlers
- **BorderWidget**: Properly displays titles in border frames
- **ProgressWidget**: Native progress bars for health/status displays
- **VStackWidget/HStackWidget**: Layout containers for organizing content
- **TextBlockWidget**: Static text display
- **TextBoxWidget**: User input (operator name creation)

### Code Structure
All screens follow a consistent pattern:
1. Define menu items as string array
2. Create ListWidget with OnItemActivated handler
3. Use switch statement on ActivatedIndex for menu actions
4. Wrap content in BorderWidget with title
5. Use helper methods from UI class for common patterns

### API Endpoints Used
- `POST /operators` — Create operator
- `GET /operators/{id}` — Get operator state
- `GET /operators` — List operators for account
- `POST /operators/{id}/infil/start` — Start mission (enter Infil mode)
- `POST /operators/{id}/infil/complete` — Complete infil successfully (Exfil)
- `POST /operators/{id}/infil/outcome` — Process combat outcome (server-side validation)
- `POST /operators/{id}/loadout` — Change equipped weapon
- `POST /operators/{id}/wounds/treat` — Heal operator
- `POST /operators/{id}/xp` — Apply experience points
- `POST /operators/{id}/perks` — Unlock a perk
- `GET /sessions/{id}/state` — Get combat state
- `POST /sessions/{id}/intent` — Submit player intents
- `POST /sessions/{id}/advance` — Progress combat

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
✅ Proper hex1b BorderWidget usage with titles
✅ **ListWidget-based menu navigation (no manual ButtonWidgets)**
✅ **Theme-managed selection indicators**
✅ **Single primary focus widget per screen (CreateOperator uses TextBox + List with Tab focus switching)**
✅ Intent submission UI for combat (Primary, Movement, Stance, Cover)
✅ Change Loadout, Treat Wounds, Unlock Perk screens
✅ Abort Mission with confirmation dialog
✅ Pokemon-style battle log (last 6 events)
✅ Cover visualization and ADS/HIP/TRANS stance indicators

### Known Limitations
- Text input widget requires focus management (TextBoxWidget used in CreateOperator)
- Manual testing requires TTY (terminal with proper input handling)
- **Synchronous event handlers**: hex1b's ListWidget.OnItemActivated handlers are synchronous, causing UI to freeze during HTTP calls (known framework limitation)

## Future Enhancements

- Implement focus-aware text input for operator names
- Add operator list/selection screen
- Add color theming (Game Boy Color palette)
- Add animation for health bars
- Add more detailed status displays
