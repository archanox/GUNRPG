using GUNRPG.Application.Backend;
using GUNRPG.Application.Dtos;
using GUNRPG.Application.Mapping;
using GUNRPG.Application.Sessions;
using GUNRPG.Core;
using GUNRPG.Core.Combat;
using GUNRPG.Core.Intents;
using GUNRPG.Core.Operators;
using GUNRPG.Core.Weapons;

namespace GUNRPG.Application.Combat;

/// <summary>
/// Pure deterministic combat simulation.
/// All randomness derives from the provided seed via <see cref="SeededRandom"/>.
/// Uses no external state, no <c>DateTime.Now</c>, and no static <c>Random</c>.
/// Offline and server both reference this same engine to ensure replay parity.
/// </summary>
public sealed class DeterministicCombatEngine : IDeterministicCombatEngine
{
    private const int MaxCombatTurns = 64;

    /// <inheritdoc />
    public CombatResult Execute(OperatorDto snapshot, int seed)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var session = CreateSession(snapshot, seed);
        var initialSnapshot = SessionMapping.ToSnapshot(session);
        session.SetReplayInitialSnapshotJson(OfflineCombatReplay.SerializeCombatSnapshot(initialSnapshot));

        for (var turn = 0; turn < MaxCombatTurns && session.Phase != SessionPhase.Completed; turn++)
        {
            CombatSessionService.ExecuteReplayTurn(session, SelectNextTurn(session));
        }

        var isVictory = session.Player.IsAlive && !session.Enemy.IsAlive;
        var operatorDied = !session.Player.IsAlive;
        var xpGained = isVictory ? 100 : operatorDied ? 0 : 50;
        var battleLog = SessionMapping.ToDto(session).BattleLog;

        var resultOperator = new OperatorDto
        {
            Id = snapshot.Id,
            Name = snapshot.Name,
            TotalXp = snapshot.TotalXp + xpGained,
            CurrentHealth = operatorDied ? snapshot.MaxHealth : Math.Max(1f, session.Player.Health),
            MaxHealth = snapshot.MaxHealth,
            EquippedWeaponName = snapshot.EquippedWeaponName,
            ExfilStreak = snapshot.ExfilStreak,
            IsDead = false,
            CurrentMode = operatorDied ? "Base" : "Infil",
            ActiveCombatSessionId = null,
            InfilSessionId = operatorDied ? null : snapshot.InfilSessionId,
            InfilStartTime = operatorDied ? null : snapshot.InfilStartTime,
            LockedLoadout = snapshot.LockedLoadout,
            Pet = snapshot.Pet
        };

        return new CombatResult
        {
            ResultOperator = resultOperator,
            IsVictory = isVictory,
            OperatorDied = operatorDied,
            BattleLog = battleLog
        };
    }

    private static CombatSession CreateSession(OperatorDto snapshot, int seed)
    {
        var session = CombatSession.CreateDefault(
            playerName: snapshot.Name,
            seed: seed,
            operatorId: ResolveOperatorGuid(snapshot.Id));

        var player = session.Player;
        player.MaxHealth = snapshot.MaxHealth > 0f ? snapshot.MaxHealth : player.MaxHealth;
        player.Health = Math.Clamp(snapshot.CurrentHealth, 0f, player.MaxHealth);
        player.EquippedWeapon = ResolveWeapon(snapshot.EquippedWeaponName, snapshot.LockedLoadout);
        player.CurrentAmmo = player.EquippedWeapon?.MagazineSize ?? 0;
        player.AimState = AimState.ADS;

        return session;
    }

    private static Guid ResolveOperatorGuid(string operatorId)
    {
        if (Guid.TryParse(operatorId, out var parsed))
            return parsed;

        // Offline envelopes use string IDs. When they are not GUID-formatted, map them
        // to a stable synthetic GUID so combat sessions still have deterministic identities.
        return StableGuidFactory.FromString(operatorId ?? string.Empty);
    }

    private static IntentSnapshot SelectNextTurn(CombatSession session)
    {
        foreach (var primary in GetPreferredPrimaryActions(session.Player))
        {
            var turn = new IntentSnapshot
            {
                OperatorId = session.Player.Id,
                Primary = primary,
                Movement = MovementAction.Stand,
                Stance = StanceAction.None,
                Cover = CoverAction.None
            };

            var validation = new SimultaneousIntents(session.Player.Id)
            {
                Primary = turn.Primary,
                Movement = turn.Movement,
                Stance = turn.Stance,
                Cover = turn.Cover,
                CancelMovement = turn.CancelMovement
            }.Validate(session.Player);

            if (validation.isValid)
            {
                return turn;
            }
        }

        return new IntentSnapshot
        {
            OperatorId = session.Player.Id,
            Primary = PrimaryAction.None,
            Movement = MovementAction.Stand,
            Stance = StanceAction.None,
            Cover = CoverAction.None
        };
    }

    private static IEnumerable<PrimaryAction> GetPreferredPrimaryActions(Operator player)
    {
        if (player.EquippedWeapon != null &&
            player.CurrentAmmo <= 0)
        {
            yield return PrimaryAction.Reload;
        }

        yield return PrimaryAction.Fire;
        yield return PrimaryAction.Reload;
        yield return PrimaryAction.None;
    }

    private static Weapon ResolveWeapon(string? equippedWeaponName, string? lockedLoadout)
    {
        var normalized = (equippedWeaponName ?? lockedLoadout ?? string.Empty).Trim();
        return WeaponFactory.TryCreateWeapon(normalized) ?? WeaponFactory.CreateM15Mod0();
    }
}
