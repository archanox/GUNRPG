namespace GUNRPG.Api.Dtos;

/// <summary>
/// API-specific pet state DTO.
/// </summary>
public sealed class ApiPetStateDto
{
    public float Health { get; init; }
    public float Fatigue { get; init; }
    public float Injury { get; init; }
    public float Stress { get; init; }
    public float Morale { get; init; }
    public float Hunger { get; init; }
    public float Hydration { get; init; }
    public DateTimeOffset LastUpdated { get; init; }
}
