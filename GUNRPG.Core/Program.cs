using GUNRPG.Core;
using GUNRPG.Core.AI;
using GUNRPG.Core.Combat;
using GUNRPG.Core.Intents;
using GUNRPG.Core.Operators;
using GUNRPG.Core.Rendering;
using GUNRPG.Core.VirtualPet;

// Shared random instance for enemy level generation
var enemyLevelRandom = new Random(42);

Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
Console.WriteLine("║          GUNRPG - Text-Based Tactical Combat Simulator       ║");
Console.WriteLine("║                     Foundation Demo v1.0                     ║");
Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
Console.WriteLine();

// Create operators
var player = new Operator("Player")
{
    EquippedWeapon = WeaponFactory.CreateSokol545(),
    DistanceToOpponent = 15f
};
player.CurrentAmmo = player.EquippedWeapon!.MagazineSize;

var enemy = new Operator("Enemy")
{
    EquippedWeapon = WeaponFactory.CreateSturmwolf45(),
    DistanceToOpponent = 15f
};
enemy.CurrentAmmo = enemy.EquippedWeapon!.MagazineSize;

// Initialize operator manager for rest/fatigue system
var operatorManager = new OperatorManager();

// Track operator experience and level
long playerXp = 0L;
int playerLevel = 0;

// Initialize pet state for virtual pet management
var playerPetState = new PetState(
    player.Id,
    Health: 100f,
    Fatigue: 0f,
    Injury: 0f,
    Stress: 0f,
    Morale: 100f,
    Hunger: 0f,  // 0 = not hungry, 100 = very hungry
    Hydration: 100f,  // 100 = well hydrated, 0 = dehydrated
    LastUpdated: DateTimeOffset.Now
);

// Main menu loop
bool exitRequested = false;
while (!exitRequested)
{
    // Apply background decay since last update
    playerPetState = PetRules.Apply(playerPetState, new RestInput(TimeSpan.Zero), DateTimeOffset.Now);
    
    Console.WriteLine("═══ MAIN MENU ═══");
    Console.WriteLine("1. View Operator Stats (Virtual Pet)");
    Console.WriteLine("2. Enter Battle");
    Console.WriteLine("3. Rest Operator");
    Console.WriteLine("4. Feed Operator");
    Console.WriteLine("5. Give Water");
    Console.WriteLine("6. Exit");
    Console.Write("Choose an option (1-6): ");
    
    var menuChoice = Console.ReadKey();
    Console.WriteLine();
    Console.WriteLine();
    
    switch (menuChoice.KeyChar)
    {
        case '1':
            DisplayOperatorStats(player, playerPetState, operatorManager, playerLevel, playerXp);
            Console.WriteLine("Press any key to return to main menu...");
            Console.ReadKey(true);
            Console.WriteLine();
            break;
            
        case '2':
            // Check if operator is resting
            if (operatorManager.IsResting(player))
            {
                Console.WriteLine("⚠️  Operator is currently resting.");
                Console.WriteLine($"Status: {operatorManager.GetStatus(player)}");
                Console.WriteLine();
                Console.Write("Wake operator from rest? (y/n): ");
                var wakeChoice = Console.ReadKey();
                Console.WriteLine();
                Console.WriteLine();
                
                if (wakeChoice.KeyChar == 'y' || wakeChoice.KeyChar == 'Y')
                {
                    operatorManager.WakeFromRest(player);
                    Console.WriteLine("✓ Operator awakened from rest.");
                    Console.WriteLine();
                }
                else
                {
                    Console.WriteLine("Operator continues resting.");
                    Console.WriteLine();
                    break;
                }
            }
            
            // Check combat readiness
            if (!operatorManager.RestSystem.IsReadyForCombat(player))
            {
                Console.WriteLine($"❌ Operator is too fatigued for combat!");
                Console.WriteLine($"Status: {operatorManager.GetStatus(player)}");
                Console.WriteLine($"Fatigue: {player.Fatigue:F0}/{player.MaxFatigue:F0}");
                float restNeeded = operatorManager.RestSystem.GetMinimumRestHours(player);
                Console.WriteLine($"Minimum rest needed: {restNeeded:F1} hours");
                Console.WriteLine();
                break;
            }
            
            // Prepare operator for combat
            if (!operatorManager.PrepareForCombat(player))
            {
                Console.WriteLine("❌ Failed to prepare operator for combat.");
                Console.WriteLine();
                break;
            }
            
            (playerPetState, playerXp, playerLevel) = StartBattle(player, enemy, playerPetState, operatorManager, playerLevel, playerXp, enemyLevelRandom);
            Console.WriteLine();
            Console.WriteLine("Press any key to return to main menu...");
            Console.ReadKey(true);
            Console.WriteLine();
            break;
            
        case '3':
            playerPetState = RestOperator(player, playerPetState, operatorManager);
            Console.WriteLine("Press any key to return to main menu...");
            Console.ReadKey(true);
            Console.WriteLine();
            break;
            
        case '4':
            playerPetState = FeedOperator(playerPetState);
            Console.WriteLine("Press any key to return to main menu...");
            Console.ReadKey(true);
            Console.WriteLine();
            break;
            
        case '5':
            playerPetState = GiveWaterOperator(playerPetState);
            Console.WriteLine("Press any key to return to main menu...");
            Console.ReadKey(true);
            Console.WriteLine();
            break;
            
        case '6':
            exitRequested = true;
            Console.WriteLine("Exiting game. Goodbye!");
            break;
            
        default:
            Console.WriteLine("Invalid choice. Please select 1-6.");
            Console.WriteLine();
            break;
    }
}

