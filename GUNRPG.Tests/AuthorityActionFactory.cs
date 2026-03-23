using GUNRPG.Application.Combat;
using GUNRPG.Application.Distributed;
using GUNRPG.Application.Mapping;
using GUNRPG.Application.Sessions;
using GUNRPG.Core.Intents;

namespace GUNRPG.Tests;

internal static class AuthorityActionFactory
{
    public static PlayerActionDto CreateReplayBackedAction(Guid operatorId, PrimaryAction primary, int seed = 42)
    {
        return CreateReplayBackedActions(operatorId, seed, primary).Single();
    }

    public static IReadOnlyList<PlayerActionDto> CreateReplayBackedActions(Guid operatorId, int seed = 42, params PrimaryAction[] primaries)
    {
        var session = CombatSession.CreateDefault(seed: seed, operatorId: operatorId);
        var snapshot = SessionMapping.ToSnapshot(session);
        // When the very first authoritative turn is Reload, prime the snapshot with one
        // bullet missing so the replay engine accepts the reload intent as valid.
        if (primaries.FirstOrDefault() == PrimaryAction.Reload)
        {
            snapshot = WithPlayerAmmo(snapshot, Math.Max(0, snapshot.Player.CurrentAmmo - 1));
        }

        var initialJson = OfflineCombatReplay.SerializeCombatSnapshot(snapshot);
        var turns = new List<IntentSnapshot>();
        var actions = new List<PlayerActionDto>();

        foreach (var primary in primaries)
        {
            turns.Add(new IntentSnapshot
            {
                OperatorId = operatorId,
                Primary = primary,
                Movement = MovementAction.Stand,
                Stance = StanceAction.None,
                Cover = CoverAction.None
            });

            actions.Add(new PlayerActionDto
            {
                OperatorId = operatorId,
                SessionId = snapshot.Id,
                Primary = primary,
                ReplayInitialSnapshotJson = initialJson,
                ReplayTurns = turns.Select(CloneIntent).ToList()
            });
        }

        return actions;
    }

