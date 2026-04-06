using GUNRPG.Core.Weapons;

namespace GUNRPG.Core;

/// <summary>
/// Factory for creating pre-configured weapons.
/// Runtime stats are loaded from the committed balance snapshot embedded in the assembly.
/// </summary>
public static class WeaponFactory
{
    private static readonly LegacyWeaponProfile Sokol545Profile = new(
        DisplayName: "SOKOL 545",
        SnapshotName: "Sokol 545",
        HeadshotMultiplier: 1.2f,
        HipfireSpreadDegrees: 9.50f,
        JumpHipfireSpreadDegrees: 14.50f,
        SlideHipfireSpreadDegrees: 16.50f,
        DiveHipfireSpreadDegrees: 16.50f,
        ADSSpreadDegrees: 1.0f,
        VerticalRecoil: 0.5627f,
        HorizontalRecoil: 0.1292f,
        RecoilRecoveryTimeMs: 680f,
        FirstShotRecoilScale: 1.08f,
        RecoilGunKickDegreesPerSecond: 24.92f,
        HorizontalRecoilControlDegreesPerSecond: 12.92f,
        VerticalRecoilControlDegreesPerSecond: 56.27f,
        KickResetSpeedMs: 680,
        AimingIdleSwayDegreesPerSecond: 8.53f,
        AimingIdleSwayDelayMs: 2200,
        FlinchResistance: 0.10f,
        JumpADSTimeMs: 552,
        CrouchMovementSpeedMetersPerSecond: 2.0f,
        SprintingMovementSpeedMetersPerSecond: 6.6f,
        SlideToFireTimeMs: 470f,
        DiveToFireTimeMs: 550f,
        JumpSprintToFireTimeMs: 329f,
        BulletsPerCommitmentUnit: 3,
        SuppressionFactor: 1.5f);

    private static readonly LegacyWeaponProfile Sturmwolf45Profile = new(
        DisplayName: "STURMWOLF 45",
        SnapshotName: "Sturmwolf 45",
        HeadshotMultiplier: 1.2f,
        HipfireSpreadDegrees: 7.25f,
        JumpHipfireSpreadDegrees: 12.00f,
        SlideHipfireSpreadDegrees: 15.00f,
        DiveHipfireSpreadDegrees: 15.00f,
        ADSSpreadDegrees: 1.0f,
        VerticalRecoil: 0.4596f,
        HorizontalRecoil: 0.1311f,
        RecoilRecoveryTimeMs: 600f,
        FirstShotRecoilScale: 1.00f,
        RecoilGunKickDegreesPerSecond: 44.86f,
        HorizontalRecoilControlDegreesPerSecond: 13.11f,
        VerticalRecoilControlDegreesPerSecond: 45.96f,
        KickResetSpeedMs: 600,
        AimingIdleSwayDegreesPerSecond: 0.39f,
        AimingIdleSwayDelayMs: 2200,
        FlinchResistance: 0.12f,
        JumpADSTimeMs: 291,
        CrouchMovementSpeedMetersPerSecond: 2.7f,
        SprintingMovementSpeedMetersPerSecond: 7.1f,
        SlideToFireTimeMs: 340f,
        DiveToFireTimeMs: 420f,
        JumpSprintToFireTimeMs: 210f,
        BulletsPerCommitmentUnit: 4,
        SuppressionFactor: 0.8f);

