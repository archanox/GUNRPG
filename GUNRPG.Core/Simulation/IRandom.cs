namespace GUNRPG.Core.Simulation;

public interface IRandom
{
    int Seed { get; }
    int CallCount { get; }
    ulong State { get; }
    int Next(int minValue, int maxValue);
}
