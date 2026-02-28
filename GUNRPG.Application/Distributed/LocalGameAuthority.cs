using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GUNRPG.Application.Distributed;

/// <summary>
/// Single-node game authority that applies actions locally using the shared
/// <see cref="IDeterministicGameEngine"/>. Maintains the same action log,
/// state hashing, and interface as <see cref="DistributedAuthority"/> but
/// without any networking or peer replication.
/// </summary>
public sealed class LocalGameAuthority : IGameAuthority
{
    private readonly IDeterministicGameEngine _engine;
    private readonly List<DistributedActionEntry> _actionLog = new();
    private readonly object _lock = new();

    private GameStateDto _currentState;
    private long _nextSequenceNumber;
    private string _currentStateHash;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public LocalGameAuthority(Guid nodeId, IDeterministicGameEngine engine)
    {
        NodeId = nodeId;
        _engine = engine;
        _currentState = new GameStateDto { ActionCount = 0, Operators = new List<GameStateDto.OperatorSnapshot>() };
        _currentStateHash = ComputeHash(_currentState);
    }

    public Guid NodeId { get; }
    public bool IsDesynced => false;

    public Task SubmitActionAsync(PlayerActionDto action, CancellationToken ct = default)
    {
        lock (_lock)
        {
            _currentState = _engine.Step(_currentState, action);
            var hash = ComputeHash(_currentState);
            _currentStateHash = hash;

            _actionLog.Add(new DistributedActionEntry
            {
                SequenceNumber = _nextSequenceNumber++,
                NodeId = NodeId,
                Action = action,
                StateHashAfterApply = hash
            });
        }

        return Task.CompletedTask;
    }

    public GameStateDto GetCurrentState()
    {
        lock (_lock)
        {
            return _currentState;
        }
    }

    public string GetCurrentStateHash()
    {
        lock (_lock)
        {
            return _currentStateHash;
        }
    }

    public IReadOnlyList<DistributedActionEntry> GetActionLog()
    {
        lock (_lock)
        {
            return _actionLog.ToList().AsReadOnly();
        }
    }

    private static string ComputeHash(GameStateDto state)
    {
        var json = JsonSerializer.Serialize(state, SerializerOptions);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes);
    }
}