static void DisplayOperatorStats(Operator op, PetState petState, OperatorManager manager, int level, long xp)
{
    // Sync operator fatigue with pet state
    op.Fatigue = petState.Fatigue;
    
    // Use the existing OperatorStatusRenderer to display the virtual pet stats
    var statusView = OperatorStatusView.From(petState);
    OperatorStatusRenderer.Render(statusView);
    
    // Display additional operator information not in the virtual pet view
    Console.WriteLine("OPERATOR INFO");
    Console.WriteLine("-------------");
    Console.WriteLine($"  Name:     {op.Name}");
    Console.WriteLine($"  ID:       {op.Id}");
    Console.WriteLine($"  Level:    {level}");
    Console.WriteLine($"  XP:       {xp:N0}");
    
    // Calculate XP needed for next level
    long xpForNextLevel = (long)((level + 1) * (level + 1) * 1000);
    long xpNeeded = xpForNextLevel - xp;
    if (xpNeeded > 0)
    {
        Console.WriteLine($"  Next Lvl: {xpNeeded:N0} XP needed");
    }
    
    Console.WriteLine($"  Status:   {manager.GetStatus(op)}");
    Console.WriteLine();
    
    Console.WriteLine("COMBAT PROFICIENCIES");
    Console.WriteLine("--------------------");
    Console.WriteLine($"  Accuracy:            {op.Accuracy:F2} ({op.Accuracy*100:F0}%)");
    Console.WriteLine($"  Accuracy Proficiency: {op.AccuracyProficiency:F2} ({op.AccuracyProficiency*100:F0}%)");
    Console.WriteLine($"  Response Proficiency: {op.ResponseProficiency:F2} ({op.ResponseProficiency*100:F0}%)");
    Console.WriteLine();
    
    if (op.EquippedWeapon != null)
    {
        Console.WriteLine("EQUIPMENT");
        Console.WriteLine("---------");
        Console.WriteLine($"  Weapon:     {op.EquippedWeapon.Name}");
        Console.WriteLine($"  Ammo:       {op.CurrentAmmo}/{op.EquippedWeapon.MagazineSize}");
        Console.WriteLine($"  Damage:     {op.EquippedWeapon.BaseDamage:F0}");
        Console.WriteLine($"  Fire Rate:  {op.EquippedWeapon.RoundsPerMinute:F0} RPM");
        Console.WriteLine();
    }
}

