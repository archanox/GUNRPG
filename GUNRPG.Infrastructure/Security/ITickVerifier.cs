using GUNRPG.Core.Simulation;

namespace GUNRPG.Security;

/// <summary>
/// Abstraction for a node that can verify per-tick simulation state using an authority's public key.
/// This is the minimal interface required by validator and client nodes, which never have access
/// to the authority's private key.
/// Implemented by <see cref="SessionAuthority"/> and can be mocked in tests.
/// </summary>
public interface ITickVerifier
{
    /// <summary>Ed25519 public key of this authority node (32 bytes).</summary>
    byte[] PublicKey { get; }

    /// <summary>
    /// Verifies the Ed25519 signature on a <see cref="SignedTick"/> using this authority's public key.
    /// Does not check hash-chain continuity; use
    /// <see cref="TickAuthorityService.VerifySignedTickOrThrow"/> which also verifies
    /// <see cref="SignedTick.PrevStateHash"/> linkage and tick continuity.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> if the signature is valid; <see langword="false"/> otherwise.
    /// </returns>
    bool VerifyTick(SignedTick tick);
}
