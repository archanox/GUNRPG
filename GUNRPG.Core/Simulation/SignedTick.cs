namespace GUNRPG.Core.Simulation;

/// <summary>
/// An authority-signed simulation tick.
/// The Ed25519 signature covers: Tick (big-endian int64) ‖ StateHash ‖ InputHash.
/// Produced by the authority node every <see cref="GUNRPG.Security.TickAuthorityService.SignInterval"/> ticks.
/// </summary>
/// <param name="Tick">The simulation tick number.</param>
/// <param name="StateHash">
/// SHA-256 hash of the simulation state after this tick.
/// The caller is responsible for defensive copying; the record does not clone the array.
/// </param>
/// <param name="InputHash">
/// SHA-256 hash of the player input that drove this tick.
/// The caller is responsible for defensive copying; the record does not clone the array.
/// </param>
/// <param name="Signature">
/// Ed25519 signature over the canonical payload (Tick ‖ StateHash ‖ InputHash).
/// The caller is responsible for defensive copying; the record does not clone the array.
/// </param>
public sealed record SignedTick(long Tick, byte[] StateHash, byte[] InputHash, byte[] Signature);
