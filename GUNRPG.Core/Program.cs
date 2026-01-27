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
    EquippedWeapon = WeaponFactory.CreateM4A1(),
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
    
    // Ask for Movement Action
    Console.WriteLine();
    Console.WriteLine("═══ MOVEMENT ACTION ═══");
    Console.WriteLine("1. Walk toward");
    Console.WriteLine("2. Walk away");
    Console.WriteLine("3. Sprint toward");
    Console.WriteLine("4. Sprint away");
    Console.WriteLine("5. Slide toward");
    Console.WriteLine("6. Slide away");
    Console.WriteLine("7. None");
    Console.Write("Choose movement action (1-7): ");
    var movementKey = Console.ReadKey();
    Console.WriteLine();
    
    playerIntents.Movement = movementKey.KeyChar switch
    {
        '1' => MovementAction.WalkToward,
        '2' => MovementAction.WalkAway,
        '3' => MovementAction.SprintToward,
        '4' => MovementAction.SprintAway,
        '5' => MovementAction.SlideToward,
        '6' => MovementAction.SlideAway,
        '7' => MovementAction.None,
        _ => MovementAction.None
    };
    
    // Ask for Stance Action
    Console.WriteLine();
    Console.WriteLine("═══ STANCE ACTION ═══");
    Console.WriteLine("1. Enter ADS");
    Console.WriteLine("2. Exit ADS");
    Console.WriteLine("3. None");
    Console.Write("Choose stance action (1-3): ");
    var stanceKey = Console.ReadKey();
    Console.WriteLine();
    Console.WriteLine();
    
    playerIntents.Stance = stanceKey.KeyChar switch
    {
        '1' => StanceAction.EnterADS,
        '2' => StanceAction.ExitADS,
        '3' => StanceAction.None,
        _ => StanceAction.None
    };
    
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
    
    // Execute
    combat.BeginExecution();
    bool hasReactionWindow = combat.ExecuteUntilReactionWindow();
    
    if (!hasReactionWindow)
    {
        // Combat ended
        break;
    }
    
    // Reaction window - AI decides if it wants to react
    if (ai.ShouldReact(enemy, player, combat, out var newEnemyIntents) && newEnemyIntents != null)
    {
        Console.WriteLine($"Enemy reacts! New intents: Primary={newEnemyIntents.Primary}, Movement={newEnemyIntents.Movement}");
        combat.CancelIntents(enemy);
    }
    else
    {
        Console.WriteLine("Enemy continues current action.");
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
