# 03 — Backend and persistence

This chapter explains where a player's progress is stored, how it gets there, and how the code is
arranged so that tests and local runs work with no database at all.

## 1. The big picture

Player saves and social state live in a central **Nakama** server, which stores them in
**PostgreSQL**. Any zone server process can read the same player because they all point at the same
Nakama instance. There is no per-server file storage.

The game code never talks to Nakama directly. It talks to one small interface:

```csharp
// Assets/Scripts/Persistence/IPlayerRepository.cs
public interface IPlayerRepository
{
    /// <summary>Returns null when the account has never been saved.</summary>
    Task<PlayerSaveRecord> LoadAsync(string userId, CancellationToken ct = default);

    Task SaveAsync(PlayerSaveRecord record, CancellationToken ct = default);
}
```

Everything gameplay-side goes through `IPlayerRepository`. The production implementation is
`NakamaPlayerRepository`; tests use an in-memory fake. Swapping them changes nothing in the gameplay
code. This is the reason the whole test suite runs offline.

## 2. The save record (the data that comes back)

The unit that travels to and from the database is `PlayerSaveRecord`
(`Assets/Scripts/Persistence/PlayerSaveRecord.cs`). It is a plain C# class that Unity's
`JsonUtility` can serialise to JSON.

| Field | Type | Meaning | Line |
|---|---|---|---|
| `UserId` | `string` | The player's account id. This is the database key. | 62 |
| `SchemaVersion` | `int` | Format version, currently `4`. Lets future code migrate old saves. | 64 |
| `Level` | `int` | Player level. | 66 |
| `Health` | `int` | Current health, clamped to the cap when applied. | 69 |
| `Mana` | `int` | Current mana. | 71 |
| `Experience` | `int` | Quest/event XP. There is no player level curve yet. | 74 |
| `Position` | `Vector3` | Reserved. Round-trips but is not applied yet (there is no movement save/load). | 77 |
| `StorageVersion` | `string` | The backend's revision hash. Not player data; used for safe concurrent writes. | 80 |
| `Gold` | `long` | Legacy scalar gold. Kept so pre-wallet saves load; the wallet migrates it into `Currencies` and keeps it in sync. | 88 |
| `Currencies` | `List<CurrencyBalance>` | The player's multi-currency wallet (gold, silver, faction marks). Empty in a legacy save. | 96 |
| `BankVault` | `List<CurrencyBalance>` | Currency held in the personal bank vault, separate from the carried wallet. | 103 |
| `PendingWalletReservations` | `List<WalletReservationRecord>` | Auction debits reconciled against listing/order state after interruption. | — |
| `BankItems` | `List<ItemSaveData>` | Items held in the personal bank's 8x6 grid. Same item shape as the bag; never worn. | 108 |
| `Items` | `List<ItemSaveData>` | Both inventory containers flattened into one list (bag + worn). | 108 |
| `CompletedQuestIds` | `List<string>` | Quest ids that have already paid their reward. | 88 |
| `QuestProgress` | `List<QuestProgressEntry>` | Active objective counters. | 91 |

Each inventory entry is an `ItemSaveData` (`PlayerSaveRecord.cs:16`):

| Field | Meaning |
|---|---|
| `ItemName` | Registry key both ends resolve locally; the definition itself never travels. |
| `GridX`, `GridY` | Position in the bag grid. |
| `Width`, `Height` | Footprint size. |
| `Durability` | Remaining wear; restored clamped to the definition's `MaxDurability`. |
| `EquipmentSlot` | `-1` means "in the bag"; otherwise the worn slot index. |

The default values match the gameplay constants: `Level = PlayerStats.DefaultLevel` (1),
`Health = PlayerStats.MaxHealth` (100), `Mana = PlayerStats.MaxMana` (50).

## 3. Mapping between live objects and the record

`PlayerSaveMapper` (`Assets/Scripts/Persistence/PlayerSaveMapper.cs`) is the **only** code that
converts between live gameplay components and the record, and it is pure, so it is unit tested.

- `PlayerSaveMapper.Capture(...)` (`PlayerSaveMapper.cs:22`) takes plain values and lists — level,
  health, mana, experience, gold, position, the bag/equipment lists and optional quest content —
  and produces a `PlayerSaveRecord`. It skips empty items and marks bag items with
  `EquipmentSlot = -1`.
- `PlayerSaveMapper.Apply(...)` (`PlayerSaveMapper.cs:96`) reads a record back into two lists
  (`out List<ItemInstance> bag`, `out List<ItemInstance> equipment`), reports how many items were
  **skipped**, and returns the stored gold. The content overload (`:111`) also returns experience,
  completed quest ids and quest progress. Item durability is restored clamped to the definition's
  current maximum.