static PetState RestOperator(Operator op, PetState petState, OperatorManager manager)
{
    Console.WriteLine("═══ REST OPERATOR ═══");
    Console.WriteLine();
    Console.WriteLine($"Current Status: {manager.GetStatus(op)}");
    Console.WriteLine($"Fatigue: {petState.Fatigue:F0}/{PetConstants.MaxStatValue:F0}");
    Console.WriteLine($"Stress: {petState.Stress:F0}/{PetConstants.MaxStatValue:F0}");
    Console.WriteLine($"Health: {petState.Health:F0}/{PetConstants.MaxStatValue:F0}");
    Console.WriteLine();
    
    if (manager.IsResting(op))
    {
        var duration = manager.GetRestDuration(op);
        Console.WriteLine($"💤 Operator is currently resting ({duration.Hours}h {duration.Minutes}m)");
        Console.WriteLine();
        Console.Write("Wake operator from rest? (y/n): ");
        var choice = Console.ReadKey();
        Console.WriteLine();
        Console.WriteLine();
        
        if (choice.KeyChar == 'y' || choice.KeyChar == 'Y')
        {
            // Apply rest recovery using PetRules
            petState = PetRules.Apply(petState, new RestInput(duration), DateTimeOffset.Now);
            
            manager.WakeFromRest(op);
            op.Fatigue = petState.Fatigue;
            
            Console.WriteLine("✓ Operator awakened and recovered!");
            Console.WriteLine($"New Status: {manager.GetStatus(op)}");
            Console.WriteLine($"Fatigue: {petState.Fatigue:F0}/{PetConstants.MaxStatValue:F0}");
            Console.WriteLine($"Stress: {petState.Stress:F0}/{PetConstants.MaxStatValue:F0}");
            Console.WriteLine($"Health: {petState.Health:F0}/{PetConstants.MaxStatValue:F0}");
        }
        else
        {
            Console.WriteLine("Operator continues resting.");
        }
    }
    else
    {
        Console.Write("Enter rest duration in hours (or press Enter for 1 hour): ");
        var input = Console.ReadLine();
        float hours = 1.0f;
        if (!string.IsNullOrWhiteSpace(input) && float.TryParse(input, out float parsed))
        {
            hours = Math.Max(0.1f, Math.Min(24f, parsed)); // Clamp between 0.1 and 24 hours
        }
        Console.WriteLine();
        
        Console.WriteLine($"💤 Operator resting for {hours:F1} hours...");
        
        // Apply rest recovery using PetRules
        petState = PetRules.Apply(petState, new RestInput(TimeSpan.FromHours(hours)), DateTimeOffset.Now);
        op.Fatigue = petState.Fatigue;
        
        Console.WriteLine();
        Console.WriteLine("✓ Rest complete!");
        Console.WriteLine($"Fatigue: {petState.Fatigue:F0}/{PetConstants.MaxStatValue:F0} (reduced by {hours * PetConstants.FatigueRecoveryPerHour:F0})");
        Console.WriteLine($"Stress: {petState.Stress:F0}/{PetConstants.MaxStatValue:F0} (reduced by ~{hours * PetConstants.StressRecoveryPerHour:F0})");
        Console.WriteLine($"Health: {petState.Health:F0}/{PetConstants.MaxStatValue:F0} (recovered)");
    }
    Console.WriteLine();
    
    return petState;
}

static PetState FeedOperator(PetState petState)
{
    Console.WriteLine("═══ FEED OPERATOR ═══");
    Console.WriteLine();
    Console.WriteLine($"Current Hunger: {petState.Hunger:F0}/{PetConstants.MaxStatValue:F0}");
    Console.WriteLine($"Current Morale: {petState.Morale:F0}/{PetConstants.MaxStatValue:F0}");
    Console.WriteLine();
    
    if (petState.Hunger < 20f)
    {
        Console.WriteLine("✓ Operator is not hungry right now.");
        Console.WriteLine();
        Console.Write("Feed anyway? (y/n): ");
        var choice = Console.ReadKey();
        Console.WriteLine();
        Console.WriteLine();
        
        if (choice.KeyChar != 'y' && choice.KeyChar != 'Y')
        {
            Console.WriteLine("Skipping meal.");
            Console.WriteLine();
            return petState;
        }
    }
    
    Console.WriteLine("Select meal type:");
    Console.WriteLine("1. Light Snack (Nutrition: 15)");
    Console.WriteLine("2. Standard Meal (Nutrition: 30)");
    Console.WriteLine("3. Large Meal (Nutrition: 50)");
    Console.Write("Choose (1-3): ");
    var mealChoice = Console.ReadKey();
    Console.WriteLine();
    Console.WriteLine();
    
    float nutrition = mealChoice.KeyChar switch
    {
        '1' => 15f,
        '2' => 30f,
        '3' => 50f,
        _ => 30f
    };
    
    // Apply eating using PetRules
    petState = PetRules.Apply(petState, new EatInput(nutrition), DateTimeOffset.Now);
    
    Console.WriteLine($"🍴 Operator fed! (Nutrition: {nutrition})");
    Console.WriteLine($"Hunger: {petState.Hunger:F0}/{PetConstants.MaxStatValue:F0}");
    
    if (petState.Hunger < 20f)
    {
        Console.WriteLine("✓ Operator is well-fed!");
    }
    Console.WriteLine();
    
    return petState;
}

