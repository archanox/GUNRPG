using GUNRPG.Application.Backend;
using GUNRPG.Application.Dtos;

namespace GUNRPG.Application.Combat;

/// <summary>
/// Deterministic result of a single combat mission execution.
/// Produced by <see cref="IDeterministicCombatEngine.Execute"/>.
/// </summary>
public sealed class CombatResult
{
    public OperatorDto ResultOperator { get; init; } = default!;
    public bool IsVictory { get; init; }
    public bool OperatorDied { get; init; }
    public List<BattleLogEntryDto> BattleLog { get; init; } = new();
}
