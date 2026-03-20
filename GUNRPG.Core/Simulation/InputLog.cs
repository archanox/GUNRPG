namespace GUNRPG.Core.Simulation;

/// <summary>
/// Canonical per-tick input frame used for live simulation, replay, and validation.
/// </summary>
public sealed record InputFrame(long Tick, Guid PlayerId, PlayerAction Intent);

/// <summary>
/// Backward-compatible alias for <see cref="InputFrame"/>.
/// </summary>
public sealed record InputLogEntry(long Tick, PlayerAction Action);

public sealed class InputLog
{
    public InputLog(Guid runId, Guid playerId, int seed, IReadOnlyList<InputLogEntry> entries)
    {
        RunId = runId;
        PlayerId = playerId;
        Seed = seed;
        Entries = NormalizeEntries(entries, nameof(entries));
        Frames = Entries.Select(e => new InputFrame(e.Tick, playerId, e.Action)).ToArray();
    }

    public Guid RunId { get; }
    public Guid PlayerId { get; }
    public int Seed { get; }
    public IReadOnlyList<InputLogEntry> Entries { get; }

    /// <summary>
    /// Canonical input frames combining each entry with the log's <see cref="PlayerId"/>.
    /// Used for live simulation, replay, and validation.
    /// </summary>
    public IReadOnlyList<InputFrame> Frames { get; }

    public static InputLog FromRunInput(RunInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var actions = input.Actions ?? throw new ArgumentException("Run input actions must not be null.", nameof(input));

        var entries = actions
            .Select((action, index) =>
            {
                if (action is null)
                {
                    throw new ArgumentException(
                        $"Action at index {index} in run input must not be null.",
                        nameof(input));
                }

                return new InputLogEntry(index, action);
            })
            .ToArray();

        return new InputLog(input.RunId, input.PlayerId, input.Seed, entries);
    }

    private static IReadOnlyList<InputLogEntry> NormalizeEntries(IReadOnlyList<InputLogEntry>? entries, string paramName)
    {
        ArgumentNullException.ThrowIfNull(entries, paramName);

        var normalized = entries
            .Select((entry, index) =>
            {
                if (entry is null)
                {
                    throw new ArgumentException($"Input log entry at index {index} must not be null.", paramName);
                }

                if (entry.Action is null)
                {
                    throw new ArgumentException(
                        $"Input log entry at index {index} must not contain a null action.",
                        paramName);
                }

                return (Entry: entry, OriginalIndex: index);
            })
            .OrderBy(item => item.Entry.Tick)
            .ThenBy(item => item.OriginalIndex)
            .Select(item => item.Entry)
            .ToArray();

        // §6: Validate no duplicate ticks (single-player simulation allows at most one input per tick)
        var seenTicks = new HashSet<long>();
        foreach (var entry in normalized)
        {
            if (!seenTicks.Add(entry.Tick))
            {
                throw new ArgumentException(
                    $"Duplicate input at tick {entry.Tick}. Each tick may have at most one input per player.",
                    paramName);
            }
        }

        return normalized;
    }
}