static PetState GiveWaterOperator(PetState petState)
{
    Console.WriteLine("═══ GIVE WATER ═══");
    Console.WriteLine();
    Console.WriteLine($"Current Hydration: {petState.Hydration:F0}/{PetConstants.MaxStatValue:F0}");
    Console.WriteLine();
    
    if (petState.Hydration > 80f)
    {
        Console.WriteLine("✓ Operator is well hydrated.");
        Console.WriteLine();
        Console.Write("Give water anyway? (y/n): ");
        var choice = Console.ReadKey();
        Console.WriteLine();
        Console.WriteLine();
        
        if (choice.KeyChar != 'y' && choice.KeyChar != 'Y')
        {
            Console.WriteLine("Skipping water.");
            Console.WriteLine();
            return petState;
        }
    }
    
    Console.WriteLine("Select drink:");
    Console.WriteLine("1. Small Drink (Hydration: 20)");
    Console.WriteLine("2. Standard Drink (Hydration: 40)");
    Console.WriteLine("3. Large Drink (Hydration: 60)");
    Console.Write("Choose (1-3): ");
    var drinkChoice = Console.ReadKey();
    Console.WriteLine();
    Console.WriteLine();
    
    float hydration = drinkChoice.KeyChar switch
    {
        '1' => 20f,
        '2' => 40f,
        '3' => 60f,
        _ => 40f
    };
    
    // Apply drinking using PetRules
    petState = PetRules.Apply(petState, new DrinkInput(hydration), DateTimeOffset.Now);
    
    Console.WriteLine($"💧 Operator hydrated! (Hydration: +{hydration})");
    Console.WriteLine($"Hydration: {petState.Hydration:F0}/{PetConstants.MaxStatValue:F0}");
    
    if (petState.Hydration > 80f)
    {
        Console.WriteLine("✓ Operator is well hydrated!");
    }
    Console.WriteLine();
    
    return petState;
}

