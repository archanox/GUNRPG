# QUESTIONS.md — GUNRPG Discovery Audit

## Project Understanding Summary

GUNRPG is a deterministic tactical combat RPG built in C# (.NET 10). It features a real-time, millisecond-precision event-driven combat engine (`CombatSystemV2`), an event-sourced operator progression system with hash-chained tamper detection, a virtual pet readiness system, and three delivery surfaces: a REST API (`GUNRPG.Api`), a Pokemon-style console TUI (`GUNRPG.ConsoleClient`), and a Blazor WASM web client (`GUNRPG.WebClient`). Authentication uses self-hosted WebAuthn + Ed25519 JWT. The foundation is solid and the combat engine is rich — but several significant subsystems are **stubs, placeholders, or disconnected** from the fully-implemented core. The highest-risk areas are:

1. **Perks are cosmetic** — stored and hash-verified but never applied to any combat stat.
2. **`CombatSession.CreateDefault` ignores the operator's loadout** — player always starts with the Sokol 545.
3. **`DeterministicCombatEngine` (offline)** is a simplified stand-in with no weapon, cover, suppression, or perk logic.
4. **`playerLevel` is hardcoded to `0`** in difficulty calculations.
5. **AI never uses cover** — `Cover` intent is never set in `SimpleAIV2`.
6. **`SessionsController` has no authentication** — sessions can be read and manipulated without a token.
7. **`WeaponState.Jammed` is declared but never triggered by any combat logic.**

---

## How to Answer

For each question, answer inline and mark its status with one of:
- `verified` — confirmed and correct, no action needed
- `partial` — partially implemented, more work needed
- `bug` — unintended defect that should be fixed
- `approved improvement` — agrees with proposed fix, ready for implementation
- `deferred` — known, will not be addressed now
- `out-of-scope` — not in the intended design

---

## Questions

---

### 1. Product & Intended Behavior

#### Q1. Are perks intentionally cosmetic?
- **Where:** `GUNRPG.Core/Operators/OperatorAggregate.cs:219–222`, `GUNRPG.Application/Operators/OperatorExfilService.cs` (UnlockPerkAsync), `GUNRPG.Core/AI/SimpleAIV2.cs`, `GUNRPG.Core/Combat/CombatSystemV2.cs`
- **Why this matters:** Players can unlock "Iron Lungs," "Quick Draw," "Toughness," "Fast Reload," and "Steady Aim" and these appear in the UI, but no code anywhere reads a perk name and modifies any combat stat. The perks are stored, hash-chained, and synced — but they are decorative. If this is intended (perks will be wired up later), the current UX where players unlock perks with XP is misleading.
- **Question:** Are unlocked perks intentionally non-functional at this time? If they are meant to have gameplay effects, what stat should each perk modify (e.g., "Fast Reload" reduces `ReloadTimeMs`, "Steady Aim" reduces `ADSSpreadDegrees`)? Should they be applied at session creation time (as a session-level stat override) or wired into `CombatSystemV2` on every event?

---

#### Q2. Does the operator's loadout (equipped weapon) actually affect their combat session?
- **Where:** `GUNRPG.Application/Sessions/CombatSession.cs:114,121` (`CreateDefault`), `GUNRPG.Application/Sessions/CombatSessionService.cs:60–68` (`CreateSessionAsync`)
- **Why this matters:** `CombatSession.CreateDefault` always creates the player with `WeaponFactory.CreateSokol545()` regardless of the operator's `EquippedWeaponName`. The weapon name is only honored on snapshot *replay* (via `SessionMapping.CreateWeapon`). So changing loadout from "SOKOL 545" to "M15 MOD 0" currently has no effect on the combat experience for a newly created session.
- **Question:** Is this a known gap? Should `CreateSessionAsync` pass the operator's `EquippedWeaponName` and `LockedLoadout` to `CombatSession.CreateDefault` so that the player starts with their chosen weapon?

---

