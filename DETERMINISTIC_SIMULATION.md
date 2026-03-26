# Deterministic Simulation

This document describes the invariants that the GUNRPG simulation must maintain to guarantee:

> **Same seed + same inputs → identical per-tick state hashes on every machine**

This property is required for the signed-tick authority system (`TickAuthorityService`) to function
correctly.  Any divergence produces a different state hash, which causes a false desync and
invalidates the Ed25519 checkpoint signature.

---

## 1. Collection ordering

Unordered collections (`Dictionary<>`, `HashSet<>`, `ConcurrentDictionary<>`) must never be
iterated in order-sensitive contexts (state hashing, serialization, tick processing).

### Current state

| Location | Collection | Usage | Risk |
|---|---|---|---|
| `Simulation.Step` | `List<SimulationEnemyState>` | Sorted by `enemy.Id` via `OrderBy` before iteration | ✅ Safe |
| `StateHasher.HashTick` | `SimulationEnemyState[]` | Sorted by `Id` before hashing | ✅ Safe |
| `InputLog.NormalizeEntries` | `IReadOnlyList<InputLogEntry>` | Sorted by `Tick` then original index | ✅ Safe |
| `TickInputs` | `IReadOnlyList<PlayerInput>` | Sorted by `PlayerId` (big-endian bytes) then original index | ✅ Safe |
| `CombatSystemV2` | `Dictionary<Guid, long>`, `HashSet<Guid>` | Lookup/deduplication only — never iterated for state | ✅ Safe |
| `AuthorityState`, `QuorumValidator`, `SignedRunValidation` | `HashSet<string>` | Membership testing only | ✅ Safe |

### Rule

Whenever a collection is used in a path that contributes to a state hash or is replayed across
nodes, iteration order must be explicitly enforced with a deterministic comparator (e.g.
`OrderBy(x => x.Key)`).

---

## 2. Floating-point determinism

Floating-point arithmetic (`float`, `double`, `Math.*`) may produce different results across
runtimes or CPU configurations when intermediate results are kept in extended-precision registers.

### Current state

The **new simulation layer** (`GUNRPG.Core/Simulation/`) uses **integer math exclusively**:

- `SimulationPlayerState` — `int Health`, `int MaxHealth`
- `SimulationEnemyState` — `int Health`, `int MaxHealth`
- `Simulation.Step` — hit-chance and damage calculated with `int` constants and `SeededRandom.Next`
- `StateHasher` — writes `int32` and `int64` fields; no floating-point serialization

The **legacy combat layer** (`GUNRPG.Core/Combat/`, `GUNRPG.Application/Sessions/`) uses `float`
for health, distances, ADS progress, and accuracy calculations (`AccuracyModel`, `CombatSystemV2`).
This layer is **not** connected to `TickAuthorityService`; it runs on a single machine (offline
session or server) and its float results are never cross-validated by hash comparison between nodes.

### Rule

Any code path that feeds `SimulationState` into `TickAuthorityService.ProcessTick` or
`StateHasher.HashTick` must use integer arithmetic only.  Float values must not appear in
`SimulationState`, `SimulationPlayerState`, or `SimulationEnemyState`.

---

## 3. RNG determinism

All randomness in the simulation must be deterministic and reproducible.

### Simulation RNG: `SeededRandom`

`GUNRPG.Core/Simulation/SeededRandom` is the **canonical deterministic RNG** for the simulation
layer.  It implements an xorshift64-variant PRNG with a fully documented algorithm:

```
state ^= state >> 12
state ^= state << 25
state ^= state >> 27
output = (uint)((state * 2685821657736338717UL) >> 32)
```

The state is seeded deterministically from the `int` seed via a mixing function, so:
- Same seed → same sequence of outputs, on every machine and .NET version.
- `RngState(Seed, State, CallCount)` is part of `SimulationState` and is hashed into every tick.

`SeededRandom` is used by `Simulation.Step` and `ReplayRunner.CreateInitialState`.  It must be the
only source of randomness in the simulation layer.

### Rules

