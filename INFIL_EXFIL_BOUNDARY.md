# Infil/Exfil Boundary Documentation

## Overview

GUNRPG implements a strict architectural boundary between **combat (infil)** and **operator management (exfil)** to ensure data integrity, prevent state corruption, and maintain a clear separation of concerns.

## Key Concepts

### Infil (Combat)
**Infil** refers to the combat phase where operators engage in tactical combat scenarios. During infil:
- Operators use a **copy** of their stats (snapshot)
- Combat state is transient and exists only for the duration of the session
- No operator progression or permanent state changes occur
- Combat produces **outcomes** but does not persist operator changes directly

### Exfil (Operator Management)
**Exfil** refers to the out-of-combat phase where operator progression and management occur. During exfil:
- Operators are represented as event-sourced aggregates
- All state changes are recorded as **append-only events**
- Events are hash-chained for tamper detection
- This is the **ONLY** place where operator events may be committed

## Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                         INFIL (Combat)                          │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  CombatSession                                                  │
│  ├─ Operator (snapshot/copy)                                    │
│  │  ├─ Health, Stamina, etc. (mutable during combat)           │
│  │  └─ Equipment, Stats (read-only from exfil)                 │
│  │                                                              │
│  └─ Produces CombatOutcome                                      │
│     ├─ Damage taken                                             │
│     ├─ XP earned                                                │
│     ├─ Victory/defeat status                                    │
│     └─ Other combat results                                     │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
                           │
                           │ CombatOutcome
                           ▼
┌─────────────────────────────────────────────────────────────────┐
│                        EXFIL (Operator Management)               │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  OperatorExfilService                                           │
│  ├─ ProcessCombatOutcome(outcome)                              │
│  │  ├─ Player reviews/confirms outcome                          │
│  │  └─ Translates to operator events                           │
│  │                                                              │
│  └─ Exfil-Only Actions                                          │
│     ├─ ApplyXp()                                                │
│     ├─ TreatWounds()                                            │
│     ├─ ChangeLoadout()                                          │
│     └─ UnlockPerk()                                             │
│                                                                 │
│  OperatorAggregate (Event-Sourced)                              │
│  ├─ State derived from events                                   │
│  ├─ Events are append-only                                      │
│  └─ Events are hash-chained                                     │
│                                                                 │
│  IOperatorEventStore (LiteDB)                                   │
│  └─ Persists events with integrity checks                       │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

## Event Sourcing

### Why Event Sourcing?

Operators are event-sourced to provide:
1. **Complete audit trail** - Every change to an operator is recorded
2. **Tamper detection** - Hash chaining prevents unauthorized modifications
3. **Time-travel debugging** - Can replay events to any point in history
4. **Offline-first design** - Events can be synced across devices (future)
5. **Clear transactional boundaries** - Events are atomic and ordered

### Event Types

All operator events inherit from `OperatorEvent` and include:

- **OperatorCreatedEvent** - Initial operator creation (sequence 0)
- **XpGainedEvent** - Experience points awarded
- **WoundsTreatedEvent** - Health restoration
- **LoadoutChangedEvent** - Equipment changes
- **PerkUnlockedEvent** - Skill/perk unlocks

### Hash Chaining

Each event contains:
- `SequenceNumber` - Monotonically increasing counter
- `PreviousHash` - Hash of the previous event
- `Hash` - SHA256 hash of event contents + previous hash
- `Timestamp` - When the event occurred

This creates a tamper-evident chain:
```
Event 0: Hash A (prev: "")
Event 1: Hash B (prev: A)
Event 2: Hash C (prev: B)
Event 3: Hash D (prev: C)
```

If any event is modified, the hash chain breaks and the corruption is detected immediately on load.

## The Boundary Contract

### Combat MUST NOT:
- ❌ Directly mutate operator aggregate state
- ❌ Append events to the operator event store
- ❌ Persist operator changes
- ❌ Grant XP, unlock perks, or modify loadout

### Combat MUST:
- ✅ Use a **snapshot** of operator stats
- ✅ Produce a **CombatOutcome** at completion
- ✅ Include all relevant combat results in the outcome
- ✅ Remain deterministic and reproducible

### Exfil MUST NOT:
- ❌ Access combat system internals
- ❌ Simulate or execute combat
- ❌ Modify combat state

### Exfil MUST:
- ✅ Be the **ONLY** place events are committed
- ✅ Validate all state changes
- ✅ Verify hash chain integrity on load
- ✅ Allow player confirmation before committing outcomes

## Usage Examples

### Creating an Operator (Exfil)
```csharp
var service = new OperatorExfilService(eventStore);
var result = await service.CreateOperatorAsync("Operative Alpha");
var operatorId = result.Value;
```

