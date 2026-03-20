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

        foreach (var entry in inputLog.Entries.OrderBy(e => e.Tick))
        {
            state = Simulation.Step(state, entry.Action, entry.Tick);
            tickHashes.Add(_stateHasher.HashTick(entry.Tick, state));
        }

        var finalHash = _stateHasher.HashReplay(inputLog, tickHashes);
        return new ReplayResult(state, tickHashes, finalHash);
    }

    public static SimulationState CreateInitialState(int seed)
    {
        return new SimulationState(
            new SimulationTime(),
            new RngState(seed, 0),
            new SimulationPlayerState(100, 100),
            [new SimulationEnemyState(1, 100, 100)]);
    }
}
