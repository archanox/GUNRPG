namespace GUNRPG.Core.Simulation;

public abstract record SimulationEvent(string EventType);

public sealed record InfilStateChangedSimulationEvent(string State, string Reason)
    : SimulationEvent("InfilStateChanged");

public sealed record RunCompletedSimulationEvent(bool WasSuccessful, string Outcome)
    : SimulationEvent("RunCompleted");

public sealed record ItemAcquiredSimulationEvent(string ItemId)
    : SimulationEvent("ItemAcquired");

public sealed record PlayerDamagedSimulationEvent(int Amount, string Reason)
    : SimulationEvent("PlayerDamaged");

public sealed record PlayerHealedSimulationEvent(int Amount, string Reason)
    : SimulationEvent("PlayerHealed");

public sealed record EnemyDamagedSimulationEvent(int Amount, string Reason)
    : SimulationEvent("EnemyDamaged");
