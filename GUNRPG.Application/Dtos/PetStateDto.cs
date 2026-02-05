namespace GUNRPG.Application.Dtos;

public sealed class PetStateDto
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