    private static readonly LegacyWeaponProfile M15Mod0Profile = new(
        DisplayName: "M15 MOD 0",
        SnapshotName: "M15 Mod 0",
        HeadshotMultiplier: 1.3f,
        HipfireSpreadDegrees: 7.95f,
        JumpHipfireSpreadDegrees: 11.95f,
        SlideHipfireSpreadDegrees: 14.95f,
        DiveHipfireSpreadDegrees: 14.95f,
        ADSSpreadDegrees: 1.0f,
        VerticalRecoil: 0.4667f,
        HorizontalRecoil: 0.1111f,
        RecoilRecoveryTimeMs: 600f,
        FirstShotRecoilScale: 1.00f,
        RecoilGunKickDegreesPerSecond: 15.50f,
        HorizontalRecoilControlDegreesPerSecond: 11.11f,
        VerticalRecoilControlDegreesPerSecond: 46.67f,
        KickResetSpeedMs: 600,
        AimingIdleSwayDegreesPerSecond: 0.42f,
        AimingIdleSwayDelayMs: 2200,
        FlinchResistance: 0.10f,
        JumpADSTimeMs: 333,
        CrouchMovementSpeedMetersPerSecond: 2.6f,
        SprintingMovementSpeedMetersPerSecond: 6.8f,
        SlideToFireTimeMs: 430f,
        DiveToFireTimeMs: 400f,
        JumpSprintToFireTimeMs: 385f,
        BulletsPerCommitmentUnit: 3,
        SuppressionFactor: 1.0f);

    private static readonly IReadOnlyDictionary<string, LegacyWeaponProfile> LegacyProfiles =
        new Dictionary<string, LegacyWeaponProfile>(StringComparer.OrdinalIgnoreCase)
        {
            [Sokol545Profile.DisplayName] = Sokol545Profile,
            [Sokol545Profile.SnapshotName] = Sokol545Profile,
            ["LMG"] = Sokol545Profile,
            [Sturmwolf45Profile.DisplayName] = Sturmwolf45Profile,
            [Sturmwolf45Profile.SnapshotName] = Sturmwolf45Profile,
            ["SMG"] = Sturmwolf45Profile,
            [M15Mod0Profile.DisplayName] = M15Mod0Profile,
            [M15Mod0Profile.SnapshotName] = M15Mod0Profile,
            ["Rifle"] = M15Mod0Profile
        };

    public static string CurrentBalanceVersion => BalanceSnapshotCatalog.GetLatest().Version;

    public static string CurrentBalanceHash => BalanceSnapshotCatalog.GetLatest().Hash;

    public static BalanceSnapshot GetBalanceSnapshot(string? snapshotHash = null) => BalanceSnapshotCatalog.GetByHash(snapshotHash);

