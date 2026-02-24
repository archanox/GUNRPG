using GUNRPG.Application.Backend;

namespace GUNRPG.Application.Combat;

/// <summary>
/// Deterministic combat engine that produces identical results given the same operator state and seed.
/// Used for both offline mission execution (client) and server-side replay validation.
/// </summary>
public interface IDeterministicCombatEngine
{
    /// <summary>
    /// Executes a combat mission deterministically using the provided seed.
    /// Must produce identical results for the same inputs on every invocation.
    /// Uses only <c>new Random(seed)</c> — no static Random, no DateTime, no external state.
    /// </summary>
    CombatResult Execute(OperatorDto snapshot, int seed);
}
