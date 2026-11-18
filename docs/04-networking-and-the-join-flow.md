# 04 — Networking and the join flow

This chapter explains the FishNet basics you need, then walks the full path from "a client opens a
socket" to "the player is standing in the world", including token authentication and moving between
zones.

## 1. FishNet in two minutes

FishNet is the networking library. Four ideas cover almost everything in this project:

- **NetworkManager** — the root object on both client and server. It owns the transport (here,
  `Tugboat`, which is simple TCP), the authenticator, and the list of "spawnable" prefabs.
- **NetworkObject / NetworkBehaviour** — an object or component that exists on the network.
  `NetworkBehaviour` is the base class for components that replicate state or send RPCs.
- **SyncVar / SyncList** — replicated state. The server writes; clients receive. In this project
  SyncVars are always declared in object form, `private readonly SyncVar<int> _health = new();`,
  because the `[SyncVar]` attribute is obsolete in FishNet 4.x and does nothing.
- **RPCs** — remote calls. `[ServerRpc]` runs on the server from a client; `[TargetRpc]` runs on one
  client from the server. A `ServerRpc` always has a `NetworkConnection sender` parameter so the
  server can re-check that the sender really owns the object.

A **broadcast** is a named message registered on both ends. This project uses broadcasts for the join
handshake and the zone-transfer redirect because they can be sent on a channel that does not require
authentication (a joining client is not yet authenticated).

## 2. The problem the join flow solves

FishNet calls the authenticator once a client has finished the version handshake. At that moment the
server knows *nothing* about the client — not even a name. But this game needs to:

1. find out who the client is,
2. optionally prove the session token against the backend,
3. optionally load the player's save so they do not spawn with default stats,
4. and only then decide to admit or reject.

So the verdict is **deferred**. The connection is parked in a waiting list, the client announces
itself over an unauthenticated broadcast, and the server resolves the join within a time limit.

## 3. The connection gate

`MMOConnectionGate` (`Assets/Scripts/Net/MMOConnectionGate.cs:35`) is a FishNet `Authenticator`. Its
job is to approve or reject each incoming connection by invoking `OnAuthenticationResult`.

```
Client                                   Server (MMOConnectionGate)                Nakama
  │  TCP connect                           │
  │───────────────────────────────────────►│ OnRemoteConnection → tracker.Begin()
  │                                        │   ... handshake deadline ticking (default 5s) ...
  │  JoinRequestBroadcast (unauthenticated) │
  │  { PlayerName, AccountId, AuthToken,    │
  │    TransferToken }                      │
  │───────────────────────────────────────►│ HandleJoinRequest(...)
  │                                        │   choose a path (table below)
  │                                        │
  │                                        │ Async: validate token and/or load save
  │                                        │   re-check handshake still live
  │  player spawned ◄──────────────────────│ Apply(...) → OnAuthenticationResult(true)
  │  JoinResultBroadcast (if rejected) ◄───│ SendRejection(...)
```

### The settings

`MMOConnectionSettings` (`Assets/Scripts/Net/MMOConnectionSettings.cs:11`) is serialised onto the
`NetworkManager`:

| Setting | Default | Meaning |
|---|---|---|
| `Port` | 7777 | The zone's TCP port. |
| `MaxClients` | 64 | Server capacity; joins are refused when full. |
| `HandshakeTimeoutSeconds` | 5 | How long a parked connection may take to identify itself. |
| `MaintenanceMode` | false | Refuse everyone (except via the allow list). |
| `AllowedPlayers` / `BlockedPlayers` | empty | Name lists used by the join policy. |
| `UseTokenAuth` | false | When true, `TokenAuthBootstrap` wires a real token validator at load. |

### The handshake tracker

`JoinHandshakeTracker` (`Assets/Scripts/Net/JoinHandshakeTracker.cs:6`) is a small pure helper. For
each parked client it records:

- when the connection arrived (`Begin`, `:14`),
- a `CancellationTokenSource` (`TokenFor`, `:40`) so an in-flight backend call can be cancelled when
  the handshake dies,
- and it can list expired clients (`Expired`, `:46`).

When a client disconnects, `End` (`:22`) removes it and cancels its token. The gate's `Update`
(`MMOConnectionGate.cs:461`) reaps expired handshakes every frame.

### The broadcast the client sends

`JoinRequestBroadcast` (`Assets/Scripts/Net/JoinBroadcasts.cs:5`) carries:

| Field | Meaning |
|---|---|
| `PlayerName` | The display name the client typed. |
| `AccountId` | The stable device id (a GUID in `PlayerPrefs`). Used on the legacy open-join path; ignored by token auth. |
| `AuthToken` | The Nakama session token from sign-in. Present only when token auth is on. |
| `TransferToken` | The zone-transfer token. Present only on a redirected join. |

`JoinResultBroadcast` (`JoinBroadcasts.cs:31`) is the reply: `Approved` plus a `Reason` string when
rejected. It is sent on the unauthenticated channel because the client is still (from FishNet's
point of view) not authenticated when it is rejected.

`JoinRequestClient` (`Assets/Scripts/Net/JoinRequestClient.cs:15`) is the client half. It:

- keeps a stable account id in `PlayerPrefs` under `MMO.AccountId` (`:18`), generating one on first
  run,
- subscribes to the client connection state in `Start` (not `Awake`, because FishNet creates the
  `ClientManager` during `Awake`),
- and sends the join request the moment the socket is up:
  `SendJoinRequest` (`:109`) checks `ClientManager.Started` (the *socket*, not admission) and
  broadcasts. Right after broadcast it clears `_transferToken` so a later reconnect can never replay
  a single-use token.

### The decision paths

`HandleJoinRequest` (`MMOConnectionGate.cs:149`) is the routing table. It is public so tests can call
it without a socket. Its checks, in order:

1. **Already authenticated?** Reject with `ReasonAlreadyJoined` (`:153`). A legitimate client
   announces itself exactly once, before admission.
2. **Transfer token present and a transfer store is wired?** Go to `TransferAndDecideAsync` (`:162`).
3. **Token validator wired?** Require `AuthToken` (`ReasonMissingAuthToken` if absent), then
   `ValidateAndDecideAsync` (`:169`).
4. **No `PlayerLoader` (no backend)?** Decide immediately with `EvaluateJoin` (`:183`) — this is the
   legacy open-join path used by tests and local demos.
5. **Otherwise** require `accountId` (`ReasonMissingAccountId` if absent), then `LoadAndDecideAsync`
   (`:189`).

The rejection reason constants are at `MMOConnectionGate.cs:40-55`.

### The two deferred waits

When token auth is on, `ValidateAndDecideAsync` (`MMOConnectionGate.cs:241`) first asks the
`ITokenValidator` to vouch for the token. Important behaviour:

- If the backend throws, the validator **fails closed**: the identity is treated as invalid
  (`MMOConnectionGate.cs:253`). An unreachable backend must not open the gate.
- If the identity is invalid, the join is rejected with `ReasonInvalidAuthToken` (`:260`).
- If the identity is valid, the **account key becomes `identity.UserId`** — never the account id the
  client claimed on the wire (`:286`).
- Optionally it checks the identity against an `expectedAccountId` (used by the transfer path).

Then either path funnels into `LoadAndDecideAsync` (`MMOConnectionGate.cs:206`), which fetches the
save record:

```csharp
record = await PlayerLoader.LoadAsync(accountId, _tracker.TokenFor(clientId));

// Timed out or disconnected while reading — applying now would admit a ghost.
if (!_tracker.IsPending(clientId))
    return;

PlayerJoinLoaded?.Invoke(clientId, accountId, record);
Apply(connection, Decide(playerName, CountAuthenticatedClients(),
    _tracker.Elapsed(clientId, Time.unscaledTimeAsDouble)));
```

Note the re-check `if (!_tracker.IsPending(clientId)) return;` — every async continuation in the gate
does this. A client that timed out or disconnected while the backend was being read is dropped, not
admitted.

### Committing the verdict

`Apply` (`MMOConnectionGate.cs:399`) is the single place that ends a handshake:

```csharp
if (!decision.Approved)
    SendRejection(connection, decision.Reason);

_tracker.End(connection.ClientId);
OnAuthenticationResult?.Invoke(connection, decision.Approved);
```

`OnAuthenticationResult(true)` promotes the connection; `false` disconnects it. `SendRejection`
broadcasts the `JoinResultBroadcast` first so the client can show a reason.

### The capacity/allow/block policy

`JoinPolicy.Evaluate` (`Assets/Scripts/Net/JoinPolicy.cs:41`) is a pure function deciding admission
for the non-token parts. It checks, in order: maintenance mode, invalid capacity, timed-out
handshake, blocked name, missing name, allow-list membership, and finally "server full". It returns a
`JoinDecision`. Keeping this pure means every rule has an EditMode test.

