using GUNRPG.Application.Gameplay;
using GUNRPG.Core.Operators;
using GUNRPG.Ledger;

namespace GUNRPG.Tests;

public sealed class RunLedgerMutationTests
{
    [Fact]
    public void ComputeHash_DistinguishesStringFieldBoundaries()
    {
        var mutationA = new RunLedgerMutation(
            [],
            [new InfilStateChangedLedgerEvent("A", "B|C")]);
        var mutationB = new RunLedgerMutation(
            [],
            [new InfilStateChangedLedgerEvent("A|B", "C")]);

        var hashA = mutationA.ComputeHash();
        var hashB = mutationB.ComputeHash();

        Assert.False(hashA.SequenceEqual(hashB));
    }

    [Fact]
    public void Constructor_RejectsNullGameplayEventEntries()
    {
        Assert.Throws<ArgumentException>(() => new RunLedgerMutation(
            [],
            [null!]));
    }

    [Fact]
    public void Constructor_RejectsNullOperatorEventEntries()
    {
        Assert.Throws<ArgumentException>(() => new RunLedgerMutation(
            [null!],
            []));
    }
}
