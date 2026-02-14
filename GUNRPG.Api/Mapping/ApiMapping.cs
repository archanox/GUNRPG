using GUNRPG.Api.Dtos;
using GUNRPG.Application.Dtos;
using GUNRPG.Application.Requests;

namespace GUNRPG.Api.Mapping;

/// <summary>
/// Maps between API DTOs and Application DTOs.
/// This layer keeps the API contract decoupled from application/domain types.
/// </summary>
public static class ApiMapping
{
    public static SessionCreateRequest ToApplicationRequest(ApiSessionCreateRequest apiRequest)
    {
        return new SessionCreateRequest
        {
            Id = apiRequest.Id,
            OperatorId = apiRequest.OperatorId,
            PlayerName = apiRequest.PlayerName,
            Seed = apiRequest.Seed,
            StartingDistance = apiRequest.StartingDistance,
            EnemyName = apiRequest.EnemyName
        };
    }

    public static SubmitIntentsRequest ToApplicationRequest(ApiSubmitIntentsRequest apiRequest)
    {
        return new SubmitIntentsRequest
        {
            Intents = ToApplicationDto(apiRequest.Intents)
        };
    }

    public static PetActionRequest ToApplicationRequest(ApiPetActionRequest apiRequest)
    {
        return new PetActionRequest
        {
            Action = apiRequest.Action,
            Nutrition = apiRequest.Nutrition,
            Hydration = apiRequest.Hydration,
            HitsTaken = apiRequest.HitsTaken,
            OpponentDifficulty = apiRequest.OpponentDifficulty,
            Hours = apiRequest.Hours
        };
    }

    public static IntentDto ToApplicationDto(ApiIntentDto apiDto)
    {
        return new IntentDto
        {
            Primary = ParsePrimaryAction(apiDto.Primary),
            Movement = ParseMovementAction(apiDto.Movement),
            Stance = ParseStanceAction(apiDto.Stance),
            Cover = ParseCoverAction(apiDto.Cover),
            CancelMovement = apiDto.CancelMovement
        };
    }

    public static ApiCombatSessionDto ToApiDto(CombatSessionDto appDto)
    {
        return new ApiCombatSessionDto
        {
            Id = appDto.Id,
            OperatorId = appDto.OperatorId,
            Phase = appDto.Phase.ToString(),
            CurrentTimeMs = appDto.CurrentTimeMs,
            Player = ToApiDto(appDto.Player),
            Enemy = ToApiDto(appDto.Enemy),
            Pet = ToApiDto(appDto.Pet),
            EnemyLevel = appDto.EnemyLevel,
            TurnNumber = appDto.TurnNumber,
            BattleLog = appDto.BattleLog.Select(ToApiDto).ToList()
        };
    }

    public static ApiOperatorStateDto ToApiDto(OperatorStateDto appDto)
    {
        return new ApiOperatorStateDto
        {
            Id = appDto.Id,
            Name = appDto.Name,
            TotalXp = appDto.TotalXp,
            CurrentHealth = appDto.CurrentHealth,
            MaxHealth = appDto.MaxHealth,
            EquippedWeaponName = appDto.EquippedWeaponName,
            UnlockedPerks = appDto.UnlockedPerks,
            ExfilStreak = appDto.ExfilStreak,
            IsDead = appDto.IsDead,
            CurrentMode = appDto.CurrentMode.ToString(),
            InfilStartTime = appDto.InfilStartTime,
            ActiveSessionId = appDto.ActiveSessionId,
            ActiveCombatSession = appDto.ActiveCombatSession != null ? ToApiDto(appDto.ActiveCombatSession) : null,
            LockedLoadout = appDto.LockedLoadout,
            Pet = appDto.Pet != null ? ToApiDto(appDto.Pet) : null
        };
    }

    public static ApiPlayerStateDto ToApiDto(PlayerStateDto appDto)
    {
        return new ApiPlayerStateDto
        {
            Id = appDto.Id,
            Name = appDto.Name,
            Health = appDto.Health,
            MaxHealth = appDto.MaxHealth,
            Stamina = appDto.Stamina,
            Fatigue = appDto.Fatigue,
            SuppressionLevel = appDto.SuppressionLevel,
            IsSuppressed = appDto.IsSuppressed,
            DistanceToOpponent = appDto.DistanceToOpponent,
            CurrentAmmo = appDto.CurrentAmmo,
            MagazineSize = appDto.MagazineSize,
            AimState = appDto.AimState.ToString(),
            MovementState = appDto.MovementState.ToString(),
            CurrentMovement = appDto.CurrentMovement.ToString(),
            CurrentDirection = appDto.CurrentDirection.ToString(),
            CurrentCover = appDto.CurrentCover.ToString(),
            IsMoving = appDto.IsMoving,
            IsAlive = appDto.IsAlive
        };
    }

    public static ApiPetStateDto ToApiDto(PetStateDto appDto)
    {
        return new ApiPetStateDto
        {
            Health = appDto.Health,
            Fatigue = appDto.Fatigue,
            Injury = appDto.Injury,
            Stress = appDto.Stress,
            Morale = appDto.Morale,
            Hunger = appDto.Hunger,
            Hydration = appDto.Hydration,
            LastUpdated = appDto.LastUpdated
        };
    }

