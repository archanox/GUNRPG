namespace GUNRPG.Security;

public sealed record QuorumPolicy
{
    public QuorumPolicy(int requiredSignatures)
    {
        if (requiredSignatures <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requiredSignatures), "Required signatures must be greater than zero.");
        }

        RequiredSignatures = requiredSignatures;
    }

    public int RequiredSignatures { get; }
}
