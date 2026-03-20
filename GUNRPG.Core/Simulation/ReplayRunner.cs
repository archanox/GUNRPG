using GUNRPG.Core.Time;

namespace GUNRPG.Core.Simulation;

public sealed class ReplayResult
{
    public ReplayResult(SimulationState finalState, IReadOnlyList<byte[]> tickHashes, byte[] finalHash)
    {
        FinalState = finalState ?? throw new ArgumentNullException(nameof(finalState));
        TickHashes = tickHashes ?? throw new ArgumentNullException(nameof(tickHashes));
        FinalHash = finalHash ?? throw new ArgumentNullException(nameof(finalHash));
    }

    public SimulationState FinalState { get; }
    public IReadOnlyList<byte[]> TickHashes { get; }
    public byte[] FinalHash { get; }
}

/// <summary>
/// Exception thrown when a replay diverges from the expected state at a specific tick.
/// </summary>
public sealed class ReplayDivergenceException : Exception
{
    public ReplayDivergenceException(long tick, byte[] expectedHash, byte[] actualHash)
        : base($"Replay diverged at tick {tick}.")
    {
        Tick = tick;
        ExpectedHash = expectedHash;
        ActualHash = actualHash;
    }

    public long Tick { get; }
    public byte[] ExpectedHash { get; }
    public byte[] ActualHash { get; }
}

public sealed class ReplayRunner
{
    private readonly IStateHasher _stateHasher;

    public ReplayRunner(IStateHasher? stateHasher = null)
    {
        _stateHasher = stateHasher ?? new StateHasher();
    }

    public ReplayResult Replay(InputLog inputLog)
    {
        ArgumentNullException.ThrowIfNull(inputLog);

        var state = CreateInitialState(inputLog.Seed);
        var tickHashes = new List<byte[]>(inputLog.Entries.Count);

        foreach (var entry in inputLog.Entries)
        {
            state = Simulation.Step(state, entry.Action, entry.Tick);
            tickHashes.Add(_stateHasher.HashTick(entry.Tick, state));
        }

        var finalHash = _stateHasher.HashReplay(inputLog, tickHashes);
        return new ReplayResult(state, tickHashes, finalHash);
    }

    /// <summary>
    /// Replays the input log and validates every tick hash against the expected hashes.
    /// Throws <see cref="ReplayDivergenceException"/> immediately on the first mismatch,
    /// reporting the tick at which divergence occurred.
    /// </summary>
    public ReplayResult ValidateReplay(InputLog inputLog, IReadOnlyList<byte[]> expectedTickHashes)
    {
        ArgumentNullException.ThrowIfNull(inputLog);
        ArgumentNullException.ThrowIfNull(expectedTickHashes);

        if (inputLog.Entries.Count != expectedTickHashes.Count)
        {
            throw new ArgumentException(
                $"Expected {expectedTickHashes.Count} tick hashes but input log has {inputLog.Entries.Count} entries.",
                nameof(expectedTickHashes));
        }

        var state = CreateInitialState(inputLog.Seed);
        var tickHashes = new List<byte[]>(inputLog.Entries.Count);

        for (var i = 0; i < inputLog.Entries.Count; i++)
        {
            var entry = inputLog.Entries[i];
            state = Simulation.Step(state, entry.Action, entry.Tick);
            var actualHash = _stateHasher.HashTick(entry.Tick, state);

            if (!actualHash.AsSpan().SequenceEqual(expectedTickHashes[i]))
            {
                throw new ReplayDivergenceException(entry.Tick, expectedTickHashes[i], actualHash);
            }

            tickHashes.Add(actualHash);
        }

        var finalHash = _stateHasher.HashReplay(inputLog, tickHashes);
        return new ReplayResult(state, tickHashes, finalHash);
    }

    public static SimulationState CreateInitialState(int seed)
    {
        var random = new SeededRandom(seed);

        return new SimulationState(
            new SimulationTime(),
            new RngState(seed, random.State, random.CallCount),
            new SimulationPlayerState(100, 100),
            [new SimulationEnemyState(1, 100, 100)]);
    }
}