A skipped item is one the local game cannot resolve — a saved item name with no matching
`ItemDefinition`, or a worn slot index that no longer exists. Skipping rather than crashing means a
save written by a newer build (or with removed content) still loads. The count is logged as a
warning.

## 4. The save cadence (when things are written)

Writing on every tiny change would hammer the database. `PlayerPersistenceService`
(`Assets/Scripts/Persistence/PlayerPersistenceService.cs:33`) owns the connection and decides when
to save. There is one per process, reachable as `PlayerPersistenceService.Instance`.

Two serialised knobs control the timing:

- `_autosaveInterval = 30f` (`PlayerPersistenceService.cs:40`) — the safety-net autosave every 30
  seconds.
- `_saveDebounce = 2f` (`PlayerPersistenceService.cs:41`) — the delay applied to non-urgent saves.

Here is every way a save can be triggered:

| Event | Code path | Timing |
|---|---|---|
| **Level up** | `PlayerSaveTrigger.OnLevelChanged` → `MarkDirty(immediate: true)` | Save now. Progress nobody wants to lose. |
| **Inventory change** | `PlayerSaveTrigger.OnInventoryChanged` → `MarkDirty(immediate: false)` | Save after the 2 s debounce, so dragging 20 items is not 20 writes. |
| **Every 30 s** | `PlayerPersistenceService.Update` marks every live player dirty | A safety net even if nothing changed. |
| **Disconnect / despawn** | `PlayerSaveTrigger.OnStopNetwork` → `QueueFinalSave(...)` | Immediate, using a snapshot taken while the SyncVars are still live. |
| **Server stop / app quit** | `FlushAsync()` | Forces every dirty player to save now. |
| **Zone transfer** | `FlushAsync(accountId)` | Forces one player to save now; the handoff aborts if it fails. |
| **Auction / buy-order reserve** | `AddWalletReservationAsync` | Saves debit and pending operation together before mutation; join-time reconciliation refunds if operation never committed. |

`MarkDirty` is the core (`PlayerPersistenceService.cs:264`):

```csharp
public void MarkDirty(string accountId, bool immediate)
{
    if (string.IsNullOrEmpty(accountId))
        return;
    _dirty.Add(accountId);
    _debounceUntil[accountId] = immediate
        ? Time.unscaledTimeAsDouble
        : Time.unscaledTimeAsDouble + _saveDebounce;
}
```

`Update` calls the "due saves" pump (`ProcessDueSavesAsync`, `PlayerPersistenceService.cs:293`),
which collects every account whose debounce deadline has passed and saves it. If a save fails, the
account stays dirty and is re-debounced (pushed 2 s into the future) so a dead backend cannot spin in
a tight retry loop.

`AddWalletReservationAsync` saves the debit and its listing/order id in one player record before the
auction mutation. A crash before that mutation leaves a pending marker. On the next join,
`PlayerSaveTrigger` checks the listing or order: it keeps committed debits and refunds missing or
uncommitted operations. `CompleteWalletReservationAsync` saves the resolved balance and removes the
marker. A failed refund save leaves the marker for another reconciliation attempt.

The save pump is single-flight: a `_saving` flag means only one save batch runs at a time.

## 5. Loading a join

The server's join gate asks the persistence service for a record **before** admitting the player
(see [04](04-networking-and-the-join-flow.md)). `PlayerPersistenceService` implements the
`IPlayerLoader` interface for exactly this purpose:

```csharp
// Assets/Scripts/Persistence/PlayerPersistenceService.cs:179
public async Task<PlayerSaveRecord> LoadAsync(string accountId, CancellationToken ct)
```

Rules it follows:

- Not connected to the backend → return `null` (the player spawns fresh).
- Blank account id → return `null`.
- Any read error → log it and return `null`. **A join never fails because the database is down.**
- Cancellation (the handshake timed out) is re-thrown so the caller can stop cleanly.

When the record is loaded, the gate raises `PlayerJoinLoaded`, the service caches the record under
the client id, and the spawning player asks for it via `TryGetJoin`. The cache keeps the record
around (it is not consumed) because both `PlayerStats` and `PlayerInventory` may need to read it, in
either order.

`PlayerSaveTrigger.OnStartNetwork` (`Assets/Scripts/Persistence/PlayerSaveTrigger.cs:51`) is where
the loaded record is applied to the live player: it calls `PlayerSaveMapper.Apply`, pushes health/
mana/level through `PlayerStats.ApplySave`, and rebuilds the inventory through
`PlayerInventory.Restore`.

## 6. Safe concurrent writes (optimistic versioning)

Two zone servers can load the same player (for example during a zone transfer). To stop one from
overwriting the other's fresh write with a stale copy, Nakama storage writes use an **optimistic
version check**:

1. `NakamaPlayerRepository.LoadAsync` (`Assets/Scripts/Persistence/NakamaPlayerRepository.cs:48`)
   reads the object and copies its `Version` hash onto the record's `StorageVersion`.
