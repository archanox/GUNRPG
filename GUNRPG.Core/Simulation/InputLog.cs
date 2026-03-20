namespace GUNRPG.Core.Simulation;

public sealed record InputLogEntry(long Tick, PlayerAction Action);

public sealed class InputLog
{
    public InputLog(Guid runId, Guid playerId, int seed, IReadOnlyList<InputLogEntry> entries)
    {
        RunId = runId;
        PlayerId = playerId;
        Seed = seed;
        Entries = entries ?? throw new ArgumentNullException(nameof(entries));
    }

    public Guid RunId { get; }
    public Guid PlayerId { get; }
    public int Seed { get; }
    public IReadOnlyList<InputLogEntry> Entries { get; }

    public static InputLog FromRunInput(RunInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var entries = input.Actions
            .Select((action, index) => new InputLogEntry(index, action))
            .ToArray();

        return new InputLog(input.RunId, input.PlayerId, input.Seed, entries);
    }
}