    public static ApiBattleLogEntryDto ToApiDto(BattleLogEntryDto appDto)
    {
        return new ApiBattleLogEntryDto
        {
            EventType = appDto.EventType,
            TimeMs = appDto.TimeMs,
            Message = appDto.Message,
            ActorName = appDto.ActorName
        };
    }

    public static ApiIntentSubmissionResultDto ToApiDto(IntentSubmissionResultDto appDto)
    {
        return new ApiIntentSubmissionResultDto
        {
            Accepted = appDto.Accepted,
            Error = appDto.Error,
            State = appDto.State != null ? ToApiDto(appDto.State) : null
        };
    }

    // Operator request mappings
    public static Application.Requests.OperatorCreateRequest ToApplicationRequest(ApiOperatorCreateRequest apiRequest)
    {
        return new Application.Requests.OperatorCreateRequest
        {
            Name = apiRequest.Name
        };
    }

    public static Application.Requests.ProcessOutcomeRequest ToApplicationRequest(ApiProcessOutcomeRequest apiRequest)
    {
        // Parse GearLost safely, filtering out any invalid IDs
        var gearLost = new List<Core.Equipment.GearId>();
        foreach (var id in apiRequest.GearLost ?? [])
        {
            if (!string.IsNullOrWhiteSpace(id))
            {
                try
                {
                    gearLost.Add(new Core.Equipment.GearId(id));
                }
                catch
                {
                    // Skip invalid gear IDs silently
                }
            }
        }

        return new Application.Requests.ProcessOutcomeRequest
        {
            SessionId = apiRequest.SessionId,
            OperatorDied = apiRequest.OperatorDied,
            XpGained = apiRequest.XpGained,
            IsVictory = apiRequest.IsVictory,
            GearLost = gearLost
        };
    }

    public static Application.Requests.ChangeLoadoutRequest ToApplicationRequest(ApiChangeLoadoutRequest apiRequest)
    {
        return new Application.Requests.ChangeLoadoutRequest
        {
            WeaponName = apiRequest.WeaponName
        };
    }

    public static Application.Requests.TreatWoundsRequest ToApplicationRequest(ApiTreatWoundsRequest apiRequest)
    {
        return new Application.Requests.TreatWoundsRequest
        {
            HealthAmount = apiRequest.HealthAmount
        };
    }

    public static Application.Requests.ApplyXpRequest ToApplicationRequest(ApiApplyXpRequest apiRequest)
    {
        return new Application.Requests.ApplyXpRequest
        {
            XpAmount = apiRequest.XpAmount,
            Reason = apiRequest.Reason
        };
    }

    public static Application.Requests.UnlockPerkRequest ToApplicationRequest(ApiUnlockPerkRequest apiRequest)
    {
        return new Application.Requests.UnlockPerkRequest
        {
            PerkName = apiRequest.PerkName
        };
    }

    private static Core.Intents.PrimaryAction? ParsePrimaryAction(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value.ToLowerInvariant() switch
        {
            "fire" => Core.Intents.PrimaryAction.Fire,
            "reload" => Core.Intents.PrimaryAction.Reload,
            "none" => Core.Intents.PrimaryAction.None,
            _ => null
        };
    }

    private static Core.Intents.MovementAction? ParseMovementAction(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value.ToLowerInvariant() switch
        {
            "stand" => Core.Intents.MovementAction.Stand,
            "walktoward" => Core.Intents.MovementAction.WalkToward,
            "walkaway" => Core.Intents.MovementAction.WalkAway,
            "sprinttoward" => Core.Intents.MovementAction.SprintToward,
            "sprintaway" => Core.Intents.MovementAction.SprintAway,
            "slidetoward" => Core.Intents.MovementAction.SlideToward,
            "slideaway" => Core.Intents.MovementAction.SlideAway,
            "crouch" => Core.Intents.MovementAction.Crouch,
            _ => null
        };
    }

    private static Core.Intents.StanceAction? ParseStanceAction(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value.ToLowerInvariant() switch
        {
            "enterads" => Core.Intents.StanceAction.EnterADS,
            "exitads" => Core.Intents.StanceAction.ExitADS,
            "none" => Core.Intents.StanceAction.None,
            _ => null
        };
    }

    private static Core.Intents.CoverAction? ParseCoverAction(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value.ToLowerInvariant() switch
        {
            "enterpartial" => Core.Intents.CoverAction.EnterPartial,
            "enterfull" => Core.Intents.CoverAction.EnterFull,
            "exit" => Core.Intents.CoverAction.Exit,
            "none" => Core.Intents.CoverAction.None,
            _ => null
        };
    }

    public static ApiOperatorSummaryDto ToApiDto(OperatorSummaryDto dto)
    {
        return new ApiOperatorSummaryDto
        {
            Id = dto.Id,
            Name = dto.Name,
            CurrentMode = dto.CurrentMode,
            IsDead = dto.IsDead,
            TotalXp = dto.TotalXp,
            CurrentHealth = dto.CurrentHealth,
            MaxHealth = dto.MaxHealth
        };
    }
}