2. `SaveAsync` (`NakamaPlayerRepository.cs:63`) writes back carrying that version.
3. `NakamaRestClient.WriteObjectAsync` (`Assets/Scripts/Persistence/NakamaRestClient.cs:172`) turns
   an empty version into `"*"` (create-if-not-exists) or an if-match on the stored version.
4. If someone else wrote in between, Nakama returns HTTP 409, which becomes a
   `StorageVersionConflictException` (`NakamaRestClient.cs:32`). The save stays dirty and is retried,
   so a stale offline snapshot can never clobber a real record.

## 7. Where records are stored

| Data | Collection | Key | Owner |
|---|---|---|---|
| Player save | `players` | the player's account id | the shared **game-server** account |
| Last zone | `player_metadata` | `last_zone` | the **player's** own account |
| Territory | `territories` | the territory id | the shared **game-server** account |
| Market search cache | `market_search_index` | `realm` | the shared **game-server** account |

The `players` collection is owned by a single shared service account. Every server process
authenticates as that account (device id from `BackendSettings.ServerDeviceId`) so they all see the
same records. This is why `BackendSettings.ServerDeviceId` must be identical on every server.

The `last_zone` record is different. Nakama scopes storage writes to the account that makes the
call, so this one is owned by the player and written by the **client** after the connection is
admitted (so it always records a *server-verified* fact). See chapter [04](04-networking-and-the-join-flow.md).

## 8. BackendSettings

`BackendSettings` (`Assets/Scripts/Persistence/BackendSettings.cs:20`) holds the connection details,
with compile-time defaults that match `docker-compose.yml`:

| Setting | Default | Constant |
|---|---|---|
| Scheme | `http` | `DefaultScheme` (`:23`) |
| Host | `127.0.0.1` | `DefaultHost` (`:26`) |
| Port | `7350` | `DefaultPort` (`:29`) |
| Server key | `defaultkey` | `DefaultServerKey` (`:32`) |
| Server device id | `mmo-game-server` | `DefaultServerDeviceId` (`:38`) |
| Server username | `game-server` | `DefaultServerUsername` (`:41`) |

## 9. The REST client

`NakamaRestClient` (`Assets/Scripts/Persistence/NakamaRestClient.cs:60`) is the raw HTTP layer. It
uses Unity's `UnityWebRequest` and speaks Nakama's REST API directly — there is no third-party
Nakama SDK and there are no Nakama RPCs. All calls use `/v2/...` endpoints.

- On connect it authenticates the shared service account with **device auth**
  (`POST /v2/account/authenticate/device?create=true&username=game-server`) using HTTP Basic auth
  carrying the server key, then reads `GET /v2/account` to learn its user id (`NakamaRestClient.cs:80`).
- `ReadObjectAsync` / `ReadObjectsAsync` read storage (`NakamaRestClient.cs:110` and `:121`).
- `WriteObjectAsync` writes with the optimistic version above (`NakamaRestClient.cs:172`).
- `DeleteObjectAsync` removes an object (`NakamaRestClient.cs:202`).
- The request timeout is 10 seconds (`NakamaRestClient.cs:62`) and a version conflict is translated
  into `StorageVersionConflictException` (`NakamaRestClient.cs:272`).

## 10. The client-side API gateway (login and last zone)

The Main Menu does not use `IPlayerRepository`. It uses a separate small interface, `IApiGateway`
(`Assets/Scripts/Persistence/IApiGateway.cs:11`), implemented by `NakamaApiGateway`
(`Assets/Scripts/Persistence/NakamaApiGateway.cs:34`):

```csharp
Task<LoginResult> LoginAsync(string username, string password, CancellationToken ct = default);
Task<LastZoneInfo> LoadLastZoneAsync(string userId, string token, CancellationToken ct = default);
Task SaveLastZoneAsync(string userId, string token, LastZoneInfo zone, CancellationToken ct = default);
```

`LoginAsync` (`NakamaApiGateway.cs:187`) does this, in order:

1. Turn the username into an email with `AuthRules.EmailForUsername`.
2. Call Nakama **email authentication** with `create=true` (so a first-time username is created).
   This uses HTTP Basic auth with the server key.
3. Parse the session token from the response.
4. Ask the backend to vouch for the token with `ValidateAsync` (a `GET /v2/account` with the bearer
   token). Without a vouched identity there is no login.
5. Try to read the player's last zone. A failure here is non-fatal — the menu just falls back to
   its configured default zone.

`LoginResult` (`Assets/Scripts/Persistence/LoginResult.cs:9`) is the outcome: either success (token,
refresh token, validated user id, username, optional last zone) or a `LoginResult.Fail(...)` with a
player-readable message. Refusals are normal results, not exceptions: `LoginAsync` never throws for
a bad password, so the UI can just show the message.

