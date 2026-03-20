namespace GUNRPG.Core.Simulation;

public interface IStateHasher
{
    byte[] HashTick(long tick, SimulationState state);
    byte[] HashReplay(InputLog inputLog, IReadOnlyList<byte[]> tickHashes);
}
