namespace GUNRPG.Api.Dtos;

/// <summary>
/// API request for pet actions.
/// </summary>
public sealed class ApiPetActionRequest
{
    public string? Action { get; init; }
    public float? Nutrition { get; init; }
    public float? Hydration { get; init; }
    public int? HitsTaken { get; init; }
    public float? OpponentDifficulty { get; init; }
    public float? Hours { get; init; }
}
