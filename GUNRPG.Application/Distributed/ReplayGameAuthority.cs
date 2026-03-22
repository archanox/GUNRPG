using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GUNRPG.Application.Distributed;

/// <summary>
/// A game authority that validates determinism by replaying the complete action log from
/// scratch after every submitted action and comparing the resulting hash with the
/// forward-computed hash. Any divergence is reported as a desync.
/// </summary>
/// <remarks>
/// Unlike <see cref="LocalGameAuthority"/>, which only steps the engine forward,
/// <see cref="ReplayGameAuthority"/> re-executes all previous actions on every
/// <see cref="SubmitActionAsync"/> call. This makes it suitable for authority validation
/// (catching non-determinism or tampered action logs) at the cost of O(n²) work per session.
/// </remarks>
public sealed class ReplayGameAuthority : IGameAuthority
{
    private readonly IDeterministicGameEngine _engine;
    private readonly List<DistributedActionEntry> _actionLog = new();
    private readonly object _lock = new();

    private GameStateDto _currentState;
    private long _nextSequenceNumber;
    private string _currentStateHash;
    private bool _isDesynced;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public ReplayGameAuthority(Guid nodeId, IDeterministicGameEngine engine)
    {
        NodeId = nodeId;
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _currentState = CreateInitialState();
        _currentStateHash = ComputeHash(_currentState);
    }

    /// <inheritdoc/>
    public Guid NodeId { get; }

    /// <inheritdoc/>
    public bool IsDesynced => _isDesynced;

    /// <inheritdoc/>
    public Task SubmitActionAsync(PlayerActionDto action, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(action);

        lock (_lock)
        {
            // Step 1: Apply the action to the running state.
            _currentState = _engine.Step(_currentState, action);
            var forwardHash = ComputeHash(_currentState);

            _actionLog.Add(new DistributedActionEntry
            {
                SequenceNumber = _nextSequenceNumber++,
                NodeId = NodeId,
                Action = action,
                StateHashAfterApply = forwardHash
            });

            // Step 2: Replay the entire action log from scratch.
            var replayedState = ReplayAllActions(_actionLog);
            var replayHash = ComputeHash(replayedState);

            // Step 3: Detect desync — any hash divergence means the engine is non-deterministic
            // or the action log has been tampered with.
            if (!string.Equals(forwardHash, replayHash, StringComparison.Ordinal))
            {
                _isDesynced = true;
            }
            else
            {
                _currentStateHash = forwardHash;
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public GameStateDto GetCurrentState()
    {
        lock (_lock)
        {
            return _currentState;
        }
    }

    /// <inheritdoc/>
    public string GetCurrentStateHash()
    {
        lock (_lock)
        {
            return _currentStateHash;
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<DistributedActionEntry> GetActionLog()
    {
        lock (_lock)
        {
            return _actionLog.ToList().AsReadOnly();
        }
    }

    /// <summary>
    /// Replays all entries in <paramref name="log"/> from the initial empty state,
    /// producing the deterministic final game state.
    /// </summary>
    private GameStateDto ReplayAllActions(IReadOnlyList<DistributedActionEntry> log)
    {
        var state = CreateInitialState();
        foreach (var entry in log)
        {
            state = _engine.Step(state, entry.Action);
        }

        return state;
    }

    private static GameStateDto CreateInitialState() =>
        new() { ActionCount = 0, Operators = new List<GameStateDto.OperatorSnapshot>() };

    private static string ComputeHash(GameStateDto state)
    {
        var json = JsonSerializer.Serialize(state, SerializerOptions);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes);
    }
}
