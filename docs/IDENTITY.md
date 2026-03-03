# GunRPG Identity System

Self-hosted WebAuthn + JWT authentication for GunRPG nodes.  
No centralized IdP. No EF Core. All state persists to LiteDB.

---

## Table of Contents

1. [Architecture overview](#architecture-overview)
2. [Configuring HTTPS for a self-hosted node](#configuring-https-for-a-self-hosted-node)
3. [Configuring WebAuthn origins](#configuring-webauthn-origins)
4. [Browser (WebAuthn) authentication flow](#browser-webauthn-authentication-flow)
5. [Console device code flow](#console-device-code-flow)
6. [JWT tokens](#jwt-tokens)
7. [Refresh token rotation](#refresh-token-rotation)
8. [appsettings.json reference](#appsettingsjson-reference)
9. [API endpoint reference](#api-endpoint-reference)

---

## Architecture overview

```
┌─────────────────────────────────────────────────────┐
│ Browser / SPA (GitHub Pages)                        │
│   navigator.credentials.create() / .get()          │
│   ──── WebAuthn ────────────────────────────────►  │
│                          GUNRPG.Api                 │
│   ◄─── JWT access token + refresh token ─────────  │
└─────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────┐
│ Console client (no browser)                         │
│   POST /auth/device/start                           │
│   ──── shows user_code to operator ─────────────►  │
│                          GUNRPG.Api                 │
│   ◄─── polls /auth/device/poll ──────────────────  │
│   (tokens arrive after browser WebAuthn completes)  │
└─────────────────────────────────────────────────────┘
```

| Component | Layer | File |
|---|---|---|
| `ApplicationUser` | Infrastructure | `GUNRPG.Infrastructure/Identity/ApplicationUser.cs` |
| `LiteDbUserStore` | Infrastructure | `GUNRPG.Infrastructure/Identity/LiteDbUserStore.cs` |
| `JwtTokenService` | Infrastructure | `GUNRPG.Infrastructure/Identity/JwtTokenService.cs` |
| `WebAuthnService` | Infrastructure | `GUNRPG.Infrastructure/Identity/WebAuthnService.cs` |
| `DeviceCodeService` | Infrastructure | `GUNRPG.Infrastructure/Identity/DeviceCodeService.cs` |
| `WebAuthnController` | API | `GUNRPG.Api/Controllers/WebAuthnController.cs` |
| `DeviceCodeController` | API | `GUNRPG.Api/Controllers/DeviceCodeController.cs` |
| `TokenController` | API | `GUNRPG.Api/Controllers/TokenController.cs` |

---

## Configuring HTTPS for a self-hosted node

WebAuthn requires a **secure context** (HTTPS or localhost).  
The browser's `navigator.credentials` API will refuse to operate over plain HTTP on any non-localhost origin.

### Option A — Self-signed certificate (development / LAN)

```bash
# Generate a dev cert trusted by your machine
dotnet dev-certs https --trust

# Run the API on HTTPS
dotnet run --project GUNRPG.Api --launch-profile https
```

### Option B — Let's Encrypt (public server)

1. Point a DNS A-record at your server's public IP.
2. Install [Certbot](https://certbot.eff.org/) and obtain a certificate:

   ```bash
   certbot certonly --standalone -d gunrpg.example.com
   ```

3. Configure `appsettings.json` (or environment variables) with the certificate path:

   ```json
   "Kestrel": {
     "Endpoints": {
       "Https": {
         "Url": "https://0.0.0.0:443",
         "Certificate": {
           "Path": "/etc/letsencrypt/live/gunrpg.example.com/fullchain.pem",
           "KeyPath": "/etc/letsencrypt/live/gunrpg.example.com/privkey.pem"
         }
       }
     }
   }
   ```

4. Set the `WebAuthn:ServerDomain` to the hostname (no scheme, no port):

   ```json
   "WebAuthn": {
     "ServerDomain": "gunrpg.example.com"
   }
   ```

### Option C — Reverse proxy (nginx / Caddy)

Place the GUNRPG.Api behind nginx/Caddy which terminates TLS, then forward to `http://localhost:5000`.  
Configure `ForwardedHeaders` in `Program.cs` if you use this approach.

---

## Configuring WebAuthn origins

The `WebAuthn:Origins` array lists every origin that is permitted to complete WebAuthn ceremonies.  
An **origin** is `scheme://host:port` (port may be omitted for standard ports).

**Rules enforced at startup:**
- All origins must be HTTPS, **unless** the host is `localhost`, `127.0.0.1`, or `[::1]`.
- Invalid or non-HTTPS non-localhost origins cause the server to fail to start.

### Typical configuration for a public + local setup

```json
"WebAuthn": {
  "ServerDomain": "gunrpg.example.com",
  "ServerName": "GunRPG",
  "Origins": [
    "https://gunrpg.example.com",
    "https://yourname.github.io",
    "https://localhost:5001"
  ],
  "VerificationUri": "https://gunrpg.example.com/auth/device/verify"
}
```

> **GitHub Pages note:** If your SPA is hosted at `https://yourname.github.io/GUNRPG`,
> the origin is `https://yourname.github.io` (no path component).

---

## Browser (WebAuthn) authentication flow

### Registration (first time)

```
Browser                                  GUNRPG.Api
  │                                          │
  │─── POST /auth/webauthn/register/begin ──►│
  │    { username: "alice" }                 │
  │◄── CredentialCreateOptions JSON ─────────│
  │                                          │
  │  navigator.credentials.create(options)   │
  │  (user touches YubiKey / FaceID)         │
  │                                          │
  │─── POST /auth/webauthn/register/complete ►│
  │    { username, attestationResponseJson } │
  │◄── { accessToken, refreshToken, ... } ───│
```

### Login

```
Browser                                  GUNRPG.Api
  │                                          │
  │─── POST /auth/webauthn/login/begin ─────►│
  │    { username: "alice" }                 │
  │◄── AssertionOptions JSON ────────────────│
  │                                          │
  │  navigator.credentials.get(options)      │
  │  (user touches YubiKey / FaceID)         │
  │                                          │
  │─── POST /auth/webauthn/login/complete ──►│
  │    { username, assertionResponseJson }   │
  │◄── { accessToken, refreshToken, ... } ───│
```

---

## Console device code flow

For console clients that cannot open a browser, GunRPG implements
[RFC 8628 Device Authorization Grant](https://www.rfc-editor.org/rfc/rfc8628).

```
Console client              Browser (user)              GUNRPG.Api
     │                           │                           │
     │─── POST /auth/device/start ──────────────────────────►│
     │◄── { device_code, user_code, verification_uri, ... } ─│
     │                           │                           │
     │  Display to user:         │                           │
     │  "Go to https://... and   │                           │
     │   enter code: ABCD1234"   │                           │
     │                           │                           │
     │                           │─── Open verification_uri  │
     │                           │    Complete WebAuthn ─────►│
     │                           │   POST /auth/device/authorize?userCode=ABCD1234
     │                           │◄── 200 OK ────────────────│
     │                           │                           │
     │─── POST /auth/device/poll (every ~5s) ───────────────►│
     │◄── { status: "authorization_pending" } ───────────────│
     │    (keep polling)                                      │
     │                           │                           │
     │─── POST /auth/device/poll ───────────────────────────►│
     │◄── { status: "authorized", tokens: { ... } } ─────────│
     │                           │                           │
     │  Store tokens, proceed    │                           │
```

### Poll status values (RFC 8628 §3.5)

| Status | Meaning | Action |
|---|---|---|
| `authorization_pending` | User hasn't acted yet | Keep polling at the current interval |
| `slow_down` | Polling too fast | Increase poll interval by 5 seconds |
| `expired_token` | Device code expired | Restart the flow with a new device code |
| `authorized` | Success — tokens in body | Store tokens and proceed |

### Console client example (C#)

```csharp
var http = new HttpClient { BaseAddress = new Uri("https://gunrpg.example.com") };

// 1. Start
var start = await http.PostAsJsonAsync("/auth/device/start", new { });
var deviceResponse = await start.Content.ReadFromJsonAsync<DeviceCodeResponse>();

Console.WriteLine($"Visit: {deviceResponse.VerificationUri}");
Console.WriteLine($"Enter code: {deviceResponse.UserCode}");

// 2. Poll
var pollInterval = TimeSpan.FromSeconds(deviceResponse.PollIntervalSeconds);
while (true)
{
    await Task.Delay(pollInterval);
    var poll = await http.PostAsJsonAsync("/auth/device/poll",
        new { DeviceCode = deviceResponse.DeviceCode });
    var pollResponse = await poll.Content.ReadFromJsonAsync<DevicePollResponse>();

    switch (pollResponse!.Status)
    {
        case "authorized":
            // Store pollResponse.Tokens.AccessToken and .RefreshToken
            return pollResponse.Tokens!;
        case "slow_down":
            pollInterval += TimeSpan.FromSeconds(5);
            break;
        case "expired_token":
            throw new Exception("Device code expired. Please restart.");
        // "authorization_pending" → keep polling
    }
}
```

---

## JWT tokens

- **Algorithm:** EdDSA with Ed25519 (via BouncyCastle)
- **Key storage:** The Ed25519 key pair is generated on first startup and stored in LiteDB (`identity_meta` collection). It survives restarts.
- **Key ID (`kid`):** Every token header contains a `kid` claim — a SHA-256 thumbprint of the public key bytes. This enables future key rotation: validators can look up the correct key by `kid`.
- **Standard claims:** `sub` (user ID), `jti` (unique token ID), `iat` (issued at), `exp` (expiry), `preferred_username`, `account_id`.
- **Access token lifetime:** 15 minutes (configurable via `Jwt:AccessTokenExpiryMinutes`).
- **Refresh token lifetime:** 30 days (configurable via `Jwt:RefreshTokenExpiryDays`).

---

## Refresh token rotation

Every call to `POST /auth/token/refresh` **consumes** the supplied refresh token and issues a brand-new pair.

- The old refresh token is marked `IsConsumed = true` in LiteDB and is permanently invalid.
- A second use of the same refresh token returns `401 Unauthorized` with `error: "invalid_grant"`.
- All refresh tokens for a user can be revoked (logout from all devices) via `ITokenService.RevokeAllAsync`.

---

## appsettings.json reference

```json
{
  "Jwt": {
    "Issuer": "gunrpg",
    "Audience": "gunrpg",
    "AccessTokenExpiryMinutes": 15,
    "RefreshTokenExpiryDays": 30
  },
  "WebAuthn": {
    "ServerDomain": "localhost",
    "ServerName": "GunRPG",
    "Origins": ["https://localhost", "http://localhost"],
    "VerificationUri": "https://localhost/auth/device/verify"
  }
}
```

---

## API endpoint reference

### WebAuthn (`/auth/webauthn/`)

| Method | Path | Auth | Description |
|---|---|---|---|
| `POST` | `/auth/webauthn/register/begin` | None | Begin credential registration |
| `POST` | `/auth/webauthn/register/complete` | None | Complete registration, receive tokens |
| `POST` | `/auth/webauthn/login/begin` | None | Begin authentication assertion |
| `POST` | `/auth/webauthn/login/complete` | None | Complete login, receive tokens |

**Error response format** (all WebAuthn endpoints):
```json
{
  "code": "AttestationFailed",
  "message": "Signature verification failed: ..."
}
```

Error codes: `InvalidRequest`, `ChallengeMissing`, `AttestationFailed`, `AssertionFailed`,
`CredentialNotFound`, `CounterRegression`, `UserNotFound`, `InternalError`.

### Token management (`/auth/token/`)

| Method | Path | Auth | Description |
|---|---|---|---|
| `POST` | `/auth/token/refresh` | None | Rotate refresh token |

Body: `{ "refreshToken": "..." }`  
On failure: `401 { "error": "invalid_grant", "error_description": "..." }`

### Device code flow (`/auth/device/`)

| Method | Path | Auth | Description |
|---|---|---|---|
| `POST` | `/auth/device/start` | None | Start device code flow |
| `POST` | `/auth/device/authorize?userCode=XXXX` | Bearer JWT | Browser binds user to device code |
| `POST` | `/auth/device/poll` | None | Poll for authorization status |
