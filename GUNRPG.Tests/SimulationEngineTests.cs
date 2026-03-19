using System.Security.Cryptography;
using GUNRPG.Application.Gameplay;
using GUNRPG.Core.Operators;
using GUNRPG.Ledger;
using GUNRPG.Security;
using Xunit;

namespace GUNRPG.Tests;

/// <summary>
/// Validates the deterministic tick-based simulation engine introduced by
/// <see cref="SimulationEngine"/> and exercised via <see cref="RunReplayEngine"/>.
/// </summary>
public sealed class SimulationEngineTests
{
    private static readonly DateTimeOffset ReferenceNow = new(2026, 03, 15, 04, 00, 00, TimeSpan.Zero);

    // ── Simulation_IsDeterministic ───────────────────────────────────────────────

    /// <summary>
    /// Same RunInput (identical Actions + Seed) must always produce byte-identical events,
    /// FinalStateHash, and attestation signature — even across separate engine instances.
    /// </summary>
    [Fact]
    public void Simulation_IsDeterministic()
    {
        var serverIdentity = CreateServerIdentity();
        var input = CreateStandardInput();

        var engineA = new RunReplayEngine(serverIdentity);
        var engineB = new RunReplayEngine(serverIdentity);

        var resultA = engineA.Replay(input);
        var resultB = engineB.Replay(input);

        // FinalStateHash must be identical — it is computed from input + derived events.
        Assert.True(resultA.FinalStateHash.SequenceEqual(resultB.FinalStateHash),
            "FinalStateHash must be identical for identical RunInput.");

        // ResultHash must be identical.
        Assert.True(resultA.ComputeResultHash().SequenceEqual(resultB.ComputeResultHash()),
            "ComputeResultHash() must be identical for identical RunInput.");

        // Attestation signature must be identical.
        Assert.True(resultA.Attestation.Validation.Signature.SequenceEqual(resultB.Attestation.Validation.Signature),
            "Attestation signature must be identical for identical RunInput.");

        // Event sequence must be structurally equal (record value equality).
        Assert.Equal(resultA.Events.Count, resultB.Events.Count);
        for (var i = 0; i < resultA.Events.Count; i++)
        {
            Assert.Equal(resultA.Events[i], resultB.Events[i]);
        }
    }

    // ── Simulation_ProducesExpectedEvents ────────────────────────────────────────

    /// <summary>
    /// A known RunInput must produce specific, expected GameplayLedgerEvents.
    /// Seed 0 with [MoveAction(North), AttackAction(...), UseItemAction(...), ExfilAction()]
    /// must always yield: InfilStateChanged, ItemAcquired, PlayerHealed, RunCompleted.
    /// The AttackAction may also produce an EnemyDamagedLedgerEvent depending on the RNG outcome.
    /// </summary>
    [Fact]
    public void Simulation_ProducesExpectedEvents()
    {
        var serverIdentity = CreateServerIdentity();
        var engine = new RunReplayEngine(serverIdentity);
        var targetId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var itemId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

        var input = new RunInput
        {
            RunId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            PlayerId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
            Seed = 0,
            Actions =
            [
                new MoveAction(Direction.North),
                new AttackAction(targetId),
                new UseItemAction(itemId),
                new ExfilAction()
            ]
        };

        var result = engine.Replay(input);

        // Move always emits InfilStateChanged.
        Assert.Contains(result.Events, e => e is InfilStateChangedLedgerEvent move
            && move.State == "Moving" && move.Reason == "North");

        // UseItem always emits ItemAcquired and PlayerHealed.
        Assert.Contains(result.Events, e => e is ItemAcquiredLedgerEvent item
            && item.ItemId == itemId.ToString("N"));
        Assert.Contains(result.Events, e => e is PlayerHealedLedgerEvent);

        // ExfilAction always emits RunCompleted.
        Assert.Contains(result.Events, e => e is RunCompletedLedgerEvent exfil
            && exfil.WasSuccessful && exfil.Outcome == "Exfil");

        // Events list is non-empty.
        Assert.NotEmpty(result.Events);
    }

    // ── Simulation_StableAcrossRuns ──────────────────────────────────────────────

    /// <summary>
    /// Running the same RunInput many times must always produce the same SHA-256 hash
    /// of the serialised event sequence (byte-stable across runs).
    /// </summary>
    [Fact]
    public void Simulation_StableAcrossRuns()
    {
        var serverIdentity = CreateServerIdentity();
        var input = CreateStandardInput();
        const int runs = 10;

        var hashes = new HashSet<string>();
        for (var i = 0; i < runs; i++)
        {
            var engine = new RunReplayEngine(serverIdentity);
            var result = engine.Replay(input);
            var hash = ComputeEventSequenceHash(result.Events);
            hashes.Add(hash);
        }

        Assert.True(hashes.Count == 1, "All runs must produce an identical event-sequence hash.");
    }

    // ── Helper methods ───────────────────────────────────────────────────────────

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

    /// <summary>
    /// Computes a stable SHA-256 hex string over the ordered event sequence using the
    /// deterministic binary serialization already defined in <see cref="RunLedgerMutation"/>.
    /// Avoids <c>ToString()</c> which can vary with <c>CultureInfo.CurrentCulture</c>.
    /// </summary>
    private static string ComputeEventSequenceHash(IReadOnlyList<GameplayLedgerEvent> events)
    {
        var mutation = new RunLedgerMutation([], events);
        return Convert.ToHexString(mutation.ComputeHash());
    }

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
