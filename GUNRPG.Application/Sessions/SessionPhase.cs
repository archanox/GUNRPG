namespace GUNRPG.Application.Sessions;

/// <summary>
/// High-level lifecycle for a combat session (distinct from combat phase).
/// </summary>
public enum SessionPhase
{
    Created,
    Planning,
    Resolving,
    Completed
}
