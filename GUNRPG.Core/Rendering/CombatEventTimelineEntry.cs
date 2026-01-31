namespace GUNRPG.Core.Rendering;

public sealed record CombatEventTimelineEntry(
    string EventType,
    int StartTimeMs,
    int EndTimeMs,
    string? ActorName,
    string? Detail = null)
{
    public int DurationMs => Math.Max(EndTimeMs - StartTimeMs, 0);
}
