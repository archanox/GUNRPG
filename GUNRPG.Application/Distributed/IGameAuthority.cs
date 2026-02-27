namespace GUNRPG.Application.Distributed;

/// <summary>
/// Abstraction over how game state is replicated and authority is managed.
/// Implementations may be offline (single-node) or distributed (peer-to-peer lockstep).
/// </summary>
public interface IGameAuthority
{
    /// <summary>Unique identifier for this node in the distributed network.</summary>
    Guid NodeId { get; }

    /// <summary>Submit a player action to the authority for processing.</summary>
    Task SubmitActionAsync(PlayerActionDto action, CancellationToken ct = default);

    /// <summary>Get the current game state as a DTO.</summary>
    GameStateDto GetCurrentState();

    /// <summary>Get the current state hash (SHA256 hex string).</summary>
    string GetCurrentStateHash();

    /// <summary>Get the full ordered action log.</summary>
    IReadOnlyList<DistributedActionEntry> GetActionLog();

    /// <summary>Whether this authority is in a desynchronized state.</summary>
    bool IsDesynced { get; }
}
