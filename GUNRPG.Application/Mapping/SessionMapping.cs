using GUNRPG.Application.Dtos;
using GUNRPG.Application.Sessions;
using GUNRPG.Core.Intents;
using GUNRPG.Core.Operators;
using GUNRPG.Core.VirtualPet;

namespace GUNRPG.Application.Mapping;

public static class SessionMapping
{
    public static CombatSessionDto ToDto(CombatSession session)
    {
        return new CombatSessionDto
        {
            Id = session.Id,
            Phase = session.Combat.Phase,
            CurrentTimeMs = session.Combat.CurrentTimeMs,
            Player = ToDto(session.Player),
            Enemy = ToDto(session.Enemy),
            Pet = ToDto(session.PetState),
            PlayerXp = session.PlayerXp,
            PlayerLevel = session.PlayerLevel,
            EnemyLevel = session.EnemyLevel
        };
    }

    public static OperatorStateDto ToDto(Operator op)
    {
        return new OperatorStateDto
        {
            Id = op.Id,
            Name = op.Name,
            Health = op.Health,
            MaxHealth = op.MaxHealth,
            Stamina = op.Stamina,
            Fatigue = op.Fatigue,
            SuppressionLevel = op.SuppressionLevel,
            IsSuppressed = op.IsSuppressed,
            DistanceToOpponent = op.DistanceToOpponent,
            CurrentAmmo = op.CurrentAmmo,
            MagazineSize = op.EquippedWeapon?.MagazineSize,
            AimState = op.AimState,
            MovementState = op.MovementState,
            CurrentMovement = op.CurrentMovement,
            CurrentDirection = op.CurrentDirection,
            CurrentCover = op.CurrentCover,
            IsMoving = op.IsMoving,
            IsAlive = op.IsAlive
        };
    }

    public static PetStateDto ToDto(PetState pet)
    {
        return new PetStateDto
        {
            Health = pet.Health,
            Fatigue = pet.Fatigue,
            Injury = pet.Injury,
            Stress = pet.Stress,
            Morale = pet.Morale,
            Hunger = pet.Hunger,
            Hydration = pet.Hydration,
            LastUpdated = pet.LastUpdated
        };
    }

    public static SimultaneousIntents ToDomainIntent(Guid operatorId, IntentDto dto)
    {
        return new SimultaneousIntents(operatorId)
        {
            Primary = dto.Primary ?? PrimaryAction.None,
            Movement = dto.Movement ?? MovementAction.Stand,
            Stance = dto.Stance ?? StanceAction.None,
            Cover = dto.Cover ?? CoverAction.None,
            CancelMovement = dto.CancelMovement
        };
    }
}
