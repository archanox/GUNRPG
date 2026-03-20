using GUNRPG.Core.Simulation;
using GUNRPG.Security;
using Xunit;

namespace GUNRPG.Tests;

/// <summary>
/// Validates the core deterministic simulation entry point and replay pipeline.
/// </summary>
public sealed class SimulationEngineTests
{
    private static readonly DateTimeOffset ReferenceNow = new(2026, 03, 15, 04, 00, 00, TimeSpan.Zero);

    [Fact]
    public void Simulation_Step_ReplaysDeterministicallyAcrossInstances()
    {
        var input = CreateStandardInput();
        var log = InputLog.FromRunInput(input);
        var runnerA = new ReplayRunner();
        var runnerB = new ReplayRunner();

        var resultA = runnerA.Replay(log);
        var resultB = runnerB.Replay(log);

        Assert.Equal(resultA.FinalState.Time.CurrentTimeMs, resultB.FinalState.Time.CurrentTimeMs);
        Assert.Equal(resultA.FinalState.Player.Health, resultB.FinalState.Player.Health);
        Assert.Equal(resultA.FinalState.Player.MaxHealth, resultB.FinalState.Player.MaxHealth);
        Assert.Equal(resultA.FinalState.Enemies.Count, resultB.FinalState.Enemies.Count);
        Assert.Equal(resultA.FinalState.Events, resultB.FinalState.Events);
        Assert.Equal(resultA.TickHashes.Count, resultB.TickHashes.Count);

        for (var i = 0; i < resultA.TickHashes.Count; i++)
        {
            Assert.True(resultA.TickHashes[i].SequenceEqual(resultB.TickHashes[i]));
        }

        Assert.True(resultA.FinalHash.SequenceEqual(resultB.FinalHash));
    }

    [Fact]
    public void Simulation_Step_UsesSimulationTimeAsSingleClock()
    {
        var state = ReplayRunner.CreateInitialState(seed: 7);

        var afterMove = Simulation.Step(state, new MoveAction(Direction.North), tick: 0);
        Assert.Equal(1, afterMove.Time.CurrentTimeMs);
        Assert.Contains(afterMove.LastStepEvents, evt => evt is InfilStateChangedSimulationEvent move
            && move.State == "Moving"
            && move.Reason == "North");

        var afterExfil = Simulation.Step(afterMove, new ExfilAction(), tick: 1);
        Assert.Equal(2, afterExfil.Time.CurrentTimeMs);
        Assert.Contains(afterExfil.LastStepEvents, evt => evt is RunCompletedSimulationEvent completed && completed.WasSuccessful);
    }

    [Fact]
    public void ReplayRunner_MatchesSecurityReplayValidationHash()
    {
        var serverIdentity = CreateServerIdentity();
        var input = CreateStandardInput();
        var replayRunner = new ReplayRunner();
        var replayResult = replayRunner.Replay(InputLog.FromRunInput(input));
        var replayEngine = new RunReplayEngine(serverIdentity);

        var validationResult = replayEngine.Replay(input);

        Assert.True(replayResult.FinalHash.SequenceEqual(validationResult.FinalStateHash));
    }

    [Fact]
    public void ReplayRunner_UsesCanonicalInputLogOrderingForExecutionAndHashing()
    {
        var move = new MoveAction(Direction.North);
        var attack = new AttackAction(Guid.Parse("33333333-3333-3333-3333-333333333333"));
        var exfil = new ExfilAction();

        var unsortedLog = new InputLog(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            12345,
            [
                new InputLogEntry(2, exfil),
                new InputLogEntry(0, move),
                new InputLogEntry(1, attack)
            ]);

        var sortedLog = new InputLog(
            unsortedLog.RunId,
            unsortedLog.PlayerId,
            unsortedLog.Seed,
            [
                new InputLogEntry(0, move),
                new InputLogEntry(1, attack),
                new InputLogEntry(2, exfil)
            ]);

        var runner = new ReplayRunner();
        var unsortedReplay = runner.Replay(unsortedLog);
        var sortedReplay = runner.Replay(sortedLog);

        Assert.Equal(sortedLog.Entries, unsortedLog.Entries);
        Assert.Equal(sortedReplay.FinalState.Events, unsortedReplay.FinalState.Events);
        Assert.True(sortedReplay.FinalHash.SequenceEqual(unsortedReplay.FinalHash));
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

    private static ServerIdentity CreateServerIdentity()
    {
        var rootPrivateKey = CertificateIssuer.GeneratePrivateKey();
        var certificateIssuer = new CertificateIssuer(rootPrivateKey);

        var serverPrivateKey = ServerIdentity.GeneratePrivateKey();
        var certificate = certificateIssuer.IssueServerCertificate(
            Guid.NewGuid(),
            ServerIdentity.GetPublicKey(serverPrivateKey),
            ReferenceNow.AddMinutes(-5),
            ReferenceNow.AddMinutes(30));

        return new ServerIdentity(certificate, serverPrivateKey);
    }
}
