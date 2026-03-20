using GUNRPG.Core.Simulation;
using GUNRPG.Security;
using Xunit;

namespace GUNRPG.Tests;

/// <summary>
/// Strengthened tests proving the deterministic simulation system is correct:
/// - Same input → same hash across multiple runs
/// - Live run vs replay validation equality
/// - Seeded randomness consistency
/// - Mismatch detection via ReplayDivergenceException
/// - QuorumValidator.ReplayHash convenience method
/// </summary>
public sealed class DeterministicSimulationTests
{
    private static readonly DateTimeOffset ReferenceNow = new(2026, 03, 15, 04, 00, 00, TimeSpan.Zero);

    [Fact]
    public void SameInput_ProducesSameHash_AcrossMultipleRuns()
    {
        var input = CreateStandardInput();
        var log = InputLog.FromRunInput(input);
        byte[]? referenceHash = null;

        for (var run = 0; run < 10; run++)
        {
            var runner = new ReplayRunner();
            var result = runner.Replay(log);

            if (referenceHash is null)
            {
                referenceHash = result.FinalHash;
            }
            else
            {
                Assert.True(
                    referenceHash.AsSpan().SequenceEqual(result.FinalHash),
                    $"Hash diverged on run {run}.");
            }
        }
    }

    [Fact]
    public void LiveRun_EqualsReplayValidation()
    {
        var input = CreateStandardInput();
        var log = InputLog.FromRunInput(input);

        // "Live" run
        var liveRunner = new ReplayRunner();
        var liveResult = liveRunner.Replay(log);

        // "Replay" validation using the live run's tick hashes
        var replayRunner = new ReplayRunner();
        var replayResult = replayRunner.ValidateReplay(log, liveResult.TickHashes);

        Assert.True(liveResult.FinalHash.AsSpan().SequenceEqual(replayResult.FinalHash));
        Assert.Equal(liveResult.FinalState.Time.CurrentTimeMs, replayResult.FinalState.Time.CurrentTimeMs);
        Assert.Equal(liveResult.FinalState.Player.Health, replayResult.FinalState.Player.Health);
        Assert.Equal(liveResult.FinalState.Events, replayResult.FinalState.Events);
    }

    [Fact]
    public void SeededRandom_IsConsistent_AcrossInstances()
    {
        const int seed = 42;

        for (var run = 0; run < 5; run++)
        {
            var rngA = new SeededRandom(seed);
            var rngB = new SeededRandom(seed);

            for (var call = 0; call < 100; call++)
            {
                Assert.Equal(rngA.Next(0, 1000), rngB.Next(0, 1000));
            }

            Assert.Equal(rngA.State, rngB.State);
            Assert.Equal(rngA.CallCount, rngB.CallCount);
        }
    }

    [Fact]
    public void ValidateReplay_DetectsMismatch_AndReportsDivergentTick()
    {
        var input = CreateStandardInput();
        var log = InputLog.FromRunInput(input);
        var runner = new ReplayRunner();
        var result = runner.Replay(log);

        // Tamper with the tick hash at index 1
        var tamperedHashes = result.TickHashes.Select(h => (byte[])h.Clone()).ToList();
        tamperedHashes[1] = new byte[tamperedHashes[1].Length]; // zero out

        var ex = Assert.Throws<ReplayDivergenceException>(
            () => runner.ValidateReplay(log, tamperedHashes));

        Assert.Equal(1, ex.Tick); // tick 1 is the second entry (tick 0-indexed matching entry index)
    }

    [Fact]
    public void ValidateReplay_AcceptsMatchingHashes()
    {
        var input = CreateStandardInput();
        var log = InputLog.FromRunInput(input);
        var runner = new ReplayRunner();
        var firstResult = runner.Replay(log);

        // No exception = success
        var validated = runner.ValidateReplay(log, firstResult.TickHashes);
        Assert.True(firstResult.FinalHash.AsSpan().SequenceEqual(validated.FinalHash));
    }

    [Fact]
    public void ValidateReplay_RejectsWrongEntryCount()
    {
        var input = CreateStandardInput();
        var log = InputLog.FromRunInput(input);
        var runner = new ReplayRunner();

        Assert.Throws<ArgumentException>(() => runner.ValidateReplay(log, []));
    }

    [Fact]
    public void QuorumValidator_ReplayHash_IsDeterministic()
    {
        var input = CreateStandardInput();

        var hashA = QuorumValidator.ReplayHash(input);
        var hashB = QuorumValidator.ReplayHash(input);

        Assert.True(hashA.AsSpan().SequenceEqual(hashB));
    }

    [Fact]
    public void QuorumValidator_ReplayHash_MatchesReplayRunnerHash()
    {
        var input = CreateStandardInput();
        var runner = new ReplayRunner();
        var log = InputLog.FromRunInput(input);
        var replayResult = runner.Replay(log);

        var quorumHash = QuorumValidator.ReplayHash(input);

        Assert.True(replayResult.FinalHash.AsSpan().SequenceEqual(quorumHash));
    }

    [Fact]
    public void QuorumValidator_ReplayHash_DiffersForDifferentSeeds()
    {
        var inputA = new RunInput
        {
            RunId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            PlayerId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Seed = 100,
            Actions = [new ExfilAction()]
        };

        var inputB = new RunInput
        {
            RunId = inputA.RunId,
            PlayerId = inputA.PlayerId,
            Seed = 200,
            Actions = [new ExfilAction()]
        };

        var hashA = QuorumValidator.ReplayHash(inputA);
        var hashB = QuorumValidator.ReplayHash(inputB);

        Assert.False(hashA.AsSpan().SequenceEqual(hashB));
    }

    [Fact]
    public void InputFrame_IsPopulatedFromInputLog()
    {
        var input = CreateStandardInput();
        var log = InputLog.FromRunInput(input);

        Assert.Equal(input.Actions.Count, log.Frames.Count);
        for (var i = 0; i < log.Frames.Count; i++)
        {
            Assert.Equal(i, log.Frames[i].Tick);
            Assert.Equal(input.PlayerId, log.Frames[i].PlayerId);
            Assert.Equal(input.Actions[i], log.Frames[i].Intent);
        }
    }

    [Fact]
    public void DifferentActions_ProduceDifferentHashes()
    {
        var inputA = new RunInput
        {
            RunId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            PlayerId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Seed = 42,
            Actions = [new MoveAction(Direction.North), new ExfilAction()]
        };

        var inputB = new RunInput
        {
            RunId = inputA.RunId,
            PlayerId = inputA.PlayerId,
            Seed = inputA.Seed,
            Actions = [new MoveAction(Direction.South), new ExfilAction()]
        };

        var hashA = QuorumValidator.ReplayHash(inputA);
        var hashB = QuorumValidator.ReplayHash(inputB);

        Assert.False(hashA.AsSpan().SequenceEqual(hashB));
    }

    private static RunInput CreateStandardInput() => new()
    {
        RunId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
        PlayerId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
        Seed = 12345,
        Actions =
        [
            new MoveAction(Direction.North),
            new AttackAction(Guid.Parse("33333333-3333-3333-3333-333333333333")),
            new UseItemAction(Guid.Parse("44444444-4444-4444-4444-444444444444")),
            new ExfilAction()
        ]
    };
}
