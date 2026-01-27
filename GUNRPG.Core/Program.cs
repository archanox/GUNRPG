using GUNRPG.Core;
using GUNRPG.Core.AI;
using GUNRPG.Core.Combat;
using GUNRPG.Core.Intents;
using GUNRPG.Core.Operators;

Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
Console.WriteLine("║          GUNRPG - Text-Based Tactical Combat Simulator       ║");
Console.WriteLine("║                     Foundation Demo v1.0                     ║");
Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
Console.WriteLine();

// Create operators
var player = new Operator("Player")
{
    EquippedWeapon = WeaponFactory.CreateRK9(),
    DistanceToOpponent = 15f
};
player.CurrentAmmo = player.EquippedWeapon!.MagazineSize;

var enemy = new Operator("Enemy")
{
    EquippedWeapon = WeaponFactory.CreateAK47(),
    DistanceToOpponent = 15f
};
enemy.CurrentAmmo = enemy.EquippedWeapon!.MagazineSize;

Console.WriteLine($"Player equipped with: {player.EquippedWeapon.Name}");
Console.WriteLine($"Enemy equipped with:  {enemy.EquippedWeapon.Name}");
Console.WriteLine($"Starting distance:    {player.DistanceToOpponent:F1} meters");
Console.WriteLine();

// Create combat system
var combat = new CombatSystemV2(player, enemy, seed: 42); // Fixed seed for determinism
var ai = new SimpleAIV2(seed: 42);

Console.WriteLine("Combat initialized. Press any key to start...");
Console.ReadKey(true);
Console.WriteLine();

// Main combat loop
int roundNumber = 1;
while (combat.Phase != CombatPhase.Ended)
{
    Console.WriteLine($"═══ ROUND {roundNumber} - PLANNING PHASE ═══");
    Console.WriteLine();
    
    // Show current operator status
    float playerADS = player.GetADSProgress(combat.CurrentTimeMs);
    int magazineSize = player.EquippedWeapon?.MagazineSize ?? 0;
    Console.WriteLine($"Player Status: HP {player.Health:F0}/{player.MaxHealth:F0}, Ammo {player.CurrentAmmo}/{magazineSize}, " +
                      $"Stamina {player.Stamina:F0}, ADS {playerADS*100:F0}%, Distance {player.DistanceToOpponent:F1}m");
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
    
    // Ask for Movement Action (filter out incompatible options)
    Console.WriteLine();
    Console.WriteLine("═══ MOVEMENT ACTION ═══");
    
    bool canSlide = playerIntents.Primary != PrimaryAction.Reload;
    var movementOptions = new List<(string key, string label, MovementAction action)>
    {
        ("1", "Walk toward", MovementAction.WalkToward),
        ("2", "Walk away", MovementAction.WalkAway),
        ("3", "Sprint toward", MovementAction.SprintToward),
        ("4", "Sprint away", MovementAction.SprintAway),
    };
    
    if (canSlide)
    {
        movementOptions.Add(("5", "Slide toward", MovementAction.SlideToward));
        movementOptions.Add(("6", "Slide away", MovementAction.SlideAway));
    }
    
    movementOptions.Add(("7", "Stand", MovementAction.Stand));
    
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
    
    // Map key to action using the available options
    var selectedMovement = movementOptions.FirstOrDefault(o => o.key == movementKey.KeyChar.ToString());
    playerIntents.Movement = selectedMovement != default ? selectedMovement.action : MovementAction.Stand;
    
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
    Console.WriteLine();
    
    // Map key to action using the available options
    var selectedStance = stanceOptions.FirstOrDefault(o => o.key == stanceKey.KeyChar.ToString());
    playerIntents.Stance = selectedStance != default ? selectedStance.action : StanceAction.None;
    
    // Display chosen intents
    Console.WriteLine($"Selected: Primary={playerIntents.Primary}, Movement={playerIntents.Movement}, Stance={playerIntents.Stance}");
    
    // Submit player intents
    var playerResult = combat.SubmitIntents(player, playerIntents);
    if (!playerResult.success)
    {
        Console.WriteLine($"❌ Player intents rejected: {playerResult.errorMessage}");
        Console.WriteLine("⚠ Incompatible actions detected. Please note:");
        Console.WriteLine("  - Cannot reload while sliding");
        Console.WriteLine("  - Sprinting will auto-exit ADS");
        Console.WriteLine("  - Cannot ADS while sliding");
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
Console.WriteLine("Press any key to exit...");
Console.ReadKey();
