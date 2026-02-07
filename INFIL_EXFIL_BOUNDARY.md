# Infil/Exfil Boundary Documentation

This document describes the strict separation between combat (infil) and exfil phases in GUNRPG's operator lifecycle.

## Overview

The system enforces a clean boundary between:
- **Combat/Infil**: Where operators engage in tactical combat using snapshots of their state
- **Exfil**: Where operator progression is committed via append-only events

This boundary ensures:
- Combat sessions cannot directly mutate operator state
- All operator changes are tamper-evident and auditable
- State can be safely rolled back if corruption is detected
- Deterministic replay of operator history

## Core Architecture

### Operator Aggregate (Event-Sourced)

Operators are event-sourced aggregates that derive their state from a hash-chained event stream:

```
OperatorEvent (base)
├── OperatorCreated     (genesis event)
├── ExfilSucceeded      (increments streak, adds XP)
├── ExfilFailed         (resets streak)
└── OperatorDied        (resets streak, tracks death)
```

Each event includes:
- `OperatorId`: Owner of the event
- `SequenceNumber`: Monotonically increasing (1, 2, 3, ...)
- `EventType`: Name of the event class
- `PreviousHash`: SHA256 hash of previous event (null for genesis)
- `Hash`: SHA256 hash of this event's content
- `Timestamp`: When the event was created
- Payload: Event-specific data

### Hash Chain Integrity

Events form a tamper-evident chain:

```
Event 1 (Genesis)           Event 2                    Event 3
┌──────────────────┐        ┌──────────────────┐       ┌──────────────────┐
│ PreviousHash: ∅  │───────>│ PreviousHash: H1 │──────>│ PreviousHash: H2 │
│ Hash: H1         │        │ Hash: H2         │       │ Hash: H3         │
└──────────────────┘        └──────────────────┘       └──────────────────┘
```

On load:
1. Events are loaded in sequence order
2. Each event's hash is verified (content matches `Hash` field)
3. Each event's `PreviousHash` matches the previous event's `Hash`
4. If verification fails at any point:
   - All events from that point forward are discarded
   - The system rolls back to the last valid event
   - No attempt is made to repair or rewrite corrupted events

### Operator States

#### Combat Snapshot (Read-Only)
```csharp
OperatorCombatSnapshot
{
    OperatorId: Guid
    Name: string
    ExfilStreak: int
    TotalExperience: int
}
```

Combat sessions receive a read-only snapshot when they start. This snapshot is:
- Immutable during combat
- Never written back to the operator aggregate
- Used only for display and context

#### Operator Aggregate (Event-Sourced)
```csharp
OperatorAggregate
{
    Id: Guid
    Name: string
    ExfilStreak: int           // Consecutive successful exfils
    TotalExperience: int
    SuccessfulExfils: int
    FailedExfils: int
    Deaths: int
    IsAlive: bool
}
```

The aggregate is reconstructed by replaying all events in sequence.

## Infil/Exfil Flow

### 1. Infil (Combat Start)

```
Combat Session Creation
         │
         ├─> Load Operator (via OperatorExfilService)
         │
         ├─> Get Combat Snapshot (read-only)
         │
         └─> Start Combat with Snapshot
```

**Key Rules:**
- Combat session receives a snapshot, not a reference to the aggregate
- Operator aggregate is not loaded into the combat session
- No events are emitted during combat
- Combat can read operator stats but cannot modify them

### 2. Combat Execution

```
Combat Loop
    │
    ├─> Player submits intents
    ├─> AI submits intents
    ├─> Events resolve (shots, movement, etc.)
    ├─> Check victory conditions
    │
    └─> Either:
        ├─> Player wins → Proceed to Exfil
        ├─> Player dies → Record death, no exfil
        └─> Combat continues → Next round
```

**Key Rules:**
- Combat state is ephemeral and session-scoped
- Gear loss, injuries, etc. are tracked in combat session only
- No operator events are emitted during combat execution

### 3. Exfil (Combat End)

#### Successful Exfil
```
Combat Victory
      │
      ├─> OperatorExfilService.CommitSuccessfulExfilAsync()
      │
      ├─> Create ExfilSucceeded event:
      │     - Increment ExfilStreak
      │     - Add Experience
      │     - Link to CombatSessionId
      │
      └─> Append event to store
            │
            └─> Operator progression committed
```

**Result:**
- `ExfilStreak` increments by 1
- Experience is added
- Event is permanently recorded

#### Failed Exfil
```
Combat Loss / Abandonment
      │
      ├─> OperatorExfilService.CommitFailedExfilAsync()
      │
      ├─> Create ExfilFailed event:
      │     - Reset ExfilStreak to 0
      │     - Record reason
      │     - Link to CombatSessionId
      │
      └─> Append event to store
```

**Result:**
- `ExfilStreak` resets to 0
- No experience gained
- Event is permanently recorded

#### Operator Death
```
Operator Dies in Combat
      │
      ├─> OperatorExfilService.RecordOperatorDeathAsync()
      │
      ├─> Create OperatorDied event:
      │     - Reset ExfilStreak to 0
      │     - Record cause of death
      │     - Increment death counter
      │
      └─> Append event to store
```

**Result:**
- `ExfilStreak` resets to 0
- `Deaths` increments
- Event is permanently recorded

## Exfil Streak Rules

The `ExfilStreak` tracks consecutive successful exfils:

| Event | Effect on Streak |
|-------|------------------|
| `ExfilSucceeded` | Increment by 1 |
| `ExfilFailed` | Reset to 0 |
| `OperatorDied` | Reset to 0 |
| Hash chain rollback | Streak reverts to value at last valid event |