## 3a. Transport trust and certificate pinning

The backend and the game servers can present TLS certificates, but trusting a public CA is only as
strong as the weakest CA in the client's trust store. A build may therefore ship a **pin set**:
`PinSet` (`Assets/Scripts/Net/PinSet.cs`) holds the SPKI SHA-256 of the backend certificate and of
each game-server certificate, each with a **primary** and a **backup** pin. Two pins are mandatory
because a single pin turns a routine certificate rotation into a client-wide outage; the backup is
the next certificate, shipped before the server rotates.

The rules are pure (`PinRules`, `Assets/Scripts/Net/PinRules.cs`):

- `IsWellFormed` requires two valid, different backend pins and a matched primary/backup pair for
  every declared game-server pin.
- `Verify` normalises hex (case, `:`, `-`, whitespace) and accepts the primary **or** backup.
- `ShouldEnforce` is true only when the set requires pinning **and** the host is not loopback, so
  local self-signed development is never bricked.

`CertificatePinning.TrySpkiSha256` derives the presented hash from a certificate's DER bytes, and
`CertificatePinHandler` plugs it into `UnityWebRequest` for both the login gateway
(`NakamaApiGateway`) and the game server's backend client (`NakamaRestClient`). A mismatch refuses
the connection; a dev loopback connection passes through untouched. The document is loaded from
`Resources/Net/PinSet.json` by `PinSetLoader`; the release pipeline (#85) attaches the environment's
set. `PinningBuildGuard` (`Assets/Editor/PinningBuildGuard.cs`) fails a build that ships a set
marked `RequirePinning` but not well formed.

## 4. Building the server object graph

`NetworkManagerFactory.Create` (`Assets/Scripts/Net/NetworkManagerFactory.cs:41`) is the single
source of truth for what a zone server's `NetworkManager` object contains. It is used by the editor
baker *and* by tests:

1. Create the `NetworkManager` GameObject and disable it so nothing runs until fully wired.
2. Add the `Tugboat` transport, set the port and max clients.
3. Create a `SinglePrefabObjects` collection named `MMOSpawnablePrefabs`.
4. Build any missing prefabs (player, target dummy, crafting station, loot bag) via their factories.
5. Register them in a fixed order so network object ids are deterministic: player = 0, dummy = 1,
   crafting station = 2, loot bag = 3.
6. Add the `MMOConnectionGate`, call `SetSettings`, add `JoinRequestClient`, add
   `TokenAuthBootstrap` (which wires the validator from `UseTokenAuth`), add
   `PlayerPersistenceService`, add `SocialHubService`, add `ZoneTransferTokenService` and
   `ZoneTransferClient`.
7. Activate the object (so `Awake` runs in the right order), then attach the prefab spawners, then
   `persistence.AttachTo(gate)` to give the gate its loader, and
   `gate.TransferTokenStore = tokenService.Store`.

`PlayerPrefabFactory.Create` (`Assets/Scripts/Net/PlayerPrefabFactory.cs:73`) builds the player
capsule and adds its components in a deliberate order: `NetworkObject`, then `PlayerStats`,
`PlayerCombat`, `PlayerInventory`, `PlayerAbilities`, `PlayerGathering`, `PlayerCrafting`,
`PlayerDeath` (the risk-tier resolver — it must come after the stats and inventory it reads),
`PlayerSaveTrigger`, `PlayerParty`, `PlayerGuild`, `PlayerRaid`, then `PlayerMover`,
`PlayerClickToMove` and the `NetworkTransform`. The `PlayerMover` is the sole writer of the
player's position (see [§7](#7-server-authoritative-movement)
and [14](14-server-authority-hardening.md)); `PlayerClickToMove` is the owner-side driver that
submits movement intents to it, and the `NetworkTransform` (owner-authoritative **off**) ships the
server's result to clients. The camera deliberately does **not** travel with the prefab — the scene's
single `PersistentCameraController` rig is bound to whichever player this client **owns**, through
`LocalPlayerCameraTarget`. A tag search is only the fallback, but it is not confined to a scene with no
local player: `OnStopNetwork` is not owner-guarded, so any player's teardown — a remote avatar leaving
the zone — clears the shared binding, and the rig then follows whatever the tag search finds on this
client until a local player binds again (see [09](09-editor-tooling-and-scenes.md)).
`PlayerPrefabFactory.Attach` (`:187`) installs a FishNet `PlayerSpawner`, which is what actually
instantiates the player when the gate admits a connection.