static (PetState, long, int) StartBattle(Operator player, Operator enemy, PetState petState, OperatorManager manager, int playerLevel, long playerXp, Random enemyLevelRandom)
{
    // Ensure both operators have weapons
    if (player.EquippedWeapon == null || enemy.EquippedWeapon == null)
    {
        Console.WriteLine("Error: Both operators must have equipped weapons to start battle.");
        return (petState, playerXp, playerLevel);
    }
    
    Console.WriteLine($"Player equipped with: {player.EquippedWeapon.Name}");
    Console.WriteLine($"Enemy equipped with:  {enemy.EquippedWeapon.Name}");
    Console.WriteLine($"Starting distance:    {player.DistanceToOpponent:F1} meters");
    Console.WriteLine();

    // Create combat system
    var combat = new CombatSystemV2(player, enemy, seed: 42); // Fixed seed for determinism
    var ai = new SimpleAIV2(seed: 42);
    var timelineRenderer = new CombatEventTimelineRenderer();

    Console.WriteLine("Combat initialized. Press any key to start...");
    Console.ReadKey(true);
    Console.WriteLine();

    // Main combat loop
    int roundNumber = 1;
    while (combat.Phase != CombatPhase.Ended)
    {
        Console.WriteLine($"═══ ROUND {roundNumber} - PLANNING PHASE ═══");
    Console.WriteLine();
    
    // Show current operator status with movement and cover info
    float playerADS = player.GetADSProgress(combat.CurrentTimeMs);
    int magazineSize = player.EquippedWeapon?.MagazineSize ?? 0;
    
    // Movement state display
    string movementDisplay = player.CurrentMovement.ToString();
    if (player.IsMoving && player.MovementEndTimeMs.HasValue)
    {
        long remainingMs = player.MovementEndTimeMs.Value - combat.CurrentTimeMs;
        movementDisplay += $" ({remainingMs}ms remaining)";
    }
    
    // Cover state display with clarified semantics
    string coverDisplay = player.CurrentCover switch
    {
        CoverState.Partial => "Partial (Peeking)",
        CoverState.Full => "Full (Concealed)",
        CoverState.None => "None",
        _ => "None"
    };
    
    // Suppression state display
    string suppressionDisplay = "None";
    if (player.IsSuppressed)
    {
        if (player.SuppressionLevel >= 0.6f)
            suppressionDisplay = "High";
        else if (player.SuppressionLevel >= 0.3f)
            suppressionDisplay = "Moderate";
        else
            suppressionDisplay = "Low";
        suppressionDisplay += $" ({player.SuppressionLevel:F2})";
    }
    
    Console.WriteLine("╔═══════════════════════════════════════════════════════════════════╗");
    Console.WriteLine($"║ HP: {player.Health,5:F0}/{player.MaxHealth:F0}  │ Ammo: {player.CurrentAmmo,3}/{magazineSize}  │ Stamina: {player.Stamina,5:F0}  │ Distance: {player.DistanceToOpponent,5:F1}m ║");
    
    // Truncate movement display if too long to maintain alignment
    string truncatedMovementDisplay = movementDisplay.Length > 18 
        ? movementDisplay.Substring(0, 15) + "..." 
        : movementDisplay;
    
    Console.WriteLine($"║ Movement: {truncatedMovementDisplay,-18} │ Cover: {coverDisplay,-12} │ ADS: {playerADS*100,3:F0}%     ║");
    Console.WriteLine($"║ Suppression: {suppressionDisplay,-45} ║");
    Console.WriteLine("╚═══════════════════════════════════════════════════════════════════╝");
    Console.WriteLine();
    
    // Create simultaneous intents
    var playerIntents = new SimultaneousIntents(player.Id);
    
    // Ask for Primary Action
    Console.WriteLine("═══ PRIMARY ACTION ═══");
    Console.WriteLine("1. Fire weapon");
    Console.WriteLine("2. Reload");
    Console.WriteLine("3. None");
    Console.Write("Choose primary action (1-3): ");
    var primaryKey = Console.ReadKey();
    Console.WriteLine();
    
    playerIntents.Primary = primaryKey.KeyChar switch
    {
        '1' => PrimaryAction.Fire,
        '2' => PrimaryAction.Reload,
        '3' => PrimaryAction.None,
        _ => PrimaryAction.None
    };
    
    // Ask for Movement Action with new state-based options
    Console.WriteLine();
    Console.WriteLine("═══ MOVEMENT ACTION ═══");
    
    bool canSlide = playerIntents.Primary != PrimaryAction.Reload;
    var movementOptions = new List<(string key, string label, MovementAction action)>();
    
    // Add cancel movement option if currently moving
    if (player.IsMoving)
    {
        movementOptions.Add(("0", "Cancel current movement", MovementAction.Stand));
    }
    
    // Directional movement options
    movementOptions.Add(("1", "Walk toward", MovementAction.WalkToward));
    movementOptions.Add(("2", "Walk away", MovementAction.WalkAway));
    movementOptions.Add(("3", "Sprint toward", MovementAction.SprintToward));
    movementOptions.Add(("4", "Sprint away", MovementAction.SprintAway));
    movementOptions.Add(("5", "Crouch", MovementAction.Crouch));
    
    if (canSlide)
    {
        movementOptions.Add(("6", "Slide toward", MovementAction.SlideToward));
        movementOptions.Add(("7", "Slide away", MovementAction.SlideAway));
    }
    
    movementOptions.Add(("s", "Stand still", MovementAction.Stand));
    
    foreach (var option in movementOptions)
    {
        Console.WriteLine($"{option.key}. {option.label}");
    }
    
    if (!canSlide)
    {
        Console.WriteLine("   (Slide actions disabled: cannot slide while reloading)");
    }
    
    Console.Write($"Choose movement action: ");
    var movementKey = Console.ReadKey();
    Console.WriteLine();
    
    // Handle cancel movement selection
    if (movementKey.KeyChar == '0' && player.IsMoving)
    {
        playerIntents.CancelMovement = true;
        playerIntents.Movement = MovementAction.Stand;
    }
    else
    {
        // Map key to action using the available options
        var selectedMovement = movementOptions.FirstOrDefault(o => o.key == movementKey.KeyChar.ToString());
        playerIntents.Movement = selectedMovement != default ? selectedMovement.action : MovementAction.Stand;
    }
    
    // Ask for Stance Action (adapt to current ADS state and filter incompatible options)
    Console.WriteLine();
    Console.WriteLine("═══ STANCE ACTION ═══");
    
    bool isInADS = player.AimState == AimState.ADS;
    bool isTransitioningToADS = player.AimState == AimState.TransitioningToADS;
    bool canADS = playerIntents.Movement != MovementAction.SlideToward && 
                  playerIntents.Movement != MovementAction.SlideAway;
    
    var stanceOptions = new List<(string key, string label, StanceAction action)>();
    
    if (isInADS)
    {
        // Already fully in ADS
        stanceOptions.Add(("1", "Maintain ADS (stay in ADS)", StanceAction.None));
        stanceOptions.Add(("2", "Exit ADS (return to hip-fire)", StanceAction.ExitADS));
    }
    else if (isTransitioningToADS)
    {
        // Currently transitioning to ADS
        stanceOptions.Add(("1", "Continue ADS transition (keep transitioning)", StanceAction.None));
        stanceOptions.Add(("2", "Cancel ADS (return to hip-fire)", StanceAction.ExitADS));
    }
    else
    {
        // Currently in hip-fire
        if (canADS)
        {
            stanceOptions.Add(("1", "Enter ADS (aim down sights)", StanceAction.EnterADS));
        }
        stanceOptions.Add(("2", "Stay in hip-fire", StanceAction.None));
    }
    
    foreach (var option in stanceOptions)
    {
        Console.WriteLine($"{option.key}. {option.label}");
    }
    
    if (!canADS && !isInADS && !isTransitioningToADS)
    {
        Console.WriteLine("   (ADS disabled: cannot ADS while sliding)");
    }
    
    Console.Write($"Choose stance action: ");
    var stanceKey = Console.ReadKey();
    Console.WriteLine();
    
    // Map key to action using the available options
    var selectedStance = stanceOptions.FirstOrDefault(o => o.key == stanceKey.KeyChar.ToString());
    playerIntents.Stance = selectedStance != default ? selectedStance.action : StanceAction.None;
    
    // Ask for Cover Action
    Console.WriteLine();
    Console.WriteLine("═══ COVER ACTION ═══");
    
    var coverOptions = new List<(string key, string label, CoverAction action)>();
    
    bool canEnterCover = MovementModel.CanEnterCover(player.CurrentMovement);
    
    if (player.CurrentCover == CoverState.None && canEnterCover)
    {
        coverOptions.Add(("1", "Enter Partial Cover", CoverAction.EnterPartial));
        coverOptions.Add(("2", "Enter Full Cover", CoverAction.EnterFull));
        coverOptions.Add(("3", "None", CoverAction.None));
    }
    else if (player.CurrentCover != CoverState.None)
    {
        coverOptions.Add(("1", "Exit Cover", CoverAction.Exit));
        coverOptions.Add(("2", "Stay in Cover", CoverAction.None));
    }
    else
    {
        coverOptions.Add(("1", "None (cannot enter cover while moving)", CoverAction.None));
    }
    
    foreach (var option in coverOptions)
    {
        Console.WriteLine($"{option.key}. {option.label}");
    }
    
    Console.Write($"Choose cover action: ");
    var coverKey = Console.ReadKey();
    Console.WriteLine();
    Console.WriteLine();
    
    // Map key to action using the available options
    var selectedCover = coverOptions.FirstOrDefault(o => o.key == coverKey.KeyChar.ToString());
    playerIntents.Cover = selectedCover != default ? selectedCover.action : CoverAction.None;
    
    // Display chosen intents with current state context
    string stanceDisplay = playerIntents.Stance switch
    {
        StanceAction.EnterADS => "EnterADS",
        StanceAction.ExitADS => "ExitADS",
        StanceAction.None when player.AimState == AimState.TransitioningToADS => "ContinueADS",
        StanceAction.None when player.AimState == AimState.ADS => "MaintainADS",
        StanceAction.None => "None",
        _ => playerIntents.Stance.ToString()
    };
    
    string selectedMovementDisplay = playerIntents.CancelMovement ? "CancelMovement" : playerIntents.Movement.ToString();
    
    Console.WriteLine($"Selected: Primary={playerIntents.Primary}, Movement={selectedMovementDisplay}, Stance={stanceDisplay}, Cover={playerIntents.Cover}");
    
    // Submit player intents
    var playerResult = combat.SubmitIntents(player, playerIntents);
    if (!playerResult.success)
    {
        Console.WriteLine($"❌ Player intents rejected: {playerResult.errorMessage}");
        Console.WriteLine("⚠ Incompatible actions detected. Please note:");
        Console.WriteLine("  - Cannot reload while sliding");
        Console.WriteLine("  - Sprinting will auto-exit ADS");
        Console.WriteLine("  - Cannot ADS while sliding");
        Console.WriteLine("  - Can only enter cover when stationary or crouching");
        Console.WriteLine("Defaulting to Stop.");
        combat.SubmitIntents(player, SimultaneousIntents.CreateStop(player.Id));
    }
    else
    {
        Console.WriteLine($"✓ Player intents accepted");
        
        // Show warnings about auto-adjustments
        if ((playerIntents.Movement == MovementAction.SprintToward || playerIntents.Movement == MovementAction.SprintAway) &&
            playerIntents.Stance == StanceAction.EnterADS)
        {
            Console.WriteLine("  ℹ Note: Sprinting will prevent ADS or exit it if already in ADS");
        }
    }
    
    // AI chooses intents
    var enemyIntents = ai.DecideIntents(enemy, player, combat);
    var enemyResult = combat.SubmitIntents(enemy, enemyIntents);
    if (!enemyResult.success)
    {
        Console.WriteLine($"❌ Enemy intent rejected: {enemyResult.errorMessage}");
        combat.SubmitIntents(enemy, SimultaneousIntents.CreateStop(enemy.Id));
    }
    else
    {
        Console.WriteLine($"✓ Enemy intents: Primary={enemyIntents.Primary}, Movement={enemyIntents.Movement}, Stance={enemyIntents.Stance}");
    }
    
    Console.WriteLine();
    Console.WriteLine("Press any key to execute...");
    Console.ReadKey(true);
    Console.WriteLine();
    
    // Execute until combat ends or round completes
    combat.BeginExecution();
    
    // Execute all events until combat ends or no more events
    // This removes the "reaction window extra turn" concept
    while (combat.Phase == CombatPhase.Executing)
    {
        bool hasMoreEvents = combat.ExecuteUntilReactionWindow();
        if (!hasMoreEvents || combat.Phase == CombatPhase.Ended)
        {
            break;
        }
        
        // Reaction windows now just expedite round end
        // No "extra turn" for AI - just continue to next planning phase
        if (combat.Phase == CombatPhase.Planning)
        {
            // Round completed, proceed to next planning phase
            break;
        }
    }
    
    if (combat.Phase == CombatPhase.Ended)
    {
        // Combat ended
        break;
    }
    
    Console.WriteLine();
    Console.WriteLine("Press any key to continue to next round...");
    Console.ReadKey(true);
    Console.WriteLine();
    
        roundNumber++;
    }

    Console.WriteLine();
    Console.WriteLine("═════════════════════════════════════════════════════════════");
    Console.WriteLine("                    COMBAT COMPLETE");
    Console.WriteLine("═════════════════════════════════════════════════════════════");
    Console.WriteLine();

    if (player.IsAlive && !enemy.IsAlive)
    {
        Console.WriteLine("🎉 VICTORY! You defeated the enemy!");
    }
    else if (!player.IsAlive && enemy.IsAlive)
    {
        Console.WriteLine("💀 DEFEAT! The enemy defeated you!");
    }
    else
    {
        Console.WriteLine("Draw? This shouldn't happen...");
    }

    Console.WriteLine();
    Console.WriteLine($"Final Stats:");
    Console.WriteLine($"  Player: {player.Health:F0}/{player.MaxHealth:F0} HP");
    Console.WriteLine($"  Enemy:  {enemy.Health:F0}/{enemy.MaxHealth:F0} HP");
    Console.WriteLine($"  Duration: {combat.CurrentTimeMs}ms ({combat.CurrentTimeMs/1000.0:F1}s)");
    Console.WriteLine();

    try
    {
        var timelineEntries = timelineRenderer.BuildTimelineEntries(combat.ExecutedEvents, player, enemy, combat.TimelineEntries);
        var timelinePath = Path.Combine(Environment.CurrentDirectory, "combat-timeline.png");
        timelineRenderer.RenderTimeline(timelineEntries, timelinePath);
        Console.WriteLine($"Combat timeline saved to: {timelinePath}");
        Console.WriteLine();
    }
    catch (Exception ex)
    {
        Console.WriteLine("Warning: Failed to render combat timeline. The application will continue without timeline output.");
        Console.WriteLine($"  Details: {ex.Message}");
        Console.WriteLine();
    }
    
    // Apply post-combat fatigue management
    manager.CompleteCombat(player, player.IsAlive);
    
    if (player.IsAlive)
    {
        // Calculate mission impact for PetRules
        float healthLost = player.MaxHealth - player.Health;
        int hitsTaken = (int)Math.Ceiling(healthLost / 10f); // Estimate hits based on health lost
        
        // Generate enemy level (varies -2 to +2 levels from player)
        // Uses shared Random instance for consistent randomness across battles
        int enemyLevel = playerLevel + enemyLevelRandom.Next(-2, 3);
        enemyLevel = Math.Max(0, enemyLevel); // Ensure non-negative
        
        // Calculate opponent difficulty using OpponentDifficulty system
        // Uses level difference (50 base, ±10 per level difference)
        float opponentDifficulty = OpponentDifficulty.Compute(
            opponentLevel: enemyLevel,
            playerLevel: playerLevel
        );
        
        // Adjust difficulty based on combat outcome for more accurate stress calculation
        if (!enemy.IsAlive)
        {
            // Victory - reduce difficulty slightly (they were beatable)
            opponentDifficulty *= 0.9f;
        }
        else
        {
            // Defeat - increase difficulty (they were too strong)
            opponentDifficulty = Math.Min(100f, opponentDifficulty * 1.2f);
        }
        
        // Apply mission effects using PetRules
        petState = PetRules.Apply(petState, new MissionInput(hitsTaken, opponentDifficulty), DateTimeOffset.Now);
        
        // Award XP based on opponent difficulty and outcome
        long xpGained = 0L;
        if (!enemy.IsAlive)
        {
            // Victory: Award XP based on adjusted difficulty
            // Actual range: 180-1800 XP (after 0.9x victory modifier on 10-100 difficulty)
            xpGained = (long)(opponentDifficulty * 20);
            playerXp += xpGained;
            
            // Check for level up
            int newLevel = OpponentDifficulty.ComputeLevelFromXp(playerXp);
            if (newLevel > playerLevel)
            {
                int levelsGained = newLevel - playerLevel;
                playerLevel = newLevel;
                Console.WriteLine();
                Console.WriteLine($"🎉 LEVEL UP! You are now level {playerLevel}! (+{levelsGained} level{(levelsGained > 1 ? "s" : "")})");
            }
        }
        
        // Sync operator stats with pet state
        player.Fatigue = petState.Fatigue;
        
        Console.WriteLine("═══ POST-COMBAT STATUS ═══");
        Console.WriteLine($"Enemy Level: {enemyLevel} (Difficulty: {opponentDifficulty:F0})");
        if (xpGained > 0)
        {
            Console.WriteLine($"XP Gained: +{xpGained:N0} (Total: {playerXp:N0})");
        }
        Console.WriteLine();
        Console.WriteLine($"Fatigue: {petState.Fatigue:F0}/{PetConstants.MaxStatValue:F0}");
        Console.WriteLine($"Stress: {petState.Stress:F0}/{PetConstants.MaxStatValue:F0}");
        Console.WriteLine($"Injury: {petState.Injury:F0}/{PetConstants.MaxStatValue:F0}");
        Console.WriteLine($"Morale: {petState.Morale:F0}/{PetConstants.MaxStatValue:F0}");
        Console.WriteLine($"Status: {manager.GetStatus(player)}");
        
        if (!manager.RestSystem.IsReadyForCombat(player))
        {
            float restNeeded = manager.RestSystem.GetMinimumRestHours(player);
            Console.WriteLine($"⚠️  Operator needs {restNeeded:F1} hours rest before next combat!");
        }
        
        // Warnings for critical conditions
        if (petState.Stress > PetConstants.HighStressThreshold)
        {
            Console.WriteLine($"⚠️  High stress detected! Consider resting.");
        }
        if (petState.Injury > PetConstants.HighInjuryThreshold)
        {
            Console.WriteLine($"⚠️  Significant injuries! Health recovery will be slower.");
        }
        if (petState.Hunger > PetConstants.HungerStressRecoveryThreshold)
        {
            Console.WriteLine($"⚠️  Operator is hungry! This affects stress recovery.");
        }
        if (petState.Hydration < PetConstants.HydrationStressRecoveryThreshold)
        {
            Console.WriteLine($"⚠️  Low hydration! This affects stress recovery.");
        }
        
        Console.WriteLine();
    }
    
    return (petState, playerXp, playerLevel);
}
