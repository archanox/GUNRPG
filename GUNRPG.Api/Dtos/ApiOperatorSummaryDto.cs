namespace GUNRPG.Api.Dtos;

/// <summary>
/// API DTO for operator summary information (list view).
/// </summary>
public sealed class ApiOperatorSummaryDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string CurrentMode { get; init; } = string.Empty;
    public bool IsDead { get; init; }
    public long TotalXp { get; init; }
    public float CurrentHealth { get; init; }
    public float MaxHealth { get; init; }
}