#### Q3. What does "Level" mean and where is it authoritative?
- **Where:** `GUNRPG.ClientModels/OperatorModels.cs:40` (`Level => TotalXp > 0 ? (int)(TotalXp / 100) + 1 : 1`), `GUNRPG.Application/Dtos/OperatorStateDto.cs` (no `Level` field)
- **Why this matters:** `Level` is computed client-side as a derived property from `TotalXp`. There is no `Level` field in the server's `OperatorStateDto`. The `playerLevel` parameter to `OpponentDifficulty.Compute()` is hardcoded to `0` (see Q4). If levels are meant to affect AI difficulty, combat unlocks, or perk eligibility, the server needs to compute and expose them.
- **Question:** Is the client-side `Level` formula the authoritative definition? Should `OperatorStateDto` expose a `Level` field? Is there a roadmap for level-gating perks, weapon unlocks, or AI difficulty scaling?

---

#### Q4. Is the `playerLevel: 0` placeholder an accepted regression or a bug?
- **Where:** `GUNRPG.Application/Sessions/CombatSessionService.cs:440–448`
- **Why this matters:** The TODO comment explicitly acknowledges this. `OpponentDifficulty.Compute(opponentLevel: session.EnemyLevel, playerLevel: 0)` always treats the player as level 0, which causes the difficulty to be incorrectly skewed (level-20 players will have the same difficulty feedback as a new player). This flows into the virtual pet's mission difficulty calculation and the XP reward modifier.
- **Question:** Should the fix load the `OperatorAggregate` in `BuildSideEffectPlan` (option 1), pass the operator level from the API call through the request (option 2), or refactor `OpponentDifficulty.Compute` to accept raw XP instead of level (option 3)? Which option is preferred?

---

#### Q5. Is the `DeterministicCombatEngine` (offline) intentionally a simplified stand-in?
- **Where:** `GUNRPG.Application/Combat/DeterministicCombatEngine.cs`
- **Why this matters:** This engine uses flat 65% / 55% hit chances, ignores weapon stats (damage falloff, RPM, recoil), ignores cover and suppression, ignores perks, and is completely separate from `CombatSystemV2`. It is used by the `OfflineGameplayService` in the web client and by the offline sync path. The "offline mission result" submitted to the server via `POST /operators/offline/sync` is produced by this simplified engine.
- **Question:** Is this intentional — a simpler engine for offline play — or should the offline path eventually use the same `CombatSystemV2` (running locally in the browser via replay)? If the simplified engine is permanent, what is the rationale for having two different combat engines that produce different outcomes for the same operator/seed combination?

---

#### Q6. What are the intended win/loss conditions for a "session" vs. an "offline mission"?
- **Where:** `GUNRPG.Application/Backend/MissionResultDto.cs` (the explicit TODO: "Will be used when offline session-based combat is implemented"), `GUNRPG.Infrastructure/Persistence/OfflineStore.cs:160` (TODO comment on `GetAllUnsyncedResults`)
- **Why this matters:** `MissionResultDto` is defined and commented with a TODO. The `OfflineStore.GetAllUnsyncedResults()` also has a TODO noting a future `ExfilSyncService` will call it. These mark an incomplete integration between session-based combat (the full `CombatSystemV2` path) and the offline sync path.
- **Question:** Is the current offline mode the final design (simplified engine + hash chain sync), or is there an intended future where the full session log is also synced? Is `MissionResultDto` meant to be wired into the sync flow, and if so, what triggers its population?

---

### 2. Architecture

#### Q7. Why does `SessionsController` have no `[Authorize]` attribute?
- **Where:** `GUNRPG.Api/Controllers/SessionsController.cs` (no `[Authorize]`), vs. `GUNRPG.Api/Controllers/OperatorsController.cs` (`[Authorize]` at class level)
- **Why this matters:** Any unauthenticated caller can create a session, read its state, submit intents, advance it, and stream SSE events without a token. Sessions contain player state and combat outcomes. This may be intentional (sessions are transient, ephemeral, and not tied to account data) but it creates a risk that an attacker could enumerate session IDs or spam session creation.
- **Question:** Is the unauthenticated sessions API intentional? If so, is there rate-limiting or IP restriction planned to prevent abuse? Should sessions require authentication, at minimum for creation?

---