### World bootstrap and player placement

The `NetworkManager` object graph is generic; the **scene** layers the world on top of it. A zone
scene carries a server-only `WorldBootstrap` (`Assets/Scripts/Net/WorldBootstrap.cs`) on the
`NetworkManager` object. On server start it:

- spawns every `ResourceNodeMarker` and `CraftingStationMarker` under the scene's `Markers` root,
  and gives every `EnemyMarker` a server-side `SpawnController` that owns its population, density
  and respawn timer (each marker names the prefab to use, so the level is authored by placing
  markers, not by hand-spawning networked objects), and
- subscribes to the `PlayerSpawner`'s `OnSpawned` event so it can place each joining player.

Placement is decided by the scene's `PlayerSpawnPoint`s (`Assets/Scripts/Net/PlayerSpawnPoint.cs`).
Every spawn point is tagged with the component; the first is the zone default. `ChooseSpawn` is pure
and unit-tested (`PlayerSpawnPointTests`): a returning player whose save carried a position is placed
at the **nearest** spawn point to it, while a brand-new player (no saved position) gets the default
point. The chosen position is written through `PlayerMover.SetAuthoritativePosition` — the sole
writer of player position — so the client sees a server-authored landing spot rather than its own
initial transform. Markers and spawn points are baked into a scene by `WorldBootstrapBaker` (see
[09](09-editor-tooling-and-scenes.md)).

Because the tutorial, safe and wilderness zones share one coordinate space (a 40×40 graybox), a
saved position from the source zone is meaningful in the destination, so a zone transfer keeps a
player roughly where they were instead of dumping them at a fixed point.

## 5. Zone transfers

A zone is one server process. Moving a player between zones is a **server-driven handoff**; the
client asks for the gate it stands at, but it never chooses where to go.

The sequence:

```
Source zone server                                        Destination zone server
  │ PlayerZoneTransfer.RpcRequestTransfer (E intent)        │
  │  ├─ server re-checks the player's gate range            │
  │  ├─ force-save the player NOW                           │
  │  ├─ mint a short-lived single-use token                 │
  │  └─ broadcast ZoneTransferBroadcast ───► client         │
  │                                          │              │
  │                                          │ reconnect    │
  │                                          ▼              │
  │                            JoinRequestBroadcast (TransferToken)
  │─────────────────────────────────────────────────────────►│ gate consumes token,
  │                                                          │ admits under saved account
```

### The trigger

`ZoneTransferTrigger` (`Assets/Scripts/Gameplay/ZoneTransferTrigger.cs:32`) is a trigger collider at
the edge of a zone. The owning client's `PlayerZoneTransfer` (`Assets/Scripts/Client/Player/PlayerZoneTransfer.cs`)
shows the HUD prompt naming its `TargetZoneName` while the player is in range, and sends a bare
intent on E. The server re-resolves the nearest gate at the player's authoritative position before
it runs `BeginHandoff`, which:

1. snapshots the player while their SyncVars are still live
   (`PlayerSaveTrigger.CaptureNow()`), and force-saves it (`PlayerPersistenceService.FlushAsync`),
2. aborts if the save fails — better to keep the player where they are than deliver a fresh
   character to the next zone,
3. builds a `ZoneTransferHandoff` and runs it (`ZoneTransferTrigger.cs:190`),
4. broadcasts the redirect with the token (`requireAuthenticated: true`), then disconnects the client
   after a short grace period.

### The handoff (pure, testable)

`ZoneTransferHandoff.RunAsync` (`Assets/Scripts/Net/ZoneTransferHandoff.cs:63`) is plain C#:

```csharp
if (string.IsNullOrEmpty(accountId)) return null;
if (!await _forceSave(accountId)) return null;   // save first — abort if it fails
var token = new ZoneTransferToken { /* account, source, target, host, port */ };
string tokenString = await _store.IssueAsync(token, ct);
if (string.IsNullOrEmpty(tokenString)) return null;
return new ZoneTransferBroadcast { /* target host/port/server id, token, reason */ };
```

### The token

`ZoneTransferTokenStore` (`Assets/Scripts/Net/ZoneTransferTokenStore.cs:20`) mints and consumes
tokens. A token is:

```
base64url(json token payload) + "." + base64url(HMAC-SHA256(payload, secret))
```

So any process that holds the shared secret can verify it without a database round-trip. Key rules:

- Default lifetime is 30 seconds (`ZoneTransferTokenStore.cs:23`). The token also records issued-at
  and expires-at.
