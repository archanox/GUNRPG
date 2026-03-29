namespace GUNRPG.Security;

/// <summary>
/// Provides methods for creating and verifying <see cref="RunReceipt"/> certificates.
/// </summary>
/// <remarks>
/// A run receipt is a small cryptographic certificate proving that a run exists and was
/// signed by the authority, without requiring the full replay data. It enables verifiable
/// leaderboards and lightweight run sharing.
/// <para>
/// Workflow:
/// <list type="number">
///   <item>Server verifies run via <see cref="ReplayVerifier.TryVerifyRun"/>.</item>
///   <item>Server creates receipt via <see cref="Create"/>.</item>
///   <item>Player publishes receipt.</item>
///   <item>Third party verifies receipt via <see cref="Verify"/> with only the authority public key.</item>
/// </list>
/// </para>
/// </remarks>
public static class RunReceiptService
{
    /// <summary>
    /// Creates a <see cref="RunReceipt"/> from a verified signed run result.
    /// </summary>
    /// <param name="run">
    /// The signed run result for which to create a receipt.
    /// Must have a non-null <see cref="SignedRunResult.TickMerkleRoot"/>.
    /// </param>
    /// <param name="authority">The session authority whose private key signs the receipt.</param>
    /// <returns>
    /// A <see cref="RunReceipt"/> whose <see cref="RunReceipt.Signature"/> covers:
    /// <c>SessionId || FinalTick || FinalStateHash || TickMerkleRoot</c>.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="run"/> or <paramref name="authority"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="run"/> does not have a <see cref="SignedRunResult.TickMerkleRoot"/>.
    /// </exception>
    public static RunReceipt Create(SignedRunResult run, SessionAuthority authority)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(authority);

        return authority.CreateRunReceipt(run);
    }

    /// <summary>
    /// Verifies a <see cref="RunReceipt"/> against an authority's public key.
    /// </summary>
    /// <remarks>
    /// Verification steps:
    /// <list type="number">
    ///   <item>Validate structural requirements (hash lengths, non-null signature).</item>
    ///   <item>Rebuild the canonical receipt payload hash.</item>
    ///   <item>Verify the Ed25519 signature using the authority's public key.</item>
    /// </list>
    /// </remarks>
    /// <param name="receipt">The receipt to verify.</param>
    /// <param name="authority">
    /// The authority whose public key is used for signature verification.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the receipt is structurally valid and the signature
    /// was produced by <paramref name="authority"/> over the receipt's canonical payload.
    /// <see langword="false"/> otherwise.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="receipt"/> or <paramref name="authority"/> is
    /// <see langword="null"/>.
    /// </exception>
    public static bool Verify(RunReceipt receipt, Authority authority)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(authority);

        if (!receipt.IsStructurallyValid)
            return false;

        var payloadHash = AuthorityCrypto.ComputeReceiptPayloadHash(
            receipt.SessionId,
            receipt.FinalTick,
            receipt.FinalStateHash,
            receipt.TickMerkleRoot);

        return AuthorityCrypto.VerifyHashedPayload(authority.PublicKeyBytes, payloadHash, receipt.Signature);
    }
}
