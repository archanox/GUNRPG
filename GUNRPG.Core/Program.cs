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
    
    // Player chooses intent (now with simultaneous intents)
    Console.WriteLine("Choose your actions:");
    Console.WriteLine("Primary Actions:");
    Console.WriteLine("  1. Fire weapon");
    Console.WriteLine("  2. Reload");
    Console.WriteLine("  3. None (no primary action)");
    Console.WriteLine("Movement Actions:");
    Console.WriteLine("  4. Walk toward");
    Console.WriteLine("  5. Walk away");
    Console.WriteLine("  6. Sprint toward");
    Console.WriteLine("  7. Sprint away");
    Console.WriteLine("  8. None (no movement)");
    Console.WriteLine("Stance Actions:");
    Console.WriteLine("  9. Enter ADS");
    Console.WriteLine("  0. Exit ADS");
    Console.WriteLine();
    Console.WriteLine("For simplicity, choose primary action (1-3): ");
    
    var key = Console.ReadKey();
    Console.WriteLine();
    Console.WriteLine();
    
    // Create simultaneous intents
    var playerIntents = new SimultaneousIntents(player.Id);
    
    // Map key to primary action
    playerIntents.Primary = key.KeyChar switch
    {
        '1' => PrimaryAction.Fire,
        '2' => PrimaryAction.Reload,
        '3' => PrimaryAction.None,
        _ => PrimaryAction.None
    };
    
    // For demo purposes, add some automatic decisions
    // Auto-enter ADS if firing and not already in ADS
    if (playerIntents.Primary == PrimaryAction.Fire && player.AimState != AimState.ADS)
    {
        playerIntents.Stance = StanceAction.EnterADS;
    }
    
    // Submit player intents
    var playerResult = combat.SubmitIntents(player, playerIntents);
    if (!playerResult.success)
    {
        Console.WriteLine($"❌ Player intent rejected: {playerResult.errorMessage}");
        Console.WriteLine("Defaulting to Stop.");
        combat.SubmitIntents(player, SimultaneousIntents.CreateStop(player.Id));
    }
    else
    {
        Console.WriteLine($"✓ Player intents: Primary={playerIntents.Primary}, Movement={playerIntents.Movement}, Stance={playerIntents.Stance}");
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
