namespace GUNRPG.Application.Gameplay;

public abstract record GameplayLedgerEvent(string EventType);

public sealed record OperatorCreatedLedgerEvent(string Name) : GameplayLedgerEvent("OperatorCreated");

public sealed record RunCompletedLedgerEvent(bool WasSuccessful, string Outcome) : GameplayLedgerEvent("RunCompleted");

public sealed record ItemAcquiredLedgerEvent(string ItemId) : GameplayLedgerEvent("ItemAcquired");

public sealed record ItemLostLedgerEvent(string ItemId) : GameplayLedgerEvent("ItemLost");

public sealed record PlayerDamagedLedgerEvent(float Amount, string Reason) : GameplayLedgerEvent("PlayerDamaged");

public sealed record PlayerHealedLedgerEvent(float Amount, string Reason) : GameplayLedgerEvent("PlayerHealed");

public sealed record XpAwardedLedgerEvent(long Amount, string Reason) : GameplayLedgerEvent("XpAwarded");

public sealed record PerkUnlockedLedgerEvent(string PerkName) : GameplayLedgerEvent("PerkUnlocked");

public sealed record InfilStateChangedLedgerEvent(string State, string Reason) : GameplayLedgerEvent("InfilStateChanged");

public sealed record CombatSessionLedgerEvent(Guid SessionId, string State) : GameplayLedgerEvent("CombatSession");

public sealed record PetStateLedgerEvent(string Action, float Health, float Fatigue, float Injury, float Stress, float Morale, float Hunger, float Hydration)
    : GameplayLedgerEvent("PetStateUpdated");

public sealed record EnemyDamagedLedgerEvent(int Amount, string Reason) : GameplayLedgerEvent("EnemyDamaged");
