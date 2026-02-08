using GUNRPG.Application.Dtos;
using GUNRPG.Application.Sessions;
using GUNRPG.Core.AI;
using GUNRPG.Core.Intents;
using GUNRPG.Core.Operators;
using GUNRPG.Core.VirtualPet;
using GUNRPG.Core;
using GUNRPG.Core.Combat;
using GUNRPG.Core.Weapons;

namespace GUNRPG.Application.Mapping;

public static class SessionMapping
{
    public static CombatSessionDto ToDto(CombatSession session)
    {
        return new CombatSessionDto
        {
            Id = session.Id,
            Phase = session.Phase,
            CurrentTimeMs = session.Combat.CurrentTimeMs,
            Player = ToDto(session.Player),
            Enemy = ToDto(session.Enemy),
            Pet = ToDto(session.PetState),
            PlayerXp = session.PlayerXp,
            PlayerLevel = session.PlayerLevel,
            EnemyLevel = session.EnemyLevel,
            TurnNumber = session.TurnNumber
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

    public static CombatSessionSnapshot ToSnapshot(CombatSession session)
    {
        var (playerIntents, enemyIntents) = session.Combat.GetPendingIntents();
        var (randomSeed, randomCalls) = session.Combat.GetRandomState();

        return new CombatSessionSnapshot
        {
            Id = session.Id,
            Phase = session.Phase,
            TurnNumber = session.TurnNumber,
            PlayerXp = session.PlayerXp,
            PlayerLevel = session.PlayerLevel,
            EnemyLevel = session.EnemyLevel,
            Seed = session.Seed,
            PostCombatResolved = session.PostCombatResolved,
            CreatedAt = session.CreatedAt,
            CompletedAt = session.CompletedAt,
            Combat = new CombatStateSnapshot
            {
                Phase = session.Combat.Phase,
                CurrentTimeMs = session.Combat.CurrentTimeMs,
                PlayerIntents = ToSnapshot(playerIntents),
                EnemyIntents = ToSnapshot(enemyIntents),
                RandomState = new RandomStateSnapshot { Seed = randomSeed, CallCount = randomCalls }
            },
            Player = ToSnapshot(session.Player),
            Enemy = ToSnapshot(session.Enemy),
            Pet = ToSnapshot(session.PetState)
        };
    }

    public static CombatSession FromSnapshot(CombatSessionSnapshot snapshot)
    {
        var player = ToOperator(snapshot.Player);
        var enemy = ToOperator(snapshot.Enemy);

        var combat = CombatSystemV2.FromState(
            player,
            enemy,
            snapshot.Combat.Phase,
            snapshot.Combat.CurrentTimeMs,
            ToDomainIntent(snapshot.Combat.PlayerIntents),
            ToDomainIntent(snapshot.Combat.EnemyIntents),
            snapshot.Combat.RandomState.Seed,
            snapshot.Combat.RandomState.CallCount);

        var ai = new SimpleAIV2(seed: snapshot.Seed);
        var operatorManager = new OperatorManager();
        var petState = new PetState(
            snapshot.Pet.OperatorId,
            snapshot.Pet.Health,
            snapshot.Pet.Fatigue,
            snapshot.Pet.Injury,
            snapshot.Pet.Stress,
            snapshot.Pet.Morale,
            snapshot.Pet.Hunger,
            snapshot.Pet.Hydration,
            snapshot.Pet.LastUpdated);

        return new CombatSession(
            snapshot.Id,
            combat,
            ai,
            operatorManager,
            petState,
            snapshot.PlayerXp,
            snapshot.PlayerLevel,
            snapshot.EnemyLevel,
            snapshot.Seed,
            snapshot.Phase,
            snapshot.TurnNumber,
            snapshot.CreatedAt,
            snapshot.CompletedAt)
        {
            PostCombatResolved = snapshot.PostCombatResolved
        };
    }

    private static OperatorSnapshot ToSnapshot(Operator op)
    {
        return new OperatorSnapshot
        {
            Id = op.Id,
            Name = op.Name,
            Health = op.Health,
            MaxHealth = op.MaxHealth,
            Stamina = op.Stamina,
            MaxStamina = op.MaxStamina,
            Fatigue = op.Fatigue,
            MaxFatigue = op.MaxFatigue,
            MovementState = op.MovementState,
            AimState = op.AimState,
            WeaponState = op.WeaponState,
            CurrentMovement = op.CurrentMovement,
            CurrentCover = op.CurrentCover,
            CurrentDirection = op.CurrentDirection,
            MovementEndTimeMs = op.MovementEndTimeMs,
            EquippedWeaponName = op.EquippedWeapon?.Name,
            CurrentAmmo = op.CurrentAmmo,
            DistanceToOpponent = op.DistanceToOpponent,
            LastDamageTimeMs = op.LastDamageTimeMs,
            HealthRegenDelayMs = op.HealthRegenDelayMs,
            HealthRegenRate = op.HealthRegenRate,
            StaminaRegenRate = op.StaminaRegenRate,
            SprintStaminaDrainRate = op.SprintStaminaDrainRate,
            SlideStaminaCost = op.SlideStaminaCost,
            WalkSpeed = op.WalkSpeed,
            SprintSpeed = op.SprintSpeed,
            SlideDistance = op.SlideDistance,
            SlideDurationMs = op.SlideDurationMs,
            CurrentRecoilX = op.CurrentRecoilX,
            CurrentRecoilY = op.CurrentRecoilY,
            RecoilRecoveryStartMs = op.RecoilRecoveryStartMs,
            RecoilRecoveryRate = op.RecoilRecoveryRate,
            Accuracy = op.Accuracy,
            AccuracyProficiency = op.AccuracyProficiency,
            ResponseProficiency = op.ResponseProficiency,
            ShotsFiredCount = op.ShotsFiredCount,
            BulletsFiredSinceLastReaction = op.BulletsFiredSinceLastReaction,
            MetersMovedSinceLastReaction = op.MetersMovedSinceLastReaction,
            ADSTransitionStartMs = op.ADSTransitionStartMs,
            ADSTransitionDurationMs = op.ADSTransitionDurationMs,
            IsActivelyFiring = op.IsActivelyFiring,
            IsCoverTransitioning = op.IsCoverTransitioning,
            CoverTransitionFromState = op.CoverTransitionFromState,
            CoverTransitionToState = op.CoverTransitionToState,
            CoverTransitionStartMs = op.CoverTransitionStartMs,
            CoverTransitionEndMs = op.CoverTransitionEndMs,
            SuppressionLevel = op.SuppressionLevel,
            SuppressionDecayStartMs = op.SuppressionDecayStartMs,
            LastSuppressionApplicationMs = op.LastSuppressionApplicationMs,
            FlinchSeverity = op.FlinchSeverity,
            FlinchShotsRemaining = op.FlinchShotsRemaining,
            FlinchDurationShots = op.FlinchDurationShots,
            RecognitionDelayEndMs = op.RecognitionDelayEndMs,
            RecognitionTargetId = op.RecognitionTargetId,
            LastTargetVisibleMs = op.LastTargetVisibleMs,
            RecognitionStartMs = op.RecognitionStartMs,
            WasTargetVisible = op.WasTargetVisible
        };
    }

    private static Operator ToOperator(OperatorSnapshot snapshot)
    {
        var op = new Operator(snapshot.Name, snapshot.Id)
        {
            Health = snapshot.Health,
            MaxHealth = snapshot.MaxHealth,
            Stamina = snapshot.Stamina,
            MaxStamina = snapshot.MaxStamina,
            Fatigue = snapshot.Fatigue,
            MaxFatigue = snapshot.MaxFatigue,
            MovementState = snapshot.MovementState,
            AimState = snapshot.AimState,
            WeaponState = snapshot.WeaponState,
            CurrentMovement = snapshot.CurrentMovement,
            CurrentCover = snapshot.CurrentCover,
            CurrentDirection = snapshot.CurrentDirection,
            MovementEndTimeMs = snapshot.MovementEndTimeMs,
            CurrentAmmo = snapshot.CurrentAmmo,
            DistanceToOpponent = snapshot.DistanceToOpponent,
            LastDamageTimeMs = snapshot.LastDamageTimeMs,
            HealthRegenDelayMs = snapshot.HealthRegenDelayMs,
            HealthRegenRate = snapshot.HealthRegenRate,
            StaminaRegenRate = snapshot.StaminaRegenRate,
            SprintStaminaDrainRate = snapshot.SprintStaminaDrainRate,
            SlideStaminaCost = snapshot.SlideStaminaCost,
            WalkSpeed = snapshot.WalkSpeed,
            SprintSpeed = snapshot.SprintSpeed,
            SlideDistance = snapshot.SlideDistance,
            SlideDurationMs = snapshot.SlideDurationMs,
            CurrentRecoilX = snapshot.CurrentRecoilX,
            CurrentRecoilY = snapshot.CurrentRecoilY,
            RecoilRecoveryStartMs = snapshot.RecoilRecoveryStartMs,
            RecoilRecoveryRate = snapshot.RecoilRecoveryRate,
            Accuracy = snapshot.Accuracy,
            AccuracyProficiency = snapshot.AccuracyProficiency,
            ResponseProficiency = snapshot.ResponseProficiency,
            BulletsFiredSinceLastReaction = snapshot.BulletsFiredSinceLastReaction,
            MetersMovedSinceLastReaction = snapshot.MetersMovedSinceLastReaction,
            ADSTransitionStartMs = snapshot.ADSTransitionStartMs,
            ADSTransitionDurationMs = snapshot.ADSTransitionDurationMs,
            IsActivelyFiring = snapshot.IsActivelyFiring,
            IsCoverTransitioning = snapshot.IsCoverTransitioning,
            CoverTransitionFromState = snapshot.CoverTransitionFromState,
            CoverTransitionToState = snapshot.CoverTransitionToState,
            CoverTransitionStartMs = snapshot.CoverTransitionStartMs,
            CoverTransitionEndMs = snapshot.CoverTransitionEndMs,
            RecognitionDelayEndMs = snapshot.RecognitionDelayEndMs,
            RecognitionTargetId = snapshot.RecognitionTargetId,
            LastTargetVisibleMs = snapshot.LastTargetVisibleMs,
            RecognitionStartMs = snapshot.RecognitionStartMs,
            WasTargetVisible = snapshot.WasTargetVisible
        };

        var weapon = CreateWeapon(snapshot.EquippedWeaponName);
        if (weapon != null)
        {
            op.EquippedWeapon = weapon;
            op.CurrentAmmo = snapshot.CurrentAmmo;
        }

        op.RestoreSuppression(snapshot.SuppressionLevel, snapshot.LastSuppressionApplicationMs, snapshot.SuppressionDecayStartMs);
        op.RestoreFlinch(snapshot.FlinchSeverity, snapshot.FlinchShotsRemaining, snapshot.FlinchDurationShots);
        op.RestoreShotsFired(snapshot.ShotsFiredCount);
        return op;
    }

    private static IntentSnapshot? ToSnapshot(SimultaneousIntents? intents)
    {
        if (intents == null)
            return null;

        return new IntentSnapshot
        {
            OperatorId = intents.OperatorId,
            Primary = intents.Primary,
            Movement = intents.Movement,
            Stance = intents.Stance,
            Cover = intents.Cover,
            CancelMovement = intents.CancelMovement,
            SubmittedAtMs = intents.SubmittedAtMs
        };
    }

    private static SimultaneousIntents? ToDomainIntent(IntentSnapshot? snapshot)
    {
        if (snapshot == null)
            return null;

        return new SimultaneousIntents(snapshot.OperatorId)
        {
            Primary = snapshot.Primary,
            Movement = snapshot.Movement,
            Stance = snapshot.Stance,
            Cover = snapshot.Cover,
            CancelMovement = snapshot.CancelMovement,
            SubmittedAtMs = snapshot.SubmittedAtMs
        };
    }

    private static PetStateSnapshot ToSnapshot(PetState pet)
    {
        return new PetStateSnapshot
        {
            OperatorId = pet.OperatorId,
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

    private static Weapon? CreateWeapon(string? name)
    {
        return name switch
        {
            "SOKOL 545" => WeaponFactory.CreateSokol545(),
            "STURMWOLF 45" => WeaponFactory.CreateSturmwolf45(),
            "M15 MOD 0" => WeaponFactory.CreateM15Mod0(),
            null or "" => null,
            _ => new Weapon(name)
        };
    }
}
