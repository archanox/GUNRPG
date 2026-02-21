using GUNRPG.Application.Sessions;
using GUNRPG.Core.Combat;
using GUNRPG.Core.Intents;

namespace GUNRPG.Application.Combat;

/// <summary>
/// Service that runs a full combat simulation to completion using AI for both player and enemy.
/// Reused by both online and offline backends to ensure consistent, deterministic results.
/// </summary>
public static class CombatSimulationService
{
    /// <summary>
    /// Maximum number of turns before the simulation is considered a draw.
    /// </summary>
    private const int MaxTurns = 100;

    /// <summary>
    /// Runs a complete combat simulation with AI controlling both sides.
    /// Returns the resulting <see cref="CombatOutcome"/> from the completed session.
    /// </summary>
    /// <param name="playerName">Name of the player operator.</param>
    /// <param name="seed">Optional random seed for deterministic replay.</param>
    /// <param name="operatorId">Optional operator ID to associate with the session.</param>
    /// <returns>The combat outcome after the session reaches completion.</returns>
    public static CombatOutcome RunSimulation(string? playerName = null, int? seed = null, Guid? operatorId = null)
    {
        var session = CombatSession.CreateDefault(
            playerName: playerName,
            seed: seed,
            operatorId: operatorId);

        for (int turn = 0; turn < MaxTurns; turn++)
        {
            if (session.Phase == SessionPhase.Completed)
                break;

            if (session.Combat.Phase == CombatPhase.Ended)
            {
                // Post-combat processing and transition to Completed
                session.TransitionTo(SessionPhase.Completed);
                break;
            }

            // AI decides intents for both player and enemy
            var playerIntents = session.Ai.DecideIntents(session.Player, session.Enemy, session.Combat);
            var playerSubmission = session.Combat.SubmitIntents(session.Player, playerIntents);
            if (!playerSubmission.success)
            {
                // Fallback: submit stop intents
                session.Combat.SubmitIntents(session.Player, SimultaneousIntents.CreateStop(session.Player.Id));
            }

            var enemyIntents = session.Ai.DecideIntents(session.Enemy, session.Player, session.Combat);
            var enemySubmission = session.Combat.SubmitIntents(session.Enemy, enemyIntents);
            if (!enemySubmission.success)
            {
                session.Combat.SubmitIntents(session.Enemy, SimultaneousIntents.CreateStop(session.Enemy.Id));
            }

            // Transition to resolving and begin execution
            session.TransitionTo(SessionPhase.Resolving);
            session.Combat.BeginExecution();

            // Run execution until next planning phase or end
            while (session.Combat.Phase == CombatPhase.Executing)
            {
                var hasMoreEvents = session.Combat.ExecuteUntilReactionWindow();
                if (!hasMoreEvents || session.Combat.Phase != CombatPhase.Executing)
                    break;
            }

            if (session.Combat.Phase == CombatPhase.Ended)
            {
                session.TransitionTo(SessionPhase.Completed);
                break;
            }

            // Go back to planning for next turn
            session.TransitionTo(SessionPhase.Planning);
            session.AdvanceTurnCounter();
            session.RecordAction();
        }

        // If we exhausted max turns without completion, force complete
        if (session.Phase != SessionPhase.Completed)
        {
            session.TransitionTo(SessionPhase.Completed);
        }

        return session.GetOutcome();
    }
}
