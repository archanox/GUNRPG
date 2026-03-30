namespace GUNRPG.Security;

/// <summary>
/// The operational role of a node within the run-receipt authority trust model.
/// </summary>
/// <remarks>
/// Role is determined at startup by checking whether the node's private key
/// corresponds to a public key registered in the <see cref="AuthorityRegistry"/>.
/// </remarks>
public enum AuthorityRole
{
    /// <summary>
    /// A verifier node can replay and verify runs, but cannot sign run receipts.
    /// </summary>
    Verifier,

    /// <summary>
    /// An authority node can verify runs and sign run receipts.
    /// Authority nodes possess a private key whose public key is listed in the
    /// <see cref="AuthorityRegistry"/>.
    /// </summary>
    Authority,
}