| Rule | Status |
|---|---|
| RNG must be seeded at run start | ✅ Seed flows from `RunInput.Seed` → `InputLog.Seed` → `ReplayRunner.CreateInitialState` |
| RNG usage must occur in the same order on all nodes | ✅ Single-threaded `Simulation.Step` in tick order |
| No `Random.Shared` in simulation layer | ✅ Only `SeededRandom` is used |
| No time-based seeds in simulation layer | ✅ Seed is always caller-supplied |

### Legacy paths (outside the simulation layer)

`CombatSession.CreateDefault` and `CombatSystemV2` have a `seed ?? Random.Shared.Next()`
fallback that is reached when no explicit seed is provided.  This fallback is **intentional** for
standalone UI / exploration sessions (console client, single-player offline) that do not
participate in the signed-tick authority system.

**Every code path that feeds into `TickAuthorityService` or `DeterministicCombatEngine` must pass
an explicit seed.**  The `Random.Shared` fallback is documented in both call sites with a
`§determinism` comment.

---

## 4. Stable serialization for hashing

`StateHasher` serializes `SimulationState` into a SHA-256 hash using explicit, fixed-width binary
encoding with no JSON, reflection, or platform-dependent representations:

| Field | Encoding |
|---|---|
| `long` (tick, time) | Big-endian `int64` |
| `int` (health, count) | Big-endian `int32` |
| `ulong` (RNG state) | Big-endian `uint64` |
| `bool` | 1-byte (`0x00`/`0x01`) |
| `string` | UTF-8 length-prefixed |
| `Guid` | 16-byte big-endian |
| Enemy list | Sorted by `Id` ascending, then each entry encoded as above |

`TickInputs.ComputeHash` similarly encodes the input batch deterministically, sorting players by
`PlayerId` big-endian bytes before writing.

### Rule

Never introduce JSON serializers, reflection-based serializers, or platform-dependent encodings
into `StateHasher` or `TickInputs.ComputeHash`.

---

## 5. Replay determinism tests

The following tests prove the determinism guarantee:

| Test | File | What it proves |
|---|---|---|
| `SameInput_ProducesSameHash_AcrossMultipleRuns` | `DeterministicSimulationTests.cs` | Same seed + same inputs → identical final hash across 10 independent runs |
| `LiveRun_EqualsReplayValidation` | `DeterministicSimulationTests.cs` | Replay of a recorded run produces the same per-tick and final hashes |
| `SeededRandom_IsConsistent_AcrossInstances` | `DeterministicSimulationTests.cs` | Two `SeededRandom` instances with the same seed produce identical output streams |
| `TickAuthority_SameSeedAndInputs_ProduceIdenticalStateHashes_AcrossRuns` | `TickAuthorityTests.cs` | Per-tick state hashes produced by `TickAuthorityService.ProcessTick` are identical across two independent authority services given the same seed and actions |
| `ReplayRunner_ProducesSameStateHashes_AsOriginalRun` | `DeterministicSimulationTests.cs` | `ReplayRunner.ValidateReplay` reproduces every per-tick hash from the original run |
| `TickInputs_MultiPlayer_OrderIsCanonical_RegardlessOfSubmissionOrder` | `TickAuthorityTests.cs` | Multi-player input batch hash is identical regardless of submission order |

---

## 6. Fixed ordering for inputs

Player inputs within a tick are sorted by `PlayerId` big-endian byte order before hashing
(`TickInputs`), and across ticks by tick number then original index (`InputLog.NormalizeEntries`).
This guarantees a canonical order even when different nodes receive inputs in different network
arrival order.

---

## Summary checklist

Before adding code that affects the simulation/hashing path:

- [ ] State fields use `int` or `long`, not `float` or `double`
- [ ] Collections iterated for hashing or state production are sorted with a deterministic key
- [ ] Randomness comes from `SeededRandom` only (no `Random.Shared`, no `new Random()`, no time seeds)
- [ ] Serialization uses explicit binary encoding (no JSON/reflection)
- [ ] A determinism test is added or updated to cover the new code path
