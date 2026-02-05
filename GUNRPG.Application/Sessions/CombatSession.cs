using GUNRPG.Core;
using GUNRPG.Core.AI;
using GUNRPG.Core.Combat;
using GUNRPG.Core.Operators;
using GUNRPG.Core.VirtualPet;

namespace GUNRPG.Application.Sessions;

/// <summary>
/// Aggregates combat state, AI, and operator lifecycle for a single session.
/// </summary>
public sealed class CombatSession
{
    public Guid Id { get; }
    public CombatSystemV2 Combat { get; }
    public SimpleAIV2 Ai { get; }
    public OperatorManager OperatorManager { get; }
    public PetState PetState { get; set; }
    public long PlayerXp { get; set; }
    public int PlayerLevel { get; set; }
    public int EnemyLevel { get; }
    public int Seed { get; }
    public DateTimeOffset CreatedAt { get; }
    public bool PostCombatResolved { get; set; }

    public Operator Player => Combat.Player;
    public Operator Enemy => Combat.Enemy;

    public CombatSession(
        Guid id,
        CombatSystemV2 combat,
        SimpleAIV2 ai,
        OperatorManager operatorManager,
        PetState petState,
        long playerXp,
        int playerLevel,
        int enemyLevel,
        int seed)
    {
        Id = id;
        Combat = combat;
        Ai = ai;
        OperatorManager = operatorManager;
        PetState = petState;
        PlayerXp = playerXp;
        PlayerLevel = playerLevel;
        EnemyLevel = enemyLevel;
        Seed = seed;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public static CombatSession CreateDefault(string? playerName = null, int? seed = null, float? startingDistance = null, string? enemyName = null)
    {
        var resolvedSeed = seed ?? Random.Shared.Next();
        var name = string.IsNullOrWhiteSpace(playerName) ? "Player" : playerName.Trim();
        var foeName = string.IsNullOrWhiteSpace(enemyName) ? "Enemy" : enemyName.Trim();

        var player = new Operator(name)
        {
            EquippedWeapon = WeaponFactory.CreateSokol545(),
            DistanceToOpponent = startingDistance ?? 15f
        };
        player.CurrentAmmo = player.EquippedWeapon!.MagazineSize;

        var enemy = new Operator(foeName)
        {
            EquippedWeapon = WeaponFactory.CreateSturmwolf45(),
            DistanceToOpponent = startingDistance ?? 15f
        };
        enemy.CurrentAmmo = enemy.EquippedWeapon!.MagazineSize;

        var debugOptions = new CombatDebugOptions { VerboseShotLogs = false };
        var combat = new CombatSystemV2(player, enemy, seed: resolvedSeed, debugOptions: debugOptions);
        var ai = new SimpleAIV2(seed: resolvedSeed);
        var operatorManager = new OperatorManager();

        var petState = new PetState(player.Id, 100f, 0f, 0f, 0f, 100f, 0f, 100f, DateTimeOffset.UtcNow);
        var enemyLevel = Math.Max(0, new Random(resolvedSeed).Next(-2, 3));

        return new CombatSession(Guid.NewGuid(), combat, ai, operatorManager, petState, 0, 0, enemyLevel, resolvedSeed);
    }
}