### Starting Combat (Infil)
```csharp
// Load operator from exfil
var loadResult = await exfilService.LoadOperatorAsync(operatorId);
var aggregate = loadResult.Value;

// Create combat snapshot
var combatSnapshot = aggregate.CreateCombatSnapshot();

// Start combat session with the snapshot
var session = CombatSession.Create(combatSnapshot);
```

### Completing Combat (Boundary)
```csharp
// Combat produces outcome
var outcome = CombatOutcome.FromSession(session);

// Outcome flows to exfil for processing
var result = await exfilService.ProcessCombatOutcomeAsync(
    outcome, 
    playerConfirmed: true
);
```

### Applying Exfil Actions
```csharp
// Only in exfil - apply XP
await exfilService.ApplyXpAsync(operatorId, 150, "Victory");

// Only in exfil - treat wounds
await exfilService.TreatWoundsAsync(operatorId, 50f);

// Only in exfil - change loadout
await exfilService.ChangeLoadoutAsync(operatorId, "AK-47");

// Only in exfil - unlock perk
await exfilService.UnlockPerkAsync(operatorId, "Fast Reload");

// Record successful exfil (increments streak)
await exfilService.RecordExfilSuccessAsync(operatorId);

// Record explicit exfil failure (resets streak)
await exfilService.RecordExfilFailureAsync(operatorId, "Ran out of time");

// Record operator death (marks as dead, resets streak)
await exfilService.RecordOperatorDeathAsync(operatorId, "Killed in action");
```

## Exfil Streak Tracking

The operator aggregate tracks an **exfil streak** value representing consecutive successful exfils.

### Streak Behavior
- **Increments on**: `ExfilSucceeded` event
- **Resets to 0 on**:
  - `ExfilFailed` event (explicit failure)
  - `OperatorDied` event (operator death)
- **Informational only**: No gameplay effects yet (future feature)

**Note**: The current event-store/aggregate load path fails fast on corruption and does **not** roll back to a last valid event stream; rollback does not affect `ExfilStreak`.

### Dead Operators
Once an `OperatorDied` event is applied:
- `IsDead` flag is set to true
- `CurrentHealth` is set to 0
- `ExfilStreak` is reset to 0
- **No further events can be applied** (enforced by service)

### Example Streak Progression
```csharp
// Operator completes 3 successful exfils -> streak = 3
await exfilService.RecordExfilSuccessAsync(opId); // streak: 1
await exfilService.RecordExfilSuccessAsync(opId); // streak: 2
await exfilService.RecordExfilSuccessAsync(opId); // streak: 3

// Explicit failure resets streak
await exfilService.RecordExfilFailureAsync(opId, "Timeout"); // streak: 0

// Rebuild streak
await exfilService.RecordExfilSuccessAsync(opId); // streak: 1
await exfilService.RecordExfilSuccessAsync(opId); // streak: 2

// Death resets streak and marks operator as dead
await exfilService.RecordOperatorDeathAsync(opId, "KIA"); // streak: 0, IsDead: true
```

## Data Integrity

### Hash Verification
On load, the system verifies:
1. Each event's hash matches its computed hash
2. Each event's previous hash matches the prior event's hash
3. Sequence numbers are consecutive with no gaps

If any check fails, the load fails with a clear error indicating tampering or corruption.

### Append Constraints
When appending events:
1. Sequence must be exactly CurrentSequence + 1
2. Previous hash must match the last event's hash
3. Event hash must verify correctly
4. No concurrent modifications (enforced by store)

## Future Enhancements

### Planned Features
- **Multi-player sync** - Events can be synced across clients
- **Replay system** - Rebuild operator state at any point in time
- **Digital signatures** - Add cryptographic signing for authentication
- **Conflict resolution** - Handle concurrent modifications gracefully
- **Event migrations** - Versioning and schema evolution

### Not Included Yet
- ❌ Cryptographic keys (hash-only for now)
- ❌ Authentication/authorization
- ❌ Network sync
- ❌ Conflict resolution for concurrent edits

## Testing

The implementation includes comprehensive tests:
- `OperatorEventTests` - Event creation, hashing, chain verification
- `OperatorAggregateTests` - Event replay, state derivation
- `LiteDbOperatorEventStoreTests` - Persistence, integrity checks
- `OperatorExfilServiceTests` - Service operations, validation

All 55+ tests pass, ensuring the boundary is maintained correctly.

## Summary

The infil/exfil boundary provides:
- ✅ **Correctness** - Clear separation prevents state corruption
- ✅ **Integrity** - Hash chaining detects tampering
- ✅ **Clarity** - Explicit boundaries make the system easier to reason about
- ✅ **Testability** - Each side can be tested independently
- ✅ **Future-proof** - Event sourcing enables powerful future features

This design favors **correctness, integrity, and clarity over convenience**, as specified in the requirements.