### Example Streak Progression

```
Event 1: OperatorCreated        → Streak = 0
Event 2: ExfilSucceeded         → Streak = 1
Event 3: ExfilSucceeded         → Streak = 2
Event 4: ExfilSucceeded         → Streak = 3
Event 5: ExfilFailed            → Streak = 0
Event 6: ExfilSucceeded         → Streak = 1
Event 7: OperatorDied           → Streak = 0
```

## Service Boundaries

### OperatorExfilService (ONLY SERVICE THAT EMITS OPERATOR EVENTS)

**Responsibilities:**
- Create new operators
- Load operator aggregates from event streams
- Commit successful exfil outcomes
- Record failed exfil outcomes
- Record operator deaths
- Provide read-only combat snapshots

**Methods:**
```csharp
Task<ServiceResult<Guid>> CreateOperatorAsync(string name)
Task<ServiceResult<OperatorAggregate>> LoadOperatorAsync(Guid operatorId)
Task<ServiceResult<OperatorAggregate>> CommitSuccessfulExfilAsync(Guid operatorId, Guid combatSessionId, int experienceGained)
Task<ServiceResult<OperatorAggregate>> CommitFailedExfilAsync(Guid operatorId, Guid combatSessionId, string reason)
Task<ServiceResult<OperatorAggregate>> RecordOperatorDeathAsync(Guid operatorId, Guid combatSessionId, string causeOfDeath)
Task<ServiceResult<OperatorCombatSnapshot>> GetCombatSnapshotAsync(Guid operatorId)
```

### CombatSessionService

**Responsibilities:**
- Create and manage combat sessions
- Accept player intents
- Execute combat rounds
- Track combat outcomes

**What it CANNOT do:**
- Emit operator events
- Modify operator aggregate state
- Access operator event store directly

## Persistence

### LiteDbOperatorEventStore

**Collection:** `operator_events`

**Indexes:**
- `OperatorId` (non-unique)
- `SequenceNumber` (non-unique)
- `(OperatorId, SequenceNumber)` (unique composite)

**Verification on Load:**
1. Load all events for an operator, ordered by `SequenceNumber`
2. For each event:
   - Verify `event.Hash` matches recomputed hash
   - Verify `event.PreviousHash` matches previous event's `Hash`
3. If verification fails:
   - Discard current and all subsequent events
   - Delete invalid events from database
   - Return valid events up to break point
   - Set `RolledBack = true` flag

**No Repair Attempted:**
- Invalid events are permanently deleted
- No attempt to rewrite or patch the chain
- System relies on rollback to last known good state

## Security Considerations

### Current Implementation (v1)
- SHA256 hash chaining for tamper evidence
- No cryptographic signatures (no private keys)
- Hashes prevent accidental corruption and detect tampering
- Cannot prove authorship of events

### Future Enhancements (Not Yet Implemented)
- Digital signatures with operator-specific key pairs
- Cryptographic proof of authorship
- Multi-party verification for high-value events

## Example Usage

### Creating an Operator
```csharp
var exfilService = serviceProvider.GetRequiredService<OperatorExfilService>();
var result = await exfilService.CreateOperatorAsync("Ghost");
var operatorId = result.Data; // Guid
```

### Starting Combat
```csharp
// Get read-only snapshot for combat
var snapshotResult = await exfilService.GetCombatSnapshotAsync(operatorId);
var snapshot = snapshotResult.Data;

// Create combat session with snapshot
var combatSession = new CombatSession(
    sessionId: Guid.NewGuid(),
    playerOperatorSnapshot: snapshot,
    // ... other params
);
```

### Successful Exfil
```csharp
// Combat session completes with player victory
var combatSessionId = combatSession.Id;
var experienceGained = 100;

var result = await exfilService.CommitSuccessfulExfilAsync(
    operatorId,
    combatSessionId,
    experienceGained);

// Operator streak incremented, XP added
var updatedOperator = result.Data;
Console.WriteLine($"Exfil Streak: {updatedOperator.ExfilStreak}");
```

### Failed Exfil
```csharp
// Combat session ends with player loss
var result = await exfilService.CommitFailedExfilAsync(
    operatorId,
    combatSessionId,
    "Abandoned extraction zone");

// Streak reset to 0
var updatedOperator = result.Data;
Console.WriteLine($"Exfil Streak: {updatedOperator.ExfilStreak}"); // 0
```

## Testing

### Hash Chain Verification Tests
- Verify valid chain loads successfully
- Verify corrupted hash triggers rollback
- Verify broken chain linkage triggers rollback
- Verify rollback deletes invalid events

### Exfil Service Tests
- Verify successful exfil increments streak
- Verify failed exfil resets streak
- Verify operator death resets streak
- Verify combat snapshot is read-only

### Integration Tests
- Full combat → exfil flow
- Multiple exfils with streak tracking
- Rollback recovery after corruption

## Constraints

### What We Do NOT Have Yet
- ❌ Authentication (anyone can commit events)
- ❌ Cryptographic signing (no private keys)
- ❌ Operator snapshots (events only)
- ❌ Multi-operator combat integration

### What We DO Have
- ✅ Hash chain integrity
- ✅ Automatic rollback on corruption
- ✅ Strict infil/exfil boundary
- ✅ Append-only event log
- ✅ Deterministic state reconstruction

## Conclusion

This architecture ensures:
1. **Integrity**: Operator state cannot be modified except through exfil
2. **Auditability**: All changes are recorded as events
3. **Recovery**: Corrupted chains are automatically rolled back
4. **Determinism**: State is always derived from events, never from snapshots

The infil/exfil boundary is the foundation for a secure, auditable operator progression system.
