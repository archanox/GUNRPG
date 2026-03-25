namespace GUNRPG.Core.Simulation;

/// <summary>
/// An authority-signed simulation tick.
/// The Ed25519 signature covers:
/// Tick (big-endian int64) || PrevStateHash || StateHash || InputHash.
/// Including <see cref="PrevStateHash"/> in the signature prevents valid ticks from
/// being replayed or spliced from a different timeline.
/// Produced by the authority node every <see cref="GUNRPG.Security.TickAuthorityService.SignInterval"/> ticks.
/// </summary>
/// <param name="Tick">The simulation tick number.</param>
/// <param name="PrevStateHash">
/// SHA-256 hash of the simulation state at the end of the <em>previous</em> signed checkpoint tick.
/// Use <see cref="TickAuthorityService.GenesisStateHash"/> for the very first tick.
/// The caller is responsible for defensive copying; the record does not clone the array.
/// </param>
/// <param name="StateHash">
/// SHA-256 hash of the simulation state after this tick.
/// The caller is responsible for defensive copying; the record does not clone the array.
/// </param>
/// <param name="InputHash">
/// SHA-256 hash of the player input(s) that drove this tick.
/// The caller is responsible for defensive copying; the record does not clone the array.
/// </param>
/// <param name="Signature">
/// Ed25519 signature over the canonical payload (Tick || PrevStateHash || StateHash || InputHash).
/// The caller is responsible for defensive copying; the record does not clone the array.
/// </param>
public sealed record SignedTick(
    long Tick,
    byte[] PrevStateHash,
    byte[] StateHash,
    byte[] InputHash,
    byte[] Signature);
