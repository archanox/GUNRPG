using GUNRPG.Api.Dtos;
using GUNRPG.Core;
using GUNRPG.Core.Weapons;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace GUNRPG.Api.Controllers;

/// <summary>
/// Returns static weapon catalogue data (stats, damage ranges, handling values).
/// No authentication is required — weapon stats are public game data.
/// </summary>
[ApiController]
[Route("weapons")]
[AllowAnonymous]
public class WeaponsController : ControllerBase
{
    private static readonly IReadOnlyList<ApiWeaponStatsDto> _weaponStats =
        new[]
        {
            WeaponFactory.CreateSokol545(),
            WeaponFactory.CreateSturmwolf45(),
            WeaponFactory.CreateM15Mod0(),
        }.Select(ToDto).ToList();

    /// <summary>
    /// Returns stats for all available weapons.
    /// </summary>
    /// <returns>A list of weapon stats objects.</returns>
    /// <response code="200">Returns the weapon catalogue.</response>
    [HttpGet]
    [ProducesResponseType(typeof(List<ApiWeaponStatsDto>), StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<ApiWeaponStatsDto>> GetAll() => Ok(_weaponStats);

    private static ApiWeaponStatsDto ToDto(Weapon w) => new()
    {
        Name = w.Name,
        RoundsPerMinute = w.RoundsPerMinute,
        BulletVelocityMetersPerSecond = w.BulletVelocityMetersPerSecond,
        MagazineSize = w.MagazineSize,
        ReloadTimeMs = w.ReloadTimeMs,
        BaseDamage = w.BaseDamage,
        HeadshotMultiplier = w.HeadshotMultiplier,
        DamageRanges = w.DamageRanges.Select(r => new ApiWeaponDamageRangeDto
        {
            MinMeters = r.MinMeters,
            MaxMeters = r.MaxMeters,
            Damage = r.Damage,
            HeadDamage = r.BodyPartDamageOverrides is not null &&
                         r.BodyPartDamageOverrides.TryGetValue(BodyPart.Head, out var hd)
                             ? hd
                             : r.Damage * w.HeadshotMultiplier,
        }).ToList(),
        HipfireSpreadDegrees = w.HipfireSpreadDegrees,
        ADSSpreadDegrees = w.ADSSpreadDegrees,
        VerticalRecoil = w.VerticalRecoil,
        HorizontalRecoil = w.HorizontalRecoil,
        RecoilRecoveryTimeMs = w.RecoilRecoveryTimeMs,
        ADSTimeMs = w.ADSTimeMs,
        FlinchResistance = w.FlinchResistance,
        SuppressionFactor = w.SuppressionFactor,
        MovementSpeedMetersPerSecond = w.MovementSpeedMetersPerSecond,
        SprintingMovementSpeedMetersPerSecond = w.SprintingMovementSpeedMetersPerSecond,
        ADSMovementSpeedMetersPerSecond = w.ADSMovementSpeedMetersPerSecond,
        SprintToFireTimeMs = w.SprintToFireTimeMs,
    };
}
