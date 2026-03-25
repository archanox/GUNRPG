namespace GUNRPG.Core.Simulation;

/// <summary>
/// Per-tick input record capturing the tick number and a hash of the player's input for that tick.
/// Used for per-tick server-authoritative replay validation.
/// </summary>
/// <param name="Tick">The simulation tick number.</param>
/// <param name="InputHash">
/// SHA-256 hash of the serialized player input for this tick.
/// The caller is responsible for defensive copying; the record does not clone the array.
/// </param>
public sealed record TickInput(long Tick, byte[] InputHash);
