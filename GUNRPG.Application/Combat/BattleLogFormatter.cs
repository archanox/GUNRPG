using GUNRPG.Application.Dtos;
using GUNRPG.Core.Events;
using GUNRPG.Core.Operators;

namespace GUNRPG.Application.Combat;

/// <summary>
/// Formats combat events into human-readable battle log messages.
/// </summary>
public static class BattleLogFormatter
{
    public static List<BattleLogEntryDto> FormatEvents(IReadOnlyList<ISimulationEvent> events, Operator player, Operator enemy)
    {
        return events
            .Select(evt => FormatEvent(evt, player, enemy))
            .Where(entry => entry != null)
            .Cast<BattleLogEntryDto>()
            .ToList();
    }

    private static BattleLogEntryDto? FormatEvent(ISimulationEvent evt, Operator player, Operator enemy)
    {
        return evt switch
        {
            ShotFiredEvent shotEvent => new BattleLogEntryDto
            {
                EventType = "ShotFired",
                TimeMs = shotEvent.EventTimeMs,
                Message = "fired a shot!",
                ActorName = GetActorName(shotEvent.OperatorId, player, enemy)
            },
            DamageAppliedEvent damageEvent => new BattleLogEntryDto
            {
                EventType = "Damage",
                TimeMs = damageEvent.EventTimeMs,
                Message = $"{damageEvent.TargetName} took {damageEvent.Damage:F0} damage ({damageEvent.BodyPart})!",
                ActorName = null // Shooter name will be prepended if needed
            },
            ShotMissedEvent missEvent => new BattleLogEntryDto
            {
                EventType = "Miss",
                TimeMs = missEvent.EventTimeMs,
                Message = "missed!",
                ActorName = GetActorName(missEvent.OperatorId, player, enemy)
            },
            ReloadCompleteEvent reloadEvent => new BattleLogEntryDto
            {
                EventType = "Reload",
                TimeMs = reloadEvent.EventTimeMs,
                Message = "reloaded.",
                ActorName = GetActorName(reloadEvent.OperatorId, player, enemy)
            },
            ADSCompleteEvent adsEvent => new BattleLogEntryDto
            {
                EventType = "ADS",
                TimeMs = adsEvent.EventTimeMs,
                Message = "aimed down sights.",
                ActorName = GetActorName(adsEvent.OperatorId, player, enemy)
            },
            MovementStartedEvent moveEvent => new BattleLogEntryDto
            {
                EventType = "Movement",
                TimeMs = moveEvent.EventTimeMs,
                Message = $"started {FormatMovementType(moveEvent.MovementType)}.",
                ActorName = moveEvent.Operator.Name
            },
            MovementEndedEvent moveEndEvent => new BattleLogEntryDto
            {
                EventType = "Movement",
                TimeMs = moveEndEvent.EventTimeMs,
                Message = "stopped moving.",
                ActorName = moveEndEvent.Operator.Name
            },
            CoverEnteredEvent coverEvent => new BattleLogEntryDto
            {
                EventType = "Cover",
                TimeMs = coverEvent.EventTimeMs,
                Message = $"took {FormatCoverType(coverEvent.CoverType)} cover.",
                ActorName = coverEvent.Operator.Name
            },
            CoverExitedEvent coverExitEvent => new BattleLogEntryDto
            {
                EventType = "Cover",
                TimeMs = coverExitEvent.EventTimeMs,
                Message = "left cover.",
                ActorName = coverExitEvent.Operator.Name
            },
            SuppressionStartedEvent suppressEvent => new BattleLogEntryDto
            {
                EventType = "Suppression",
                TimeMs = suppressEvent.EventTimeMs,
                Message = "is suppressing!",
                ActorName = GetActorName(suppressEvent.OperatorId, player, enemy)
            },
            _ => new BattleLogEntryDto
            {
                // Deterministic offline sync stores every event; replay currently uses
                // Damage entries and keeps others for chain verification and future replay expansion.
                EventType = evt.GetType().Name,
                TimeMs = evt.EventTimeMs,
                Message = $"Unformatted event: {evt.GetType().Name}",
                ActorName = null
            }
        };
    }

    private static string GetActorName(Guid operatorId, Operator player, Operator enemy)
    {
        if (operatorId == player.Id)
            return player.Name;
        else if (operatorId == enemy.Id)
            return enemy.Name;
        else
            return "Unknown";
    }

    private static string FormatMovementType(MovementState movementType)
    {
        return movementType switch
        {
            MovementState.Walking => "walking",
            MovementState.Sprinting => "sprinting",
            MovementState.Crouching => "crouching",
            MovementState.Sliding => "sliding",
            MovementState.Stationary => "stationary",
            _ => movementType.ToString().ToLowerInvariant()
        };
    }

    private static string FormatCoverType(CoverState coverType)
    {
        return coverType switch
        {
            CoverState.Partial => "partial",
            CoverState.Full => "full",
            CoverState.None => "no",
            _ => coverType.ToString().ToLowerInvariant()
        };
    }
}
