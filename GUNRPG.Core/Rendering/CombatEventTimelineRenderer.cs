using GUNRPG.Core.Events;
using GUNRPG.Core.Operators;
using Microsoft.FSharp.Core;
using Plotly.NET;
using Plotly.NET.CSharp;
using Plotly.NET.ImageExport;
using Plotly.NET.LayoutObjects;

namespace GUNRPG.Core.Rendering;

public sealed class CombatEventTimelineRenderer
{
    private const int MinEventDurationMs = 2;

    public IReadOnlyList<CombatEventTimelineEntry> BuildTimelineEntries(
        IReadOnlyList<ISimulationEvent> events,
        Operator player,
        Operator enemy)
    {
        var entries = events
            .Select(evt => ToTimelineEntry(evt, player, enemy))
            .OrderBy(entry => entry.StartTimeMs)
            .ThenBy(entry => entry.ActorName, StringComparer.Ordinal)
            .ToList();

        return entries;
    }

    public void RenderTimeline(
        IReadOnlyList<CombatEventTimelineEntry> entries,
        string outputPath)
    {
        if (entries.Count == 0)
        {
            Console.WriteLine("No combat events captured for timeline rendering.");
            return;
        }

        var (labels, durations, bases) = BuildChartSeries(entries);
        var timelineBarTraces = new List<GenericChart>(entries.Count);

        for (int i = 0; i < labels.Count; i++)
        {
            var trace = Plotly.NET.CSharp.Chart.Bar<double, string, string>(
                new[] { durations[i] },
                Keys: new[] { labels[i] },
                Base: bases[i],
                Width: 0.6,
                ShowLegend: false);
            timelineBarTraces.Add(trace);
        }

        var chart = Plotly.NET.Chart.Combine(timelineBarTraces);

        var orientation = Trace2DStyle.Bar<double, double, double, double, double, double, double, double, double, double, double, double, double, double, double, double, double, Trace>(
            Orientation: FSharpOption<StyleParam.Orientation>.Some(StyleParam.Orientation.Horizontal));
        chart = Plotly.NET.GenericChart.mapTrace(orientation, chart);

        chart = Plotly.NET.CSharp.GenericChartExtensions.WithXAxisStyle<double, double, string>(
            chart,
            TitleText: "Simulated Time (ms)");
        chart = Plotly.NET.CSharp.GenericChartExtensions.WithYAxisStyle<double, double, string>(
            chart,
            TitleText: "Combat Events",
            CategoryOrder: StyleParam.CategoryOrder.Array,
            CategoryArray: labels);

        var title = Plotly.NET.Title.init(FSharpOption<string>.Some("Combat Event Timeline"));
        var margin = Margin.init<int, int, int, int, int, bool>(
            Left: FSharpOption<int>.Some(280),
            Right: FSharpOption<int>.Some(40),
            Top: FSharpOption<int>.Some(60),
            Bottom: FSharpOption<int>.Some(60),
            Pad: FSharpOption<int>.Some(8),
            Autoexpand: FSharpOption<bool>.Some(true));

        chart = Plotly.NET.GenericChartExtensions.WithLayoutStyle<string>(
            chart,
            Title: FSharpOption<Plotly.NET.Title>.Some(title),
            Margin: FSharpOption<Margin>.Some(margin),
            Height: FSharpOption<int>.Some(Math.Clamp(entries.Count * 28, 320, 1400)));

        SaveChart(chart, outputPath);
    }

    private static void SaveChart(GenericChart chart, string outputPath)
    {
        var extension = Path.GetExtension(outputPath);
        if (string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                chart.SavePNG(
                    outputPath,
                    EngineType: FSharpOption<ExportEngine>.Some(ExportEngine.PuppeteerSharp),
                    Width: FSharpOption<int>.Some(1600),
                    Height: FSharpOption<int>.Some(900));
                return;
            }
            catch (Exception ex)
            {
                var fallbackPath = Path.ChangeExtension(outputPath, ".html");
                Plotly.NET.GenericChartExtensions.SaveHtml(
                    chart,
                    fallbackPath,
                    FSharpOption<bool>.Some(true));
                Console.WriteLine("PNG export unavailable. Saved HTML instead:");
                Console.WriteLine($"  {fallbackPath}");
                Console.WriteLine($"  Details: {ex.GetType().Name} - {ex.Message}");
                Console.WriteLine("  Hint: Ensure PuppeteerSharp dependencies are available.");
                return;
            }
        }

