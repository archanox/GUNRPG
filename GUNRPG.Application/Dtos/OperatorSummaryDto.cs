namespace GUNRPG.Application.Dtos;

/// <summary>
/// Lightweight summary of an operator for list views.
/// </summary>
public sealed class OperatorSummaryDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string CurrentMode { get; init; } = string.Empty;
    public bool IsDead { get; init; }
    public long TotalXp { get; init; }
    public float CurrentHealth { get; init; }
    public float MaxHealth { get; init; }
}