#### Q8. Is the `DefaultGameEngine` meant to shadow or replace `CombatSystemV2` for P2P state verification?
- **Where:** `GUNRPG.Application/Distributed/DefaultGameEngine.cs`
- **Why this matters:** `DefaultGameEngine` maintains a lightweight action ledger (XP from Fire/Reload only, no health tracking, no cover, no suppression). It is described as "mirrors the intent-level state changes from the existing combat system." In practice the two engines can diverge — `CombatSystemV2` may kill a player in round 3 while `DefaultGameEngine` would still show them alive and accumulating XP. The P2P hash produced by `DefaultGameEngine` is not a hash of actual combat state.
- **Question:** Is the `DefaultGameEngine` intentionally a thin action-log checksum (not a full state snapshot), and is this difference from `CombatSystemV2` state considered acceptable? Or should the P2P hash incorporate the full session state from `CombatSessionService`?

---

#### Q9. What is the relationship between the gossip/ledger system and the session-based combat system?
- **Where:** `GUNRPG.Infrastructure/Gossip/`, `GUNRPG.Infrastructure/Ledger/`, `GUNRPG.Infrastructure/Gameplay/RunLedgerGameplayBridge.cs`, `GUNRPG.Application/Gameplay/IGameplayLedgerBridge.cs`
- **Why this matters:** There are two parallel operator state systems: the event-sourced `IOperatorEventStore` (used for auth'd operator management) and the gossip ledger (`RunLedger` + `LedgerGameStateProjector`). The `RunLedgerGameplayBridge` mirrors runs into the ledger. It is not clear from reading the code which system is the source of truth in production, when the ledger is used instead of the event store, and whether they are kept in sync.
- **Question:** Is the ledger system an optional/experimental feature (enabled only when `PreferLedgerReads` is set to `true`)? What is the long-term intent — will the ledger replace the event store, or are they complementary systems for different concerns?

---

#### Q10. Is there a planned migration path away from `InMemoryCombatSessionStore` and `InMemoryOperatorEventStore`?
- **Where:** `GUNRPG.Application/Sessions/InMemoryCombatSessionStore.cs`, `GUNRPG.Application/Operators/InMemoryOperatorEventStore.cs`, `GUNRPG.Infrastructure/Persistence/LiteDbCombatSessionStore.cs`, `GUNRPG.Infrastructure/Persistence/LiteDbOperatorEventStore.cs`
- **Why this matters:** Both in-memory and LiteDB implementations exist. The DI registration wires up LiteDB for production but in-memory stores are used in some service construction paths without explicit store injection (e.g., `OfflineGameplayService` constructs `new CombatSessionService(_combatStore)` directly). Any restart would lose in-memory sessions.
- **Question:** Is the in-memory store used only for tests? Is there a scenario where the server is started with in-memory stores in production (e.g., during offline/dev mode)? Should `InMemory*` stores be test-only by convention?

---

### 3. Code Structure & Boundaries

#### Q11. Is it intentional that `OfflineGameplayService` uses `new CombatSessionService(...)` instead of a DI-injected instance?
- **Where:** `GUNRPG.WebClient/Services/OfflineGameplayService.cs:28–40`
- **Why this matters:** `OfflineGameplayService` constructs `new CombatSessionService(_combatStore)` directly, bypassing DI. This means none of the optional collaborators (`IOperatorEventStore`, `IGameAuthority`, `CombatSessionUpdateHub`) are available for the offline session. If those collaborators are ever required (e.g., if `updateHub` fires notifications), the offline path would silently skip them.
- **Question:** Is bypassing DI intentional here (to keep the offline path lightweight)? Should `CombatSessionService` be injected via the DI container instead, or is the null-defaults pattern sufficient?

---

#### Q12. Are there two separate `EventQueue` types and is that intentional?
- **Where:** `GUNRPG.Core/Events/EventQueue.cs` vs. `GUNRPG.Core/Simulation/EventQueue.cs`
- **Why this matters:** There are two `EventQueue` classes in different namespaces in `GUNRPG.Core`. The `Events/EventQueue.cs` is a `SortedSet`-backed priority queue for `ISimulationEvent`, while `Simulation/EventQueue.cs` appears in the simulation path. It is unclear which path uses which, and whether they can be unified.
- **Question:** Are the two `EventQueue` types serving distinct purposes (one for the legacy event system, one for the new simulation)? Is the `Events/` path still active, or is it legacy code that can be deleted?

---

### 4. API Design

#### Q13. Should the `POST /operators/{id}/infil/start` endpoint accept the desired weapon/loadout as a parameter?
- **Where:** `GUNRPG.Api/Controllers/OperatorsController.cs` (`StartInfil`), `GUNRPG.Application/Operators/OperatorExfilService.cs` (`StartInfilAsync`)
- **Why this matters:** The infil start endpoint locks the operator's loadout via `LockedLoadoutEvent`. However, `CombatSession.CreateDefault` still ignores the locked loadout when creating a new session (see Q2). If the loadout is locked at infil but not used in the session, the feature is incomplete.
- **Question:** Should `StartInfilAsync` return the locked loadout weapon name, and should `CreateSessionAsync` be updated to accept and apply it? Or is the loadout lock purely an exfil-side guard (preventing loadout changes while deployed)?

---

#### Q14. Is there a deliberate design choice to not require the `OperatorId` when creating a session?
- **Where:** `GUNRPG.Api/Dtos/ApiSessionCreateRequest.cs`, `GUNRPG.Application/Requests/SessionCreateRequest.cs` (`OperatorId` is `Guid?`)
- **Why this matters:** The `OperatorId` is optional in `SessionCreateRequest`. A session without an operator ID cannot be linked to operator progression. If created without an ID, the session is effectively a sandbox match with no exfil outcome possible.
- **Question:** Is creating a session without an operator ID an intended use case (e.g., a demo/practice mode)? Or should `OperatorId` be required, at minimum when called from the authenticated operator flow?

---

#### Q15. Should `DELETE /sessions/{id}` return a success code rather than always returning an error?
- **Where:** `GUNRPG.Api/Controllers/SessionsController.cs:172–180` (`Delete`)
- **Why this matters:** The `Delete` action always returns a `Conflict` error ("Combat sessions cannot be deleted"). A client that calls `DELETE` will always get a 409 and no way to distinguish "session not found" from "intentional no-delete policy." This unconventional response may confuse REST clients.
- **Question:** Is the no-delete policy final? Should the endpoint be removed from the router (rather than returning 409), or should it return 405 Method Not Allowed, which is the correct HTTP status for "this method is not supported on this resource"?

---

### 5. Data & Persistence

#### Q16. What happens when `CreateWeapon` receives an unknown weapon name?
- **Where:** `GUNRPG.Application/Mapping/SessionMapping.cs:440–448` (`CreateWeapon`)
- **Why this matters:** The fallback `_ => new Weapon(name)` constructs a `Weapon` with the given name but with all-default stats (0 damage, 0 RPM, etc.). This will silently cause broken combat — the player fires but deals no damage. The fallback should probably throw an exception or return `null`.
- **Question:** Is `new Weapon(name)` as a fallback intentional (for modding/custom weapon support), or is it a silent failure path? Should unknown names throw, return null (which would fall back to no weapon), or be validated at loadout-change time to prevent invalid names from ever being stored?

---

#### Q17. What is the rollback policy when hash chain corruption is detected in `LiteDbOperatorEventStore`?
- **Where:** `GUNRPG.Infrastructure/Persistence/LiteDbOperatorEventStore.cs:198, 207, 216` (3 TODO: Add logging/metrics)
- **Why this matters:** When hash chain corruption is detected during event load, the store silently deletes all events from the corrupt sequence onward and returns a truncated event list. The caller may not know a rollback occurred. There are three TODO comments requesting logging/metrics to be added for diagnostics.
- **Question:** Is the silent truncation + rollback the intended recovery strategy? Should the API caller receive an explicit error (e.g., `ServiceResult.InvalidState("corruption detected: N events rolled back")`) rather than a silently shorter event list? What alert/monitoring should trigger on a rollback?

---

#### Q18. Is there a risk that the offline `OfflineStore.GetAllUnsyncedResults()` is never called?
- **Where:** `GUNRPG.Infrastructure/Persistence/OfflineStore.cs:160` (TODO comment)
- **Why this matters:** The method has an explicit TODO: "Future ExfilSyncService will call this to retrieve all pending results for server reconciliation." There is no background service, timer, or event that calls this on the server side. The console client has `IExfilSyncService` which handles the client-side sync, but if the client disconnects without syncing, pending results in the server-side `OfflineStore` would accumulate indefinitely.
- **Question:** Is `OfflineStore` on the server side used at all in the current flow (post-sync, the server has already processed the envelope), or is this planned for a future background reconciliation pass?

---

### 6. Security

#### Q19. Are session IDs exposed to any user who didn't create the session?
- **Where:** `GUNRPG.Api/Controllers/SessionsController.cs` (no authorization), `GUNRPG.Application/Sessions/InMemoryCombatSessionStore.cs` / `LiteDbCombatSessionStore.cs`
- **Why this matters:** Session IDs are GUIDs, so enumeration is difficult, but any authenticated user who knows another user's session GUID can read their combat state, submit intents, and advance combat. There is no owner check on any `SessionsController` endpoint.
- **Question:** Should sessions be scoped to the authenticated operator? Would it be sufficient to add `[Authorize]` to `SessionsController` and then validate that the requesting token's `account_id` matches the session's `OperatorId`?

---

#### Q20. Is the offline sync endpoint (`POST /operators/offline/sync`) fully tamper-proof?
- **Where:** `GUNRPG.Api/Controllers/OperatorsController.cs:251–310` (`SyncOfflineMission`), `GUNRPG.Application/Services/OperatorService.cs:545` (`SyncOfflineMission`)
- **Why this matters:** The endpoint verifies hash chain continuity and replays the `DeterministicCombatEngine` to confirm the outcome. However, `DeterministicCombatEngine` is a simplified engine (fixed hit chance, no weapons, no cover). A malicious client could fabricate a winning result that the simplified engine considers valid (by constructing a valid hash chain with a manipulated seed that produces a victory) even though the same outcome could not have occurred in the real `CombatSystemV2`.
- **Question:** Is there a plan to validate offline submissions against the full `CombatSystemV2` (which would require running it server-side for every offline mission), or is the hash-chain integrity check sufficient as an anti-cheat mechanism for this use case?

---

### 7. Performance

#### Q21. Is `ReplayGameAuthority`'s O(n²) replay cost acceptable?
- **Where:** `GUNRPG.Application/Distributed/ReplayGameAuthority.cs`
- **Why this matters:** `ReplayGameAuthority.SubmitActionAsync` replays the entire action log from scratch after every submitted action for determinism validation. After 100 actions, this performs 5050 engine steps. This is documented in the XML comment but no upper bound on session length is enforced.
- **Question:** Is `ReplayGameAuthority` used in production or only in tests/validation? If it is used in production, should a maximum action count be enforced before a new session is required?

---

#### Q22. Is `LedgerGameStateProjector.Project` called often enough to warrant caching?
- **Where:** `GUNRPG.Infrastructure/Ledger/LedgerGameStateProjector.cs`
- **Why this matters:** `Project` iterates all ledger entries, groups all operator events, and re-projects full operator aggregates on every call. With a growing ledger, this becomes an O(n) full-scan with no indexing. There is no caching layer.
- **Question:** Is ledger projection a hot path, or is it only called during admin/debug queries? If it is on the hot path, should a snapshot cache be introduced?

---

### 8. Error Handling & Resilience

#### Q23. What happens if `CombatSessionService.AdvanceAsync` is called concurrently by two clients on the same session?
- **Where:** `GUNRPG.Application/Sessions/CombatSessionService.cs` (`AdvanceAsync`), `GUNRPG.Application/Sessions/InMemoryCombatSessionStore.cs`
- **Why this matters:** Neither the in-memory store nor the LiteDB store uses optimistic locking or transaction-level serialization for the read-modify-write cycle in `AdvanceAsync`. If two web clients race to advance the same session simultaneously, both reads may return the same snapshot, both advance it independently, and the second write will overwrite the first without conflict detection.
- **Question:** Is concurrent access to the same session a realistic scenario in the current design? If yes, should a per-session lock or optimistic concurrency check (e.g., compare session version) be added?

---

#### Q24. Are desync errors in `DistributedAuthority` surfaced to the player?
- **Where:** `GUNRPG.Application/Distributed/DistributedAuthority.cs:246–252` (`HandleHashReceived`, `_isDesynced = true`)
- **Why this matters:** When a hash mismatch is detected, `_isDesynced` is set to `true` and further `SubmitActionAsync` calls throw `InvalidOperationException("Node is in desync state")`. There is no recovery path (reconnect, resync from authority), and the player would be stuck until they restart the client.
- **Question:** Is the desync state a permanent failure condition by design, or should there be an automatic recovery flow (e.g., requesting a full-log sync from peers to rebuild state)?

---

### 9. Testing & QA

#### Q25. Are the `StubOperatorEventStore.AppendEventAsync` and `AppendEventsAsync` exceptions ever hit in tests?
- **Where:** `GUNRPG.Tests/Stubs/StubOperatorEventStore.cs:26,31`
- **Why this matters:** Both `AppendEventAsync` and `AppendEventsAsync` throw `NotImplementedException("not needed for current tests")`. If any test (now or in the future) invokes a code path that tries to append events against this stub, it will throw a confusing error rather than a clear test failure.
- **Question:** Should the stubs be updated to silently swallow appends (no-op) or record them for assertion, instead of throwing `NotImplementedException`? Are there tests that test the append path that use a different setup?

---

#### Q26. Is there test coverage for the offline sync hash-chain validation path?
- **Where:** `GUNRPG.Tests/OfflineModeTests.cs`, `GUNRPG.Tests/ExfilSyncServiceTests.cs`
- **Why this matters:** The offline sync relies on hash-chain integrity checks. The `ExfilSyncServiceTests` exist, but it is not immediately clear whether they cover the "hash chain mismatch → corruption detected" path, the "server operator state mismatch → integrity failure" path, or the "sequence gap" path.
- **Question:** Do existing tests cover all three integrity failure paths (sequence gap, hash chain break, initial state mismatch)? If not, which paths are untested?

---

### 10. Observability

#### Q27. Where are server logs emitted, and is there a structured logging strategy?
- **Where:** `GUNRPG.Infrastructure/Backend/ExfilSyncService.cs` (uses `Console.WriteLine`), `GUNRPG.Application/Operators/OperatorExfilService.cs:616` (TODO: logging), `GUNRPG.Infrastructure/Persistence/LiteDbOperatorEventStore.cs:198,207,216` (TODO: logging)
- **Why this matters:** The console client uses `Console.WriteLine` for diagnostic output. The API services use `ILogger` in some places (e.g., `LedgerGossipService`) but `Console.WriteLine` in others (e.g., `ExfilSyncService`, `OnlineGameBackend`). There are at least 4 places with explicit TODOs to add logging.
- **Question:** Is `Console.WriteLine` in infrastructure services acceptable, or should all logging be unified through `ILogger<T>`? Should the 4 outstanding TODO logging items be treated as a tech-debt story?

---

### 11. Documentation & Missing Decisions

#### Q28. What is the intended XP cost (or cost mechanism) for unlocking perks?
- **Where:** `GUNRPG.Application/Operators/OperatorExfilService.cs` (`UnlockPerkAsync`), `GUNRPG.WebClient/Pages/OperatorPerks.razor`, `GUNRPG.ConsoleClient/Program.cs:2492–2496`
- **Why this matters:** `UnlockPerkAsync` does not deduct XP when a perk is unlocked. Perks can be unlocked at any time with any XP balance (including 0). There is no cost gate, no level requirement, and no perk prerequisite system.
- **Question:** Is the perk system intentionally free (XP is purely a progression score, not a currency), or is there a planned cost-per-perk mechanic that is not yet implemented?

---

#### Q29. Is weapon jamming (`WeaponState.Jammed`) planned for implementation?
- **Where:** `GUNRPG.Core/Operators/OperatorState.cs:34`, `GUNRPG.Core/Intents/SimultaneousIntents.cs:111`, `GUNRPG.Core/Intents/Intent.cs:73`
- **Why this matters:** `WeaponState.Jammed` is declared in the enum and guarded against in `Validate()` ("Cannot fire: weapon is jammed"), but no code in `CombatSystemV2`, `CombatSession`, or the AI ever sets an operator's weapon state to `Jammed`. The jam mechanic is validated but unreachable.
- **Question:** Is weapon jamming a planned feature with validation pre-implemented, or is it dead code that should be removed to reduce confusion?

---

#### Q30. Does the AI's cover decision being absent represent a planned future feature or a gap?
- **Where:** `GUNRPG.Core/AI/SimpleAIV2.cs:35–43` (`DecideIntents`)
- **Why this matters:** `SimultaneousIntents` has a `Cover` field (`CoverAction.EnterPartial`, `EnterFull`, `Exit`). The cover model, transition model, and validation are all implemented in `CombatSystemV2`. But `SimpleAIV2.DecideIntents()` never sets `intents.Cover` — it always stays `CoverAction.None`. The AI is incapable of using cover despite the full cover system being implemented.
- **Question:** Should `SimpleAIV2` be extended to make tactical cover decisions? What behavior is desired — should the AI enter full cover when health is low, use partial cover at mid-range, etc.?

---

#### Q31. What is the exfil behavior when an operator's 30-minute infil timer expires while they are mid-combat?
- **Where:** `GUNRPG.Application/Operators/OperatorExfilService.cs:400,641,736`, `GUNRPG.WebClient/Pages/MissionInfil.razor`, `GUNRPG.WebClient/Pages/ExfilFailed.razor`
- **Why this matters:** The client UI shows a countdown timer and redirects to the "EXFIL FAILED" screen when the timer expires. But on the server side, the timer is only enforced at the `CompleteExfilAsync` and `ProcessCombatOutcomeAsync` call points — there is no background job that automatically marks operators as KIA when the 30 minutes elapses. If the player simply abandons the session without navigating back, the operator remains stuck in `Infil` mode indefinitely.
- **Question:** Is there a server-side scheduled job or lazy-eval check that marks operators KIA when the infil timer expires, or is the timer enforcement entirely client-side? If an operator is in `Infil` mode past 30 minutes and the `GetOperatorAsync` endpoint is called, does the server detect the expiry and apply consequences?

---

#### Q32. Are multiple simultaneous opponents part of the near-term roadmap?
- **Where:** `README.md` ("Planned: Multiple simultaneous opponents"), `GUNRPG.Core/Simulation/Simulation.cs`, `GUNRPG.Core/Combat/CombatSystemV2.cs`
- **Why this matters:** `CombatSystemV2` is built around a 1v1 model (a `player` and an `enemy`). Adding a second opponent would require significant architectural changes: multi-target intent routing, multi-entity event priority ordering, and UI changes in both clients.
- **Question:** Is multi-opponent combat a Q3/Q4 target or a longer-term goal? Are there any architectural constraints in the current event queue or intent system that would need to be resolved before multi-opponent can be attempted?

---

#### Q33. Is the `InfilTimerMinutes` constant duplicated between server and client?
- **Where:** `GUNRPG.Application/Operators/OperatorExfilService.cs:30` (`public const int InfilTimerMinutes = 30`), `GUNRPG.ClientModels/InfilConstants.cs:12` (comment: "Must match OperatorExfilService.InfilTimerMinutes")
- **Why this matters:** The comment acknowledges the duplication risk. If the value is changed on the server, the client must be manually kept in sync. This is a latent divergence risk, especially because `GUNRPG.ClientModels` is shared — the constant could be moved there instead.
- **Question:** Should `InfilConstants.InfilTimerMinutes` in `GUNRPG.ClientModels` replace the constant in `OperatorExfilService`, with the server referencing the shared value? Or is keeping them separate intentional to allow server-side changes without client redeployment?

---

#### Q34. Is there a plan for campaign mode or persistent mission progression?
- **Where:** `README.md` ("Planned: Campaign mode with persistent mission progression")
- **Why this matters:** The current design has no concept of missions as a structured sequence. Each combat session is independent. There are no mission objectives, maps, campaign nodes, or progression gates. The `NodeSelection.razor` page in the web client suggests UI scaffolding for node-based navigation, but the backing data model and API for "campaign nodes" does not exist.
- **Question:** What is the intended data model for campaign mode? Are "nodes" the same as "sessions" plus metadata (objectives, loot), or would they require a new aggregate and new API endpoints?

---

## Suggested Answer Tags

Use these when annotating answers:
- `verified` — confirmed, no action needed
- `partial` — partially correct, more work needed
- `bug` — unintended defect requiring a fix
- `approved improvement` — approved change, ready for implementation
- `deferred` — known gap, will not be addressed now
- `out-of-scope` — intentionally not in the design
- `caveat` — correct with conditions/nuance