    public static IReadOnlyList<Weapon> GetAvailableWeapons(string? snapshotHash = null)
    {
        var snapshot = GetBalanceSnapshot(snapshotHash);
        return snapshot.Weapons
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => CreateWeapon(entry.Key, snapshotHash: snapshot.Hash))
            .ToList();
    }

    /// <summary>
    /// Creates a SOKOL 545 light machine gun from the active balance snapshot.
    /// </summary>
    public static Weapon CreateSokol545(string? snapshotHash = null) => CreateWeapon(Sokol545Profile.DisplayName, snapshotHash);

    /// <summary>
    /// Creates a STURMWOLF 45 submachine gun from the active balance snapshot.
    /// </summary>
    public static Weapon CreateSturmwolf45(string? snapshotHash = null) => CreateWeapon(Sturmwolf45Profile.DisplayName, snapshotHash);

    /// <summary>
    /// Creates a M15 MOD 0 assault rifle from the active balance snapshot.
    /// </summary>
    public static Weapon CreateM15Mod0(string? snapshotHash = null) => CreateWeapon(M15Mod0Profile.DisplayName, snapshotHash);

    public static Weapon? TryCreateWeapon(string? name, string? snapshotHash = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var snapshot = GetBalanceSnapshot(snapshotHash);
        if (TryResolveSnapshotWeapon(name, snapshot, out var resolvedName, out var balanceWeapon))
            return CreateWeapon(resolvedName, balanceWeapon);

        return null;
    }

    public static Weapon CreateWeapon(string name, string? snapshotHash = null)
    {
        var snapshot = GetBalanceSnapshot(snapshotHash);
        if (!TryResolveSnapshotWeapon(name, snapshot, out var resolvedName, out var balanceWeapon))
            throw new InvalidOperationException($"Weapon '{name}' is not defined in balance snapshot {snapshot.Hash}.");

        return CreateWeapon(resolvedName, balanceWeapon);
    }

    private static bool TryResolveSnapshotWeapon(
        string name,
        BalanceSnapshot snapshot,
        out string resolvedName,
        out BalanceWeaponSnapshot balanceWeapon)
    {
        if (LegacyProfiles.TryGetValue(name, out var legacyProfile) &&
            legacyProfile is not null &&
            snapshot.Weapons.TryGetValue(legacyProfile.SnapshotName, out var legacyWeapon) &&
            legacyWeapon is not null)
        {
            balanceWeapon = legacyWeapon;
            resolvedName = legacyProfile.DisplayName;
            return true;
        }

        if (snapshot.Weapons.TryGetValue(name, out var directWeapon) &&
            directWeapon is not null)
        {
            balanceWeapon = directWeapon;
            resolvedName = name;
            return true;
        }

        resolvedName = string.Empty;
        balanceWeapon = default!;
        return false;
    }

    private static Weapon CreateWeapon(
        string resolvedName,
        BalanceWeaponSnapshot balanceWeapon)
    {
        LegacyProfiles.TryGetValue(resolvedName, out var profile);

        var weapon = new Weapon(profile?.DisplayName ?? resolvedName)
        {
            RoundsPerMinute = balanceWeapon.RoundsPerMinute,
            BulletVelocityMetersPerSecond = balanceWeapon.BulletVelocityMetersPerSecond,
            MagazineSize = balanceWeapon.MagazineSize,
            ReloadTimeMs = balanceWeapon.ReloadTimeMs,
            BaseDamage = balanceWeapon.DamageRanges.FirstOrDefault()?.Chest ?? 0f,
            HeadshotMultiplier = profile?.HeadshotMultiplier ?? ComputeHeadshotMultiplier(balanceWeapon),
            HipfireSpreadDegrees = profile?.HipfireSpreadDegrees ?? 8f,
            JumpHipfireSpreadDegrees = profile?.JumpHipfireSpreadDegrees ?? 12f,
            SlideHipfireSpreadDegrees = profile?.SlideHipfireSpreadDegrees ?? 15f,
            DiveHipfireSpreadDegrees = profile?.DiveHipfireSpreadDegrees ?? 15f,
            ADSSpreadDegrees = profile?.ADSSpreadDegrees ?? 1f,
            VerticalRecoil = profile?.VerticalRecoil ?? 0.45f,
            HorizontalRecoil = profile?.HorizontalRecoil ?? 0.12f,
            RecoilRecoveryTimeMs = profile?.RecoilRecoveryTimeMs ?? 600f,
            FirstShotRecoilScale = profile?.FirstShotRecoilScale ?? 1f,
            RecoilGunKickDegreesPerSecond = profile?.RecoilGunKickDegreesPerSecond ?? 20f,
            HorizontalRecoilControlDegreesPerSecond = profile?.HorizontalRecoilControlDegreesPerSecond ?? 12f,
            VerticalRecoilControlDegreesPerSecond = profile?.VerticalRecoilControlDegreesPerSecond ?? 45f,
            KickResetSpeedMs = profile?.KickResetSpeedMs ?? 600,
            AimingIdleSwayDegreesPerSecond = profile?.AimingIdleSwayDegreesPerSecond ?? 0.5f,
            AimingIdleSwayDelayMs = profile?.AimingIdleSwayDelayMs ?? 2200,
            FlinchResistance = profile?.FlinchResistance ?? 0.10f,
            ADSTimeMs = balanceWeapon.ADSTimeMs,
            JumpADSTimeMs = profile?.JumpADSTimeMs ?? Math.Max(balanceWeapon.ADSTimeMs, (int)(balanceWeapon.ADSTimeMs * 1.25f)),
            MovementSpeedMetersPerSecond = balanceWeapon.MovementSpeedMetersPerSecond,
            CrouchMovementSpeedMetersPerSecond = profile?.CrouchMovementSpeedMetersPerSecond ?? Math.Max(1f, balanceWeapon.MovementSpeedMetersPerSecond * 0.55f),
            SprintingMovementSpeedMetersPerSecond = profile?.SprintingMovementSpeedMetersPerSecond ?? balanceWeapon.MovementSpeedMetersPerSecond + 2f,
            ADSMovementSpeedMetersPerSecond = balanceWeapon.ADSMovementSpeedMetersPerSecond,
            SprintToFireTimeMs = balanceWeapon.SprintToFireTimeMs,
            SlideToFireTimeMs = profile?.SlideToFireTimeMs ?? balanceWeapon.SprintToFireTimeMs * 2f,
            DiveToFireTimeMs = profile?.DiveToFireTimeMs ?? balanceWeapon.SprintToFireTimeMs * 2.5f,
            JumpSprintToFireTimeMs = profile?.JumpSprintToFireTimeMs ?? balanceWeapon.SprintToFireTimeMs * 1.5f,
            BulletsPerCommitmentUnit = profile?.BulletsPerCommitmentUnit ?? 3,
            SuppressionFactor = profile?.SuppressionFactor ?? 1.0f
        };

        ApplyDamageRanges(weapon, balanceWeapon.DamageRanges);
        weapon.BodyPartDamageMultipliers.Clear();
        return weapon;
    }

    private static void ApplyDamageRanges(Weapon weapon, IReadOnlyList<BalanceDamageRangeSnapshot> damageRanges)
    {
        weapon.DamageRanges.Clear();

        for (var index = 0; index < damageRanges.Count; index++)
        {
            var current = damageRanges[index];
            var maxMeters = index + 1 < damageRanges.Count
                ? damageRanges[index + 1].RangeMeters
                : float.PositiveInfinity;

            weapon.DamageRanges.Add(new DamageRange(current.RangeMeters, maxMeters, current.Chest)
            {
                BodyPartDamageOverrides = new Dictionary<BodyPart, float>
                {
                    [BodyPart.Head] = current.Head,
                    [BodyPart.Neck] = current.Neck,
                    [BodyPart.UpperTorso] = current.Chest,
                    [BodyPart.LowerTorso] = current.Stomach,
                    [BodyPart.UpperArm] = current.UpperArm,
                    [BodyPart.LowerArm] = current.LowerArm,
                    [BodyPart.UpperLeg] = current.UpperLeg,
                    [BodyPart.LowerLeg] = current.LowerLeg
                }
            });
        }
    }

    private static float ComputeHeadshotMultiplier(BalanceWeaponSnapshot weapon)
    {
        var firstRange = weapon.DamageRanges.FirstOrDefault();
        if (firstRange == null || firstRange.Chest <= 0f)
            return 1f;

        return firstRange.Head / firstRange.Chest;
    }

    private sealed record LegacyWeaponProfile(
        string DisplayName,
        string SnapshotName,
        float HeadshotMultiplier,
        float HipfireSpreadDegrees,
        float JumpHipfireSpreadDegrees,
        float SlideHipfireSpreadDegrees,
        float DiveHipfireSpreadDegrees,
        float ADSSpreadDegrees,
        float VerticalRecoil,
        float HorizontalRecoil,
        float RecoilRecoveryTimeMs,
        float FirstShotRecoilScale,
        float RecoilGunKickDegreesPerSecond,
        float HorizontalRecoilControlDegreesPerSecond,
        float VerticalRecoilControlDegreesPerSecond,
        int KickResetSpeedMs,
        float AimingIdleSwayDegreesPerSecond,
        int AimingIdleSwayDelayMs,
        float FlinchResistance,
        int JumpADSTimeMs,
        float CrouchMovementSpeedMetersPerSecond,
        float SprintingMovementSpeedMetersPerSecond,
        float SlideToFireTimeMs,
        float DiveToFireTimeMs,
        float JumpSprintToFireTimeMs,
        int BulletsPerCommitmentUnit,
        float SuppressionFactor);
}