        Plotly.NET.GenericChartExtensions.SaveHtml(
            chart,
            outputPath,
            FSharpOption<bool>.Some(true));
    }

    private static CombatEventTimelineEntry ToTimelineEntry(
        ISimulationEvent evt,
        Operator player,
        Operator enemy)
    {
        var actorName = ResolveActorName(evt.OperatorId, player, enemy);
        var (start, end) = ResolveEventWindow(evt);

        return new CombatEventTimelineEntry(FormatEventType(evt), start, end, actorName);
    }

    private static int InferDurationMs(ISimulationEvent evt)
    {
        return evt switch
        {
            ShotFiredEvent shot => Math.Max((int)Math.Round((double)shot.TravelTimeMs, MidpointRounding.AwayFromZero), MinEventDurationMs),
            DamageAppliedEvent => MinEventDurationMs,
            ShotMissedEvent => MinEventDurationMs,
            ReloadCompleteEvent reload => Math.Max(reload.ActionDurationMs, MinEventDurationMs),
            ADSCompleteEvent ads => Math.Max(ads.ActionDurationMs, MinEventDurationMs),
            ADSTransitionUpdateEvent adsUpdate => Math.Max(adsUpdate.ActionDurationMs, MinEventDurationMs),
            MovementIntervalEvent movement => Math.Max(movement.IntervalDurationMs, MinEventDurationMs),
            SlideCompleteEvent slide => Math.Max(slide.ActionDurationMs, MinEventDurationMs),
            MicroReactionEvent microReaction => Math.Max(microReaction.ActionDurationMs, MinEventDurationMs),
            _ => MinEventDurationMs
        };
    }

    private static (int start, int end) ResolveEventWindow(ISimulationEvent evt)
    {
        int eventTime = (int)Math.Clamp(evt.EventTimeMs, int.MinValue, int.MaxValue);
        int duration = Math.Max(InferDurationMs(evt), MinEventDurationMs);
        int start = eventTime;
        int end = eventTime + duration;

        switch (evt)
        {
            case ReloadCompleteEvent reload:
                start = eventTime - Math.Max(reload.ActionDurationMs, MinEventDurationMs);
                end = eventTime;
                break;
            case ADSCompleteEvent ads:
                start = eventTime - Math.Max(ads.ActionDurationMs, MinEventDurationMs);
                end = eventTime;
                break;
            case ADSTransitionUpdateEvent adsUpdate:
                start = eventTime - Math.Max(adsUpdate.ActionDurationMs, MinEventDurationMs);
                end = eventTime;
                break;
            case MovementIntervalEvent movement:
                start = eventTime - Math.Max(movement.IntervalDurationMs, MinEventDurationMs);
                end = eventTime;
                break;
            case SlideCompleteEvent slide:
                start = eventTime - Math.Max(slide.ActionDurationMs, MinEventDurationMs);
                end = eventTime;
                break;
        }

        start = Math.Max(start, 0);
        if (end <= start)
        {
            end = start + MinEventDurationMs;
        }

        return (start, end);
    }

    private static string? ResolveActorName(Guid operatorId, Operator player, Operator enemy)
    {
        if (operatorId == player.Id)
        {
            return player.Name;
        }

        if (operatorId == enemy.Id)
        {
            return enemy.Name;
        }

        return null;
    }

    private static string FormatEventType(ISimulationEvent evt)
    {
        string name = evt.GetType().Name;
        return name.EndsWith("Event", StringComparison.Ordinal)
            ? name[..^"Event".Length]
            : name;
    }

    private static (List<string> labels, List<double> durations, List<double> bases) BuildChartSeries(
        IReadOnlyList<CombatEventTimelineEntry> entries)
    {
        var labels = new List<string>(entries.Count);
        var durations = new List<double>(entries.Count);
        var bases = new List<double>(entries.Count);

        foreach (var entry in entries)
        {
            int duration = entry.DurationMs <= 0 ? MinEventDurationMs : entry.DurationMs;
            string actorLabel = string.IsNullOrWhiteSpace(entry.ActorName) ? "Unknown" : entry.ActorName!;
            string label = FormattableString.Invariant($"{entry.StartTimeMs}ms | {actorLabel} | {entry.EventType}");

            labels.Add(label);
            durations.Add(duration);
            bases.Add(entry.StartTimeMs);
        }

        return (labels, durations, bases);
    }
}
