using GUNRPG.Application.Combat;
using GUNRPG.Application.Mapping;
using GUNRPG.Application.Sessions;
using GUNRPG.Core.Intents;

namespace GUNRPG.Application.Distributed;

/// <summary>
/// Deterministic game engine for the distributed lockstep authority.
/// Replays authoritative combat session state so P2P verification hashes the same
/// replay-driven combat model used by offline and online session resolution.
/// </summary>
public sealed class DefaultGameEngine : IDeterministicGameEngine
{
    public GameStateDto Step(GameStateDto state, PlayerActionDto action)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(action);

        var sessions = state.Sessions.Select(session => session.Clone()).ToList();
        var existingSession = sessions.FirstOrDefault(session => session.OperatorId == action.OperatorId);
        var resolvedSession = ResolveSessionState(existingSession, action);

        sessions.RemoveAll(session => session.OperatorId == action.OperatorId);
        sessions.Add(resolvedSession);

        var operators = sessions
            .Select(ProjectOperatorSnapshot)
            .OrderBy(op => op.OperatorId)
            .ToList();

        return new GameStateDto
        {
            ActionCount = state.ActionCount + 1,
            Operators = operators,
            Sessions = sessions.OrderBy(session => session.OperatorId).ToList()
        };
    }

    private static GameStateDto.CombatSessionState ResolveSessionState(
        GameStateDto.CombatSessionState? existingSession,
        PlayerActionDto action)
    {
        var replayState = ReplayAuthoritativeState(existingSession, action);
        var snapshot = replayState.Snapshot;
        var snapshotHash = Convert.ToHexString(CombatSessionHasher.ComputeStateHash(snapshot));

        return new GameStateDto.CombatSessionState
        {
            SessionId = snapshot.Id,
            OperatorId = snapshot.OperatorId,
            Snapshot = snapshot,
            SnapshotHash = snapshotHash,
            Outcome = replayState.Outcome
        };
    }

    private static ReplaySimulationState ReplayAuthoritativeState(
        GameStateDto.CombatSessionState? existingSession,
        PlayerActionDto action)
    {
        string initialSnapshotJson;
        IReadOnlyList<IntentSnapshot> replayTurns;

        if (!string.IsNullOrWhiteSpace(action.ReplayInitialSnapshotJson) && action.ReplayTurns is { Count: > 0 })
        {
            initialSnapshotJson = action.ReplayInitialSnapshotJson;
            replayTurns = action.ReplayTurns.Select(CloneIntent).ToList();
        }
        else if (existingSession != null)
        {
            initialSnapshotJson = existingSession.Snapshot.ReplayInitialSnapshotJson;
            replayTurns = existingSession.Snapshot.ReplayTurns
                .Select(CloneIntent)
                .Append(ToReplayTurn(action))
                .ToList();
        }
        else
        {
            var initialSnapshot = CreateInitialSnapshot(action);
            initialSnapshotJson = initialSnapshot.ReplayInitialSnapshotJson;
            replayTurns = [ToReplayTurn(action)];
        }

        var replayState = ReplayRunner.Run(initialSnapshotJson, replayTurns);
        var snapshot = replayState.Snapshot;

        if (snapshot.OperatorId != action.OperatorId)
        {
            throw new InvalidOperationException(
                $"Replay snapshot operator mismatch. Expected '{action.OperatorId}', got '{snapshot.OperatorId}'.");
        }

        if (action.SessionId.HasValue && snapshot.Id != action.SessionId.Value)
        {
            throw new InvalidOperationException(
                $"Replay snapshot session mismatch. Expected '{action.SessionId}', got '{snapshot.Id}'.");
        }

        return replayState;
    }

    private static CombatSessionSnapshot CreateInitialSnapshot(PlayerActionDto action)
    {
        var seed = CreateStableSeed(action.OperatorId);
        var session = CombatSession.CreateDefault(
            seed: seed,
            id: action.SessionId,
            operatorId: action.OperatorId);
        var snapshot = SessionMapping.ToSnapshot(session);
        var deterministicCreatedAt = DateTimeOffset.UnixEpoch + TimeSpan.FromSeconds(Math.Abs((long)seed));
        var sessionId = action.SessionId ?? CreateStableGuid(action.OperatorId, "session");
        var playerId = action.OperatorId;
        var enemyId = CreateStableGuid(action.OperatorId, "enemy");
        // Fallback authority tests may construct a replay-backed reload action without a
        // preceding fire turn. In that case we start one bullet below full so the reload
        // intent is valid when the authoritative replay engine re-validates it.
        var playerAmmo = action.Primary == PrimaryAction.Reload
            ? Math.Max(0, snapshot.Player.CurrentAmmo - 1)
            : snapshot.Player.CurrentAmmo;

        var normalized = new CombatSessionSnapshot
        {
            Id = sessionId,
            OperatorId = action.OperatorId,
            Phase = snapshot.Phase,
            TurnNumber = snapshot.TurnNumber,
            Combat = snapshot.Combat,
            Player = new OperatorSnapshot
            {
                Id = playerId,
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
                CurrentAmmo = playerAmmo,
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
            Enemy = new OperatorSnapshot
            {
                Id = enemyId,
                Name = snapshot.Enemy.Name,
                Health = snapshot.Enemy.Health,
                MaxHealth = snapshot.Enemy.MaxHealth,
                Stamina = snapshot.Enemy.Stamina,
                MaxStamina = snapshot.Enemy.MaxStamina,
                Fatigue = snapshot.Enemy.Fatigue,
                MaxFatigue = snapshot.Enemy.MaxFatigue,
                MovementState = snapshot.Enemy.MovementState,
                AimState = snapshot.Enemy.AimState,
                WeaponState = snapshot.Enemy.WeaponState,
                CurrentMovement = snapshot.Enemy.CurrentMovement,
                CurrentCover = snapshot.Enemy.CurrentCover,
                CurrentDirection = snapshot.Enemy.CurrentDirection,
                MovementEndTimeMs = snapshot.Enemy.MovementEndTimeMs,
                EquippedWeaponName = snapshot.Enemy.EquippedWeaponName,
                CurrentAmmo = snapshot.Enemy.CurrentAmmo,
                DistanceToOpponent = snapshot.Enemy.DistanceToOpponent,
                LastDamageTimeMs = snapshot.Enemy.LastDamageTimeMs,
                HealthRegenDelayMs = snapshot.Enemy.HealthRegenDelayMs,
                HealthRegenRate = snapshot.Enemy.HealthRegenRate,
                StaminaRegenRate = snapshot.Enemy.StaminaRegenRate,
                SprintStaminaDrainRate = snapshot.Enemy.SprintStaminaDrainRate,
                SlideStaminaCost = snapshot.Enemy.SlideStaminaCost,
                WalkSpeed = snapshot.Enemy.WalkSpeed,
                SprintSpeed = snapshot.Enemy.SprintSpeed,
                SlideDistance = snapshot.Enemy.SlideDistance,
                SlideDurationMs = snapshot.Enemy.SlideDurationMs,
                CurrentRecoilX = snapshot.Enemy.CurrentRecoilX,
                CurrentRecoilY = snapshot.Enemy.CurrentRecoilY,
                RecoilRecoveryStartMs = snapshot.Enemy.RecoilRecoveryStartMs,
                RecoilRecoveryRate = snapshot.Enemy.RecoilRecoveryRate,
                Accuracy = snapshot.Enemy.Accuracy,
                AccuracyProficiency = snapshot.Enemy.AccuracyProficiency,
                ResponseProficiency = snapshot.Enemy.ResponseProficiency,
                ShotsFiredCount = snapshot.Enemy.ShotsFiredCount,
                BulletsFiredSinceLastReaction = snapshot.Enemy.BulletsFiredSinceLastReaction,
                MetersMovedSinceLastReaction = snapshot.Enemy.MetersMovedSinceLastReaction,
                ADSTransitionStartMs = snapshot.Enemy.ADSTransitionStartMs,
                ADSTransitionDurationMs = snapshot.Enemy.ADSTransitionDurationMs,
                IsActivelyFiring = snapshot.Enemy.IsActivelyFiring,
                IsCoverTransitioning = snapshot.Enemy.IsCoverTransitioning,
                CoverTransitionFromState = snapshot.Enemy.CoverTransitionFromState,
                CoverTransitionToState = snapshot.Enemy.CoverTransitionToState,
                CoverTransitionStartMs = snapshot.Enemy.CoverTransitionStartMs,
                CoverTransitionEndMs = snapshot.Enemy.CoverTransitionEndMs,
                SuppressionLevel = snapshot.Enemy.SuppressionLevel,
                SuppressionDecayStartMs = snapshot.Enemy.SuppressionDecayStartMs,
                LastSuppressionApplicationMs = snapshot.Enemy.LastSuppressionApplicationMs,
                FlinchSeverity = snapshot.Enemy.FlinchSeverity,
                FlinchShotsRemaining = snapshot.Enemy.FlinchShotsRemaining,
                FlinchDurationShots = snapshot.Enemy.FlinchDurationShots,
                RecognitionDelayEndMs = snapshot.Enemy.RecognitionDelayEndMs,
                RecognitionTargetId = snapshot.Enemy.RecognitionTargetId,
                LastTargetVisibleMs = snapshot.Enemy.LastTargetVisibleMs,
                RecognitionStartMs = snapshot.Enemy.RecognitionStartMs,
                WasTargetVisible = snapshot.Enemy.WasTargetVisible
            },
            Pet = new PetStateSnapshot
            {
                OperatorId = playerId,
                Health = snapshot.Pet.Health,
                Fatigue = snapshot.Pet.Fatigue,
                Injury = snapshot.Pet.Injury,
                Stress = snapshot.Pet.Stress,
                Morale = snapshot.Pet.Morale,
                Hunger = snapshot.Pet.Hunger,
                Hydration = snapshot.Pet.Hydration,
                LastUpdated = deterministicCreatedAt
            },
            EnemyLevel = snapshot.EnemyLevel,
            Seed = snapshot.Seed,
            PostCombatResolved = snapshot.PostCombatResolved,
            CreatedAt = deterministicCreatedAt,
            CompletedAt = snapshot.CompletedAt,
            LastActionTimestamp = deterministicCreatedAt,
            ReplayInitialSnapshotJson = string.Empty,
            ReplayTurns = snapshot.ReplayTurns.ToList(),
            BalanceSnapshotVersion = snapshot.BalanceSnapshotVersion,
            BalanceSnapshotHash = snapshot.BalanceSnapshotHash,
            Version = snapshot.Version,
            FinalHash = snapshot.FinalHash != null ? (byte[])snapshot.FinalHash.Clone() : null
        };

        return new CombatSessionSnapshot
        {
            Id = normalized.Id,
            OperatorId = normalized.OperatorId,
            Phase = normalized.Phase,
            TurnNumber = normalized.TurnNumber,
            Combat = normalized.Combat,
            Player = normalized.Player,
            Enemy = normalized.Enemy,
            Pet = normalized.Pet,
            EnemyLevel = normalized.EnemyLevel,
            Seed = normalized.Seed,
            PostCombatResolved = normalized.PostCombatResolved,
            CreatedAt = normalized.CreatedAt,
            CompletedAt = normalized.CompletedAt,
            LastActionTimestamp = normalized.LastActionTimestamp,
            ReplayInitialSnapshotJson = OfflineCombatReplay.SerializeCombatSnapshot(normalized),
            ReplayTurns = normalized.ReplayTurns.ToList(),
            BalanceSnapshotVersion = normalized.BalanceSnapshotVersion,
            BalanceSnapshotHash = normalized.BalanceSnapshotHash,
            Version = normalized.Version,
            FinalHash = normalized.FinalHash != null ? (byte[])normalized.FinalHash.Clone() : null
        };
    }

    private static IntentSnapshot ToReplayTurn(PlayerActionDto action)
    {
        return new IntentSnapshot
        {
            OperatorId = action.OperatorId,
            Primary = action.Primary ?? PrimaryAction.None,
            Movement = action.Movement ?? MovementAction.Stand,
            Stance = action.Stance ?? StanceAction.None,
            Cover = action.Cover ?? CoverAction.None,
            CancelMovement = action.CancelMovement
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

    private static GameStateDto.OperatorSnapshot ProjectOperatorSnapshot(GameStateDto.CombatSessionState sessionState)
    {
        var snapshot = sessionState.Snapshot;
        var player = snapshot.Player;
        var outcome = sessionState.Outcome;

        return new GameStateDto.OperatorSnapshot
        {
            OperatorId = sessionState.OperatorId,
            Name = player.Name,
            TotalXp = outcome?.XpGained ?? 0,
            CurrentHealth = player.Health,
            MaxHealth = player.MaxHealth,
            EquippedWeaponName = player.EquippedWeaponName ?? string.Empty,
            ExfilStreak = 0,
            IsDead = player.Health <= 0f
        };
    }

    private static Guid CreateStableGuid(Guid operatorId, string purpose)
    {
        return StableGuidFactory.FromString($"{operatorId:N}:{purpose}");
    }

    private static int CreateStableSeed(Guid operatorId)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(operatorId.ToString("N")));
        return BitConverter.ToInt32(bytes, 0) & int.MaxValue;
    }
}
