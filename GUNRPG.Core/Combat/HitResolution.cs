using GUNRPG.Core.Operators;
using GUNRPG.Core.Weapons;

namespace GUNRPG.Core.Combat;

/// <summary>
/// Represents the result of a shot resolution.
/// </summary>
public sealed record ShotResolutionResult(BodyPart HitLocation, float FinalAngleDegrees);

/// <summary>
/// Implements vertical body-part hit resolution based on angular bands.
/// Assumptions:
/// - NO horizontal recoil
/// - Each round resolves at most ONE shot per operator
/// - A shot either hits a body part or misses entirely
/// - Recoil is modeled as vertical angular displacement only
/// - Distance affects which body part is intersected based on vertical angle
/// </summary>
public static class HitResolution
{
    /// <summary>
    /// Angular range definition for a body part.
    /// </summary>
    private sealed record AngularBand(float MinDegrees, float MaxDegrees, BodyPart BodyPart);

    /// <summary>
    /// Body part angular bands (in degrees).
    /// - LowerTorso: 0.00° – 0.25°
    /// - UpperTorso: 0.25° – 0.50°
    /// - Neck:       0.50° – 0.75°
    /// - Head:       0.75° – 1.00°
    /// </summary>
    private static readonly AngularBand[] AngularBands = new[]
    {
        new AngularBand(0.00f, 0.25f, BodyPart.LowerTorso),
        new AngularBand(0.25f, 0.50f, BodyPart.UpperTorso),
        new AngularBand(0.50f, 0.75f, BodyPart.Neck),
        new AngularBand(0.75f, 1.00f, BodyPart.Head)
    };

    private const float MinAngle = 0.00f;
    private const float MaxAngle = 1.00f;

    /// <summary>
    /// Resolves a shot to determine which body part (if any) is hit.
    /// </summary>
    /// <param name="distance">Distance to target in meters</param>
    /// <param name="targetBodyPart">Intended target body part</param>
    /// <param name="operatorAccuracy">Operator accuracy stat (affects standard deviation of aim error)</param>
    /// <param name="weaponVerticalRecoil">Weapon's vertical recoil value</param>
    /// <param name="currentRecoilY">Current accumulated vertical recoil state</param>
    /// <param name="recoilVariance">Optional variance in recoil application</param>
    /// <param name="random">Random number generator for deterministic-friendly operation</param>
    /// <returns>The resolved body part hit or Miss if shot overshoots/undershoots</returns>
    public static ShotResolutionResult ResolveShot(
        float distance,
        BodyPart targetBodyPart,
        float operatorAccuracy,
        float weaponVerticalRecoil,
        float currentRecoilY,
        float recoilVariance,
        Random random)
    {
        // Get the center angle of the target body part
        float targetAngle = GetBodyPartCenterAngle(targetBodyPart);

        // Apply initial aim acquisition error based on operator accuracy
        // Lower accuracy = higher standard deviation = more error
        float aimErrorStdDev = (1.0f - operatorAccuracy) * 0.15f; // Scale to reasonable range
        float aimError = SampleGaussian(random, 0f, aimErrorStdDev);

        // Apply vertical recoil with variance
        float recoilVariation = recoilVariance > 0
            ? (float)(random.NextDouble() * 2.0 - 1.0) * recoilVariance
            : 0f;
        float totalVerticalRecoil = currentRecoilY + weaponVerticalRecoil + recoilVariation;

        // Calculate final vertical angle
        float finalAngle = targetAngle + aimError + totalVerticalRecoil;

        // Convert final angle to body part hit
        BodyPart hitLocation = ConvertAngleToBodyPart(finalAngle);

        return new ShotResolutionResult(hitLocation, finalAngle);
    }

    /// <summary>
    /// Gets the center angle of a body part's angular band.
    /// </summary>
    private static float GetBodyPartCenterAngle(BodyPart bodyPart)
    {
        var band = Array.Find(AngularBands, b => b.BodyPart == bodyPart);
        if (band != null)
        {
            return (band.MinDegrees + band.MaxDegrees) / 2f;
        }

        // Default to center of lower torso if not found
        return 0.125f;
    }

    /// <summary>
    /// Converts a final vertical angle into a body part hit using the angular bands.
    /// Returns Miss if the angle is outside the valid range.
    /// </summary>
    private static BodyPart ConvertAngleToBodyPart(float angleDegrees)
    {
        // Check for overshoots (above head)
        if (angleDegrees > MaxAngle)
            return BodyPart.Miss;

        // Check for undershoots (below lower torso)
        if (angleDegrees < MinAngle)
            return BodyPart.Miss;

        // Find the matching angular band
        for (int i = 0; i < AngularBands.Length; i++)
        {
            var band = AngularBands[i];
            // Use inclusive upper bound for the last band (Head) to handle exactly 1.0°
            bool isLastBand = i == AngularBands.Length - 1;
            bool inBand = angleDegrees >= band.MinDegrees && 
                         (isLastBand ? angleDegrees <= band.MaxDegrees : angleDegrees < band.MaxDegrees);
            
            if (inBand)
            {
                return band.BodyPart;
            }
        }

        // Should not reach here due to range checks above, but return Miss as fallback
        return BodyPart.Miss;
    }

    /// <summary>
    /// Samples from a Gaussian (normal) distribution using the Box-Muller transform.
    /// </summary>
    private static float SampleGaussian(Random random, float mean, float stdDev)
    {
        // Box-Muller transform
        double u1 = 1.0 - random.NextDouble(); // Uniform(0,1] 
        double u2 = 1.0 - random.NextDouble();
        double randStdNormal = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
        return mean + stdDev * (float)randStdNormal;
    }
}
