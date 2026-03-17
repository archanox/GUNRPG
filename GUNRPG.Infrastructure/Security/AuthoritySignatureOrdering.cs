namespace GUNRPG.Security;

internal static class AuthoritySignatureOrdering
{
    internal static string CreateSignerId(byte[] publicKey)
    {
        ArgumentNullException.ThrowIfNull(publicKey);
        return Convert.ToBase64String(publicKey);
    }

    internal static int Compare(
        string leftSignerId,
        ReadOnlySpan<byte> leftSignature,
        string rightSignerId,
        ReadOnlySpan<byte> rightSignature)
    {
        var signerComparison = StringComparer.Ordinal.Compare(leftSignerId, rightSignerId);
        return signerComparison != 0
            ? signerComparison
            : leftSignature.SequenceCompareTo(rightSignature);
    }
}
