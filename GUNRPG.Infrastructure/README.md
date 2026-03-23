# GUNRPG.Infrastructure

Infrastructure layer for GUNRPG, providing concrete implementations of persistence, identity, security, and distributed transport abstractions.

## Overview

This project contains implementation details for storage and external integrations that are abstracted in the Application layer. It follows Clean Architecture principles by keeping infrastructure concerns separate from domain and application logic.

## Components

### Persistence

#### LiteDbCombatSessionStore

Embedded document database implementation of `ICombatSessionStore` using LiteDB.

**Features:**
- Persist combat session snapshots to disk
- Support for save, load, update, delete, and list operations
- Thread-safe for concurrent requests
- Automatic enum serialization to strings for readability
- No annotations required on domain objects
- Schema migration support via LiteDB.Migration
- Hash validation of completed sessions on load

#### LiteDbOperatorEventStore

Embedded document database implementation of `IOperatorEventStore` for operator event sourcing.

**Features:**
- Append-only event log per operator
- SHA-256 hash chain verification on every load
- Automatic rollback to last valid event on corruption
- Indexes on OperatorId and SequenceNumber for performance

#### LiteDbMigrations

Manages database schema migrations using the [LiteDB.Migration](https://github.com/JKamsker/LiteDB.Migration) library.

**Features:**
- Automatic migration application on startup
- Schema version tracking
- Forward-only migrations
- Supports complex data transformations

**Current Schema Version:** 1 (initial baseline)

**Adding Migrations:**

When the snapshot schema evolves, add migrations in `LiteDbMigrations.ApplyMigrations`:

```csharp
var migrations = new MigrationContainer(config =>
{
    config.Collection<CombatSessionSnapshotV2>("combat_sessions", collectionConfig =>
    {
        collectionConfig
            .StartWithModel<CombatSessionSnapshotV1>()
            .WithMigration(v1 => new CombatSessionSnapshotV2
            {
                Id = v1.Id,
                // ... map existing properties
                NewProperty = "default-value" // Add new property
            })
            .UseLatestVersion();
    });
});
migrations.Apply(database);
```

Update `CurrentSchemaVersion` constant after adding migrations.

**Configuration:**

```json
{
  "Storage": {
    "Provider": "LiteDB"
  }
}
```

If `LiteDbConnectionString` is omitted, the default `~/.gunrpg/combat_sessions.db` under the current user's home directory is used. You can set it explicitly to override the location.

**Usage:**

The store is automatically registered via the `AddCombatSessionStore` extension method:

```csharp
builder.Services.AddCombatSessionStore(builder.Configuration);
```

To switch to in-memory storage for testing:

```json
{
  "Storage": {
    "Provider": "InMemory"
  }
}
```

### Identity

Self-hosted WebAuthn + JWT authentication using LiteDB for all state. No external identity provider required.

| Class | Description |
|---|---|
| `ApplicationUser` | User account model |
| `LiteDbUserStore` | Persists user accounts to LiteDB |
| `LiteDbWebAuthnStore` | Persists WebAuthn credentials to LiteDB |
| `JwtTokenService` | Issues and validates Ed25519 JWT access tokens and refresh tokens |
| `WebAuthnService` | Handles FIDO2 registration and authentication ceremonies |
| `DeviceCodeService` | Implements RFC 8628 device authorization grant for console clients |

See [docs/IDENTITY.md](../docs/IDENTITY.md) for configuration and API reference.

### Security

Authority and session signing infrastructure for tamper-evident game run validation.

| Class | Description |
|---|---|
| `SessionAuthority` | Signs completed sessions with Ed25519 via `AuthorityCrypto` |
| `SignedRunResult` | Holds SessionId/PlayerId/FinalHash/AuthorityId/Signature |
| `RunReplayEngine` | Verifies signed runs via replay → hash → signature check |
| `QuorumPolicy` / `QuorumValidator` | Multi-authority quorum validation |
| `AuthorityRoot` / `AuthorityState` | Authority key management and state |
| `ServerIdentity` / `ServerCertificate` | Node identity and certificate issuance |

### Distributed / Backend

Infrastructure for offline and online play modes and P2P transport.

| Class | Description |
|---|---|
| `OfflineGameBackend` | Runs sessions locally without an API server |
| `OnlineGameBackend` | Delegates to a remote GUNRPG.Api instance |
| `GameBackendResolver` | Selects the appropriate backend based on configuration |
| `ExfilSyncService` | Syncs offline mission results to an online node |
| `LibP2pPeerService` | libp2p-based peer discovery and transport |
| `Libp2pLockstepTransport` | Lockstep combat transport over libp2p |
| `InMemoryLockstepTransport` | In-process lockstep transport for testing |

### Ledger

Append-only game event ledger for audit and gossip.

| Class | Description |
|---|---|
| `RunLedger` | Ordered ledger of signed run results |
| `RunLedgerEntry` / `RunLedgerMutation` | Ledger entries and mutations |
| `LedgerGameStateProjector` | Projects ledger into current game state |
| `MerkleSkipIndex` | Fast skip-list index for Merkle proofs |
| `LedgerGossipService` | Gossips new ledger entries to peers |
| `LedgerSyncEngine` | Synchronizes ledger with peers |

## Design Principles

1. **Separation of Concerns**: LiteDB types never leak into Core or Application layers
2. **Configuration-Driven**: Store selection via appsettings.json
3. **Dependency Injection**: Singleton lifetime for database connections
4. **Clean Architecture**: Infrastructure depends on Application, never the reverse
5. **Thread Safety**: All operations are safe for concurrent use

## Dependencies

- `LiteDB` (5.0.21): Embedded NoSQL document database
- `LiteDB.Migration` (0.0.10): Schema migration framework for LiteDB
- `Fido2.Net` / `Fido2.AspNet`: WebAuthn/FIDO2 library
- `BouncyCastle.Cryptography`: Ed25519 key generation and signing
- `Microsoft.Extensions.*`: Configuration, DI, options pattern support

## Testing

Tests are located in `GUNRPG.Tests/` and verify:
- `LiteDbCombatSessionStoreTests` — Basic CRUD, nested object serialization, enum handling, concurrent access
- `LiteDbOperatorEventStoreTests` — Event append, hash chain verification, rollback on corruption
- `RunReplayEngineTests` — Session signing and replay verification
- `QuorumValidatorTests` — Multi-authority quorum rules