- Tokens are **single-use**: `ConsumeAsync` (`:66`) revokes the token id under a lock and refuses it
  a second time. A bounded list of consumed ids is pruned when it grows past 1024.
- Verification is a fixed-time signature compare (`:174`).
- `allocated` field `TokenId` makes each token unique even for the same account and destination.

`zone transfer tokens` require every server in a deployment to share the signing secret
(`ZoneTransferTokenService.secretKey`), exactly as they share `BackendSettings.ServerDeviceId`.

### The destination gate

When a join arrives carrying a transfer token, `TransferAndDecideAsync`
(`Assets/Scripts/Net/MMOConnectionGate.cs:306`) consumes it. A forged, expired, already-used or
account-mismatched token is rejected. A valid one admits the player under the account the *source*
server saved, and loads that account's record from the shared backend — so the player arrives with
the right stats and inventory. The signed transfer token is also sufficient when the client's
Nakama session token expired during play; a long session must not strand the player at the gate.

### The client half

`ZoneTransferClient` (`Assets/Scripts/Net/ZoneTransferClient.cs:25`) shows the loading screen, stops
the current connection, retargets the transport at the destination host/port, reconnects, arms the
`JoinRequestClient` with the transfer token, and waits until the client is admitted before clearing
the loading screen.

## 6. Fail-open versus fail-closed at a glance

| Path | Backend down |
|---|---|
| No token validator, no loader (local/demo) | Open join (legacy). |
| Loader only | Join still succeeds; player spawns **fresh** (loader returns null). |
| Token validator wired | Join is **refused** (validator fails closed). |
| Transfer token | Join is refused unless the token consumes. |

## 7. Server-authoritative movement

Once the player is admitted, their position is still not theirs to set. `PlayerMover`
(`Assets/Scripts/Gameplay/PlayerMover.cs:29`) is the **only** thing that writes it. A client sends an
**input direction**, never a position:

```
client (owner)                            server (PlayerMover)
  RequestMove(input) ─── ServerRpc ─────► RpcRequestMove(input, sender)
   • non-owners get no RPC                  • re-check sender owns the object
                                             • ApplyInput(input, dt)  (server integrates at MaxSpeed)
                                             • MoveBy(dir * MaxSpeed * dt) → capture authoritative position
```

The input path carries no position, so there is nothing for a cheating client to inflate. Any
message that *does* carry a position goes through `ApplyReportedPosition`, which judges it with the
pure `MovementRules` and snaps an impossible delta back to the last server-observed position. Notable
members: `RequestMove` (`:60`), `RpcRequestMove` (`:74`), `ApplyInput` (`:89`),
`ApplyReportedPosition` (`:106`), `Teleport` (`:125`). The mover works with or without a
`CharacterController`. See [14](14-server-authority-hardening.md#1-movement-is-server-authoritative)
for the rules and the tests.

### Click-to-move

A click is a **destination**, not a direction, so the server plans the route: `RpcRequestMoveTo`
samples the destination onto the zone NavMesh, plans a route over that mesh with
`NavMesh.CalculatePath`, and walks the route's corners through the same `ApplyInput` path. Because
the route is planned on the NavMesh, it bends around the holes that solid objects carve into it —
a crafting station's `NavMeshObstacle` makes the route go around the workbench instead of through
it. The client only says where the player wants to stand; the server decides the way there.

## Common tasks

- **Add a new zone server.** Copy an existing zone scene, or bake a fresh one with
  `Tools > MMO > Create NetworkManager In Scene`. Set the port, and set `UseTokenAuth` if you want
  verified joins. Boot the backend first.
- **Turn on token auth for a zone.** In the baked scene, set the `MMOConnectionGate` settings'
  `UseTokenAuth` flag to true. `TokenAuthBootstrap` wires the validator at load — no code change.
- **Change the handshake timeout.** Edit `HandshakeTimeoutSeconds` in `MMOConnectionSettings`
  (default 5 s) where the settings are configured.
- **Change the zone-transfer token lifetime.** Change `_tokenTtlSeconds` on the
  `ZoneTransferTokenService` component.
- **Add a new rejection reason.** Add a constant to `MMOConnectionGate` and use it where the path
  rejects; it travels in `JoinResultBroadcast.Reason`, so the client can display it.
- **Debug a refused join.** Check the server console. The gate logs the token/loader failures, and
  the client receives the `JoinResultBroadcast` reason. `PendingHandshakes` on the gate tells you how
  many connections are currently parked.
