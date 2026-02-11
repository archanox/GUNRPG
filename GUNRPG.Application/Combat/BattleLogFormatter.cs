using GUNRPG.Application.Dtos;
using GUNRPG.Core.Events;
using GUNRPG.Core.Operators;

namespace GUNRPG.Application.Combat;

/// <summary>
/// Formats combat events into human-readable battle log messages.
/// </summary>
public static class BattleLogFormatter
{
    private const int MaxLogEntries = 20; // Keep the most recent 20 events

    public static List<BattleLogEntryDto> FormatEvents(IReadOnlyList<ISimulationEvent> events, Operator player, Operator enemy)
    {
        var entries = new List<BattleLogEntryDto>();

        foreach (var evt in events)
        {
            var entry = FormatEvent(evt, player, enemy);
            if (entry != null)
            {
                entries.Add(entry);
            }
        }

        // Return only the most recent entries
        return entries.Count > MaxLogEntries 
            ? entries.Skip(entries.Count - MaxLogEntries).ToList() 
            : entries;
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
                Message = $"started {moveEvent.MovementType.ToString().ToLower()}.",
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
                Message = $"took {coverEvent.CoverType.ToString().ToLower()} cover.",
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
            _ => null // Skip events we don't want to display
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
}
