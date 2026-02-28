namespace GUNRPG.Application.Distributed;

/// <summary>
/// Shared deterministic game engine that produces identical results given the same state and action.
/// Used by both <see cref="LocalGameAuthority"/> and <see cref="DistributedAuthority"/>
/// to ensure a single source of truth for game rules.
/// </summary>
public interface IDeterministicGameEngine
{
    /// <summary>
    /// Applies a single player action to the current game state and returns the new state.
    /// Must be pure and deterministic — identical inputs must always produce identical outputs.
    /// </summary>
    GameStateDto Step(GameStateDto state, PlayerActionDto action);
}
