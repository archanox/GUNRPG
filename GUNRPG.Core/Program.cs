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
var combat = new CombatSystem(player, enemy, seed: 42); // Fixed seed for determinism
var ai = new SimpleAI(seed: 42);

Console.WriteLine("Combat initialized. Press any key to start...");
Console.ReadKey(true);
Console.WriteLine();

// Main combat loop
int roundNumber = 1;
while (combat.Phase != CombatPhase.Ended)
{
    Console.WriteLine($"═══ ROUND {roundNumber} - PLANNING PHASE ═══");
    Console.WriteLine();
    
    // Player chooses intent
    Console.WriteLine("Choose your action:");
    Console.WriteLine("1. Fire weapon");
    Console.WriteLine("2. Reload");
    Console.WriteLine("3. Enter ADS");
    Console.WriteLine("4. Walk toward");
    Console.WriteLine("5. Walk away");
    Console.WriteLine("6. Sprint toward");
    Console.WriteLine("7. Sprint away");
    Console.WriteLine("8. Stop/Wait");
    Console.Write("\nYour choice (1-8): ");
    
    var key = Console.ReadKey();
    Console.WriteLine();
    Console.WriteLine();
    
    Intent? playerIntent = key.KeyChar switch
    {
        '1' => new FireWeaponIntent(player.Id),
        '2' => new ReloadIntent(player.Id),
        '3' => new EnterADSIntent(player.Id),
        '4' => new WalkIntent(player.Id, towardOpponent: true),
        '5' => new WalkIntent(player.Id, towardOpponent: false),
        '6' => new SprintIntent(player.Id, towardOpponent: true),
        '7' => new SprintIntent(player.Id, towardOpponent: false),
        '8' => new StopIntent(player.Id),
        _ => new StopIntent(player.Id)
    };
    
    // Submit player intent
    var playerResult = combat.SubmitIntent(player, playerIntent);
    if (!playerResult.success)
    {
        Console.WriteLine($"❌ Player intent rejected: {playerResult.errorMessage}");
        Console.WriteLine("Defaulting to Stop.");
        combat.SubmitIntent(player, new StopIntent(player.Id));
    }
    else
    {
        Console.WriteLine($"✓ Player intent: {playerIntent.Type}");
    }
    
    // AI chooses intent
    var enemyIntent = ai.DecideIntent(enemy, player, combat);
    var enemyResult = combat.SubmitIntent(enemy, enemyIntent);
    if (!enemyResult.success)
    {
        Console.WriteLine($"❌ Enemy intent rejected: {enemyResult.errorMessage}");
        combat.SubmitIntent(enemy, new StopIntent(enemy.Id));
    }
    else
    {
        Console.WriteLine($"✓ Enemy intent: {enemyIntent.Type}");
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
    if (ai.ShouldReact(enemy, player, combat, out var newEnemyIntent) && newEnemyIntent != null)
    {
        Console.WriteLine($"Enemy reacts! New intent: {newEnemyIntent.Type}");
        combat.CancelIntent(enemy);
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