    private static CombatSessionSnapshot WithPlayerAmmo(CombatSessionSnapshot snapshot, int ammo)
    {
        return new CombatSessionSnapshot
        {
            Id = snapshot.Id,
            OperatorId = snapshot.OperatorId,
            Phase = snapshot.Phase,
            TurnNumber = snapshot.TurnNumber,
            Combat = snapshot.Combat,
            Player = new OperatorSnapshot
            {
                Id = snapshot.Player.Id,
                Name = snapshot.Player.Name,
                Health = snapshot.Player.Health,
                MaxHealth = snapshot.Player.MaxHealth,
                Stamina = snapshot.Player.Stamina,
                MaxStamina = snapshot.Player.MaxStamina,
                Fatigue = snapshot.Player.Fatigue,
                MaxFatigue = snapshot.Player.MaxFatigue,
                MovementState = snapshot.Player.MovementState,
                AimState = snapshot.Player.AimState,
                WeaponState = snapshot.Player.WeaponState,
                CurrentMovement = snapshot.Player.CurrentMovement,
                CurrentCover = snapshot.Player.CurrentCover,
                CurrentDirection = snapshot.Player.CurrentDirection,
                MovementEndTimeMs = snapshot.Player.MovementEndTimeMs,
                EquippedWeaponName = snapshot.Player.EquippedWeaponName,
                CurrentAmmo = ammo,
                DistanceToOpponent = snapshot.Player.DistanceToOpponent,
                LastDamageTimeMs = snapshot.Player.LastDamageTimeMs,
                HealthRegenDelayMs = snapshot.Player.HealthRegenDelayMs,
                HealthRegenRate = snapshot.Player.HealthRegenRate,
                StaminaRegenRate = snapshot.Player.StaminaRegenRate,
                SprintStaminaDrainRate = snapshot.Player.SprintStaminaDrainRate,
                SlideStaminaCost = snapshot.Player.SlideStaminaCost,
                WalkSpeed = snapshot.Player.WalkSpeed,
                SprintSpeed = snapshot.Player.SprintSpeed,
                SlideDistance = snapshot.Player.SlideDistance,
                SlideDurationMs = snapshot.Player.SlideDurationMs,
                CurrentRecoilX = snapshot.Player.CurrentRecoilX,
                CurrentRecoilY = snapshot.Player.CurrentRecoilY,
                RecoilRecoveryStartMs = snapshot.Player.RecoilRecoveryStartMs,
                RecoilRecoveryRate = snapshot.Player.RecoilRecoveryRate,
                Accuracy = snapshot.Player.Accuracy,
                AccuracyProficiency = snapshot.Player.AccuracyProficiency,
                ResponseProficiency = snapshot.Player.ResponseProficiency,
                ShotsFiredCount = snapshot.Player.ShotsFiredCount,
                BulletsFiredSinceLastReaction = snapshot.Player.BulletsFiredSinceLastReaction,
                MetersMovedSinceLastReaction = snapshot.Player.MetersMovedSinceLastReaction,
                ADSTransitionStartMs = snapshot.Player.ADSTransitionStartMs,
                ADSTransitionDurationMs = snapshot.Player.ADSTransitionDurationMs,
                IsActivelyFiring = snapshot.Player.IsActivelyFiring,
                IsCoverTransitioning = snapshot.Player.IsCoverTransitioning,
                CoverTransitionFromState = snapshot.Player.CoverTransitionFromState,
                CoverTransitionToState = snapshot.Player.CoverTransitionToState,
                CoverTransitionStartMs = snapshot.Player.CoverTransitionStartMs,
                CoverTransitionEndMs = snapshot.Player.CoverTransitionEndMs,
                SuppressionLevel = snapshot.Player.SuppressionLevel,
                SuppressionDecayStartMs = snapshot.Player.SuppressionDecayStartMs,
                LastSuppressionApplicationMs = snapshot.Player.LastSuppressionApplicationMs,
                FlinchSeverity = snapshot.Player.FlinchSeverity,
                FlinchShotsRemaining = snapshot.Player.FlinchShotsRemaining,
                FlinchDurationShots = snapshot.Player.FlinchDurationShots,
                RecognitionDelayEndMs = snapshot.Player.RecognitionDelayEndMs,
                RecognitionTargetId = snapshot.Player.RecognitionTargetId,
                LastTargetVisibleMs = snapshot.Player.LastTargetVisibleMs,
                RecognitionStartMs = snapshot.Player.RecognitionStartMs,
                WasTargetVisible = snapshot.Player.WasTargetVisible
            },
            Enemy = snapshot.Enemy,
            Pet = snapshot.Pet,
            EnemyLevel = snapshot.EnemyLevel,
            Seed = snapshot.Seed,
            PostCombatResolved = snapshot.PostCombatResolved,
            CreatedAt = snapshot.CreatedAt,
            CompletedAt = snapshot.CompletedAt,
            LastActionTimestamp = snapshot.LastActionTimestamp,
            ReplayInitialSnapshotJson = snapshot.ReplayInitialSnapshotJson,
            ReplayTurns = snapshot.ReplayTurns.ToList(),
            Version = snapshot.Version,
            FinalHash = snapshot.FinalHash != null ? (byte[])snapshot.FinalHash.Clone() : null
        };
    }

    private static IntentSnapshot CloneIntent(IntentSnapshot snapshot)
    {
        return new IntentSnapshot
        {
            OperatorId = snapshot.OperatorId,
            Primary = snapshot.Primary,
            Movement = snapshot.Movement,
            Stance = snapshot.Stance,
            Cover = snapshot.Cover,
            CancelMovement = snapshot.CancelMovement,
            SubmittedAtMs = snapshot.SubmittedAtMs
        };
    }
}
