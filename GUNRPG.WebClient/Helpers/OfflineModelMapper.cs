using System.Text.Json;
using GUNRPG.Application.Backend;
using GUNRPG.Application.Dtos;
using GUNRPG.Core.Intents;

namespace GUNRPG.WebClient.Helpers;

internal static class OfflineModelMapper
{
    public static OperatorDto ToBackendDto(OperatorState state) => new()
    {
        Id = state.Id.ToString(),
        Name = state.Name,
        TotalXp = state.TotalXp,
        CurrentHealth = state.CurrentHealth,
        MaxHealth = state.MaxHealth,
        EquippedWeaponName = state.EquippedWeaponName,
        ExfilStreak = state.ExfilStreak,
        IsDead = state.IsDead,
        CurrentMode = state.CurrentMode,
        ActiveCombatSessionId = state.ActiveCombatSessionId,
        InfilSessionId = state.InfilSessionId,
        InfilStartTime = state.InfilStartTime,
        LockedLoadout = state.LockedLoadout,
        Pet = state.Pet is null
            ? null
            : new PetStateDto
            {
                Health = state.Pet.Health,
                Fatigue = state.Pet.Fatigue,
                Injury = state.Pet.Injury,
                Stress = state.Pet.Stress,
                Morale = state.Pet.Morale,
                Hunger = state.Pet.Hunger,
                Hydration = state.Pet.Hydration,
                LastUpdated = state.Pet.LastUpdated
            }
    };

    public static OperatorState ToOperatorState(OperatorDto dto) => new()
    {
        Id = Guid.Parse(dto.Id),
        Name = dto.Name,
        TotalXp = dto.TotalXp,
        CurrentHealth = dto.CurrentHealth,
        MaxHealth = dto.MaxHealth,
        EquippedWeaponName = dto.EquippedWeaponName,
        ExfilStreak = dto.ExfilStreak,
        IsDead = dto.IsDead,
        CurrentMode = dto.CurrentMode,
        InfilStartTime = dto.InfilStartTime,
        InfilSessionId = dto.InfilSessionId,
        ActiveCombatSessionId = dto.ActiveCombatSessionId,
        LockedLoadout = dto.LockedLoadout,
        Pet = dto.Pet is null
            ? null
            : new GUNRPG.ClientModels.PetState
            {
                Health = dto.Pet.Health,
                Fatigue = dto.Pet.Fatigue,
                Injury = dto.Pet.Injury,
                Stress = dto.Pet.Stress,
                Morale = dto.Pet.Morale,
                Hunger = dto.Pet.Hunger,
                Hydration = dto.Pet.Hydration,
                LastUpdated = dto.Pet.LastUpdated
            }
    };

    public static OperatorSummary ToSummary(OperatorState state) => new()
    {
        Id = state.Id,
        Name = state.Name,
        CurrentMode = state.CurrentMode,
        IsDead = state.IsDead,
        TotalXp = state.TotalXp,
        CurrentHealth = state.CurrentHealth,
        MaxHealth = state.MaxHealth
    };

    public static CombatSession ToClientModel(CombatSessionDto dto) => new()
    {
        Id = dto.Id,
        OperatorId = dto.OperatorId,
        Phase = dto.Phase.ToString(),
        CurrentTimeMs = dto.CurrentTimeMs,
        Player = ToPlayerState(dto.Player),
        Enemy = ToPlayerState(dto.Enemy),
        Pet = ToPetState(dto.Pet),
        EnemyLevel = dto.EnemyLevel,
        TurnNumber = dto.TurnNumber,
        BattleLog = dto.BattleLog.Select(x => new BattleLogEntry
        {
            EventType = x.EventType,
            TimeMs = x.TimeMs,
            Message = x.Message,
            ActorName = x.ActorName
        }).ToList()
    };

    public static GUNRPG.Application.Dtos.IntentDto ToApplicationIntent(string? primary, string? movement, string? stance, string? cover) => new()
    {
        Primary = ParseEnum<PrimaryAction>(primary),
        Movement = ParseEnum<MovementAction>(movement),
        Stance = ParseEnum<StanceAction>(stance),
        Cover = ParseEnum<CoverAction>(cover)
    };

    public static string ToCanonicalJson(OperatorDto dto, JsonSerializerOptions options) => JsonSerializer.Serialize(dto, options);

    private static PlayerState ToPlayerState(PlayerStateDto dto) => new()
    {
        Id = dto.Id,
        Name = dto.Name,
        Health = dto.Health,
        MaxHealth = dto.MaxHealth,
        Stamina = dto.Stamina,
        Fatigue = dto.Fatigue,
        SuppressionLevel = dto.SuppressionLevel,
        IsSuppressed = dto.IsSuppressed,
        DistanceToOpponent = dto.DistanceToOpponent,
        CurrentAmmo = dto.CurrentAmmo,
        MagazineSize = dto.MagazineSize,
        AimState = dto.AimState.ToString(),
        MovementState = dto.MovementState.ToString(),
        CurrentMovement = dto.CurrentMovement.ToString(),
        CurrentDirection = dto.CurrentDirection.ToString(),
        CurrentCover = dto.CurrentCover.ToString(),
        IsMoving = dto.IsMoving,
        IsAlive = dto.IsAlive
    };

    private static GUNRPG.ClientModels.PetState ToPetState(GUNRPG.Application.Dtos.PetStateDto dto) => new()
    {
        Health = dto.Health,
        Fatigue = dto.Fatigue,
        Injury = dto.Injury,
        Stress = dto.Stress,
        Morale = dto.Morale,
        Hunger = dto.Hunger,
        Hydration = dto.Hydration,
        LastUpdated = dto.LastUpdated
    };

    private static TEnum? ParseEnum<TEnum>(string? raw) where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(raw) || string.Equals(raw, "None", StringComparison.OrdinalIgnoreCase))
            return null;

        return Enum.TryParse<TEnum>(raw, ignoreCase: true, out var value)
            ? value
            : null;
    }
}
