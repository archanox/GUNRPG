namespace GUNRPG.Core.Simulation;

/// <summary>
/// Per-tick state record capturing the tick number and the deterministic hash of the simulation
/// state produced after processing that tick's input.
/// </summary>
/// <param name="Tick">The simulation tick number.</param>
/// <param name="StateHash">SHA-256 hash of the full simulation state after this tick.</param>
public sealed record TickState(long Tick, byte[] StateHash);