The refresh token is what keeps a long session alive. `NakamaApiGateway.RefreshSessionAsync` posts
it as the `token` field of `POST /v2/account/session/refresh` (Nakama's wire format for a refresh
request), stores both rotated tokens back on `JoinRequestClient`, and falls back to a silent sign-in
with the in-memory credentials only if Nakama rejects the refresh token itself.

### Username rules and the email mapping

`AuthRules` (`Assets/Scripts/Persistence/AuthRules.cs:23`) keeps the login contract in one place:

- `MinPasswordLength = 8` (`:25`), `MaxUsernameLength = 128` (`:26`),
  `EmailDomain = "mmolocal.dev"` (`:27`).
- `EmailForUsername` (`:34`) makes the username deterministic:
  `string.Concat(username?.Trim().ToLowerInvariant(), "@", "mmolocal.dev")`. Usernames are lowercased
  because Nakama lowercases addresses, so "Bob" and "bob" are the same account.
- `IsValidUsername` (`:38`) allows only ASCII letters, digits, `-`, `.` and `_`, and forbids a
  leading/trailing dot or a `..`. These rules make the username → email mapping **injective**: two
  different usernames can never collapse onto one account, and a blank username maps to an invalid
  email the backend rejects.
- `IsValidPassword` (`:72`) requires at least 8 characters.

### The last-zone record

`LastZoneInfo` (`Assets/Scripts/Persistence/LastZoneInfo.cs:19`) stores where the player was last
standing: `Ip`, `Port` and `SavedAt` (an ISO-8601 UTC string for diagnostics). The sentinel
`UnspecifiedAddress = "0.0.0.0"` (`:22`) means "unset", and `IsValid` (`:49`) requires a non-blank
address (not the sentinel) and a port between 1 and 65535.

The record lives in the player's own account, collection `player_metadata`, key `last_zone`. It is
read at login and written by the client only after a zone connection has actually been admitted:
once for the initial login, and again after every zone-transfer reconnect, so quitting or crashing
in SafeZone/Wilderness routes the next login back to the zone the player actually reached.

## 11. Fail-open, and why it matters

Several places are deliberately lenient when the backend is unreachable:

| Place | Behaviour when the backend is down |
|---|---|
| `PlayerPersistenceService.ConnectAsync` (`:133`) | Logs an error, marks itself disconnected; the server still starts. |
| `PlayerPersistenceService.LoadAsync` (`:179`) | Returns `null`; the player spawns fresh. |
| `SaveOneAsync` / `FlushAsync(accountId)` | Treat the write as done so gameplay is not blocked. |
| `NakamaApiGateway` zone read | Falls back to the default zone. |

The one thing that is **not** fail-open is joining a token-auth server: if the backend cannot vouch
for a token, the join is refused (fail **closed**). That distinction is intentional — see
chapter [04](04-networking-and-the-join-flow.md).

## 12. TokenAuthBootstrap

`MMOConnectionGate.TokenValidator` is a runtime property that Unity cannot serialise. To make a
baked scene remember that it wants token auth, a small component reads the serialised
`UseTokenAuth` flag at load and wires the validator:

```csharp
// Assets/Scripts/Persistence/TokenAuthBootstrap.cs:41
public void Attach(MMOConnectionGate gate)
{
    if (gate == null || !gate.Settings.UseTokenAuth || gate.TokenValidator != null)
        return;
    gate.TokenValidator = new NakamaApiGateway(new BackendSettings());
}
```

It is idempotent (calling it twice does nothing) and never downgrades — if a validator is already
attached (for example a test double), it is left alone. `NetworkManagerFactory` adds this component
to every manager it builds, so any zone server with the flag on re-wires itself automatically.

## Common tasks

- **Change the save frequency.** Edit the serialised `_autosaveInterval` / `_saveDebounce` on the
  `PlayerPersistenceService` component (or change the field defaults at
  `PlayerPersistenceService.cs:40-41`).
- **Add a field to the save.** Add it to `PlayerSaveRecord`, capture it in
  `PlayerSaveMapper.Capture`, apply it in `PlayerSaveMapper.Apply`, and bump
  `PlayerSaveRecord.CurrentSchemaVersion` if old saves need a migration. Add EditMode tests for the
  round-trip.
- **Point the backend somewhere else.** Change the `BackendSettings` defaults, or set them on the
  component in the scene before baking.
- **Reset a player.** Delete their `players/<accountId>` object in the Nakama console
  (`http://127.0.0.1:7351`).
- **Store something new in Nakama.** Add a method to `IPlayerRepository` (server-owned) or
  `IApiGateway` (client-owned), implement it on the Nakama class, and add tests with an in-memory
  fake. Never call Nakama from gameplay code directly.
