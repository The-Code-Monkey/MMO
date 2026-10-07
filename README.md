# Unity MMO

A 3D URP, server-authoritative MMO built on **Unity** and **FishNet**. The server owns every
player's stats, inventory and position; clients submit intended inputs and render what the server
replicates back.

## Running the backend (Nakama + PostgreSQL)

Player saves (stats, inventory) live in a central database so any server instance can read the same
player. The stack is [Nakama](https://heroiclabs.com/nakama/) on PostgreSQL:

```sh
docker compose up -d
```

This brings up:

| Service | Address | Used for |
|---|---|---|
| Nakama HTTP API | `127.0.0.1:7350` | the game server's persistence repository |
| Nakama gRPC | `127.0.0.1:7349` | (reserved) |
| Nakama console | `127.0.0.1:7351` | inspecting players/storage while developing |
| PostgreSQL | `127.0.0.1:5432` | the actual saves — `nakama` DB, `postgres`/`password` |

The shipped `BackendSettings` already point at these defaults, so a freshly started game server
connects with no configuration. First run creates the Nakama schema (`migrate up`); afterwards a
`docker compose down` keeps the `postgres` volume, so player saves survive.

### How saves work

1. A client generates a stable **account id** (a device GUID kept in `PlayerPrefs`) and announces it
   during the join handshake.
2. The server's join gate waits for that player's record to load from the database **before**
   admitting the connection, so no player ever spawns with default stats.
3. The spawned player applies the record to its `PlayerStats`/`PlayerInventory`.
4. Level ups save immediately; inventory changes save after a short debounce; disconnect and server
   shutdown flush the final state. The 30&nbsp;s autosave remains as a safety net.

Records are stored under collection `players`, keyed by account id, owned by one shared **game-server
account** (see `BackendSettings.ServerDeviceId` — keep it identical across every server instance).
Writes use Nakama's optimistic version check, so two servers racing the same player collide and retry
instead of clobbering each other.

### Running without the backend

Tests and local demos run with no database: the persistence service fails open, joins spawn the
player fresh, and saves are queued but dropped. A save is never overwritten this way — the version
check refuses to stomp a record the offline process never saw.

## Main Menu, login and the last-zone record

Zone servers boot into a scene with a baked NetworkManager (run `Tools > MMO > Create NetworkManager
In Scene`). The **Main Menu** is its own scene — baked with `Tools > MMO > Create Main Menu Scene` —
whose UI is built entirely in code (`Assets/Scripts/UI`). The menu signs the player in over Nakama's
REST gateway and then joins a zone; the join gate on the server verifies the session token before the
connection is admitted.

The flow, end to end:

1. The player types a username + password. Usernames use a safe character set (`[A-Za-z0-9._-]`, no
   point-less dots) and map deterministically to an email address `<username>@mmolocal.dev`, so the
   same account keeps working across devices; the mapping is injective so two names can never collapse
   onto one account. Passwords need at least 8 characters (the backend's own minimum).
2. `LoginConnector` asks the backend to authenticate (auto-creating a first-time account), which
   yields a session token plus the backend's user id and display name.
3. The record of where this player was last standing in (collection `player_metadata`, key
   `last_zone`) is read back. Because Nakama scopes every HTTP storage write to the *caller's*
   account, the record is player-owned: it is written by the client once the zone connection has
   actually been admitted, so it always records a server-verified fact. If none is stored, the menu's
   configured default zone is used instead.
4. The transport is pointed at that zone, the session token is handed to the join client, and the
   client connects. The server's gate asks the backend to vouch for the token; the player's save is
   then loaded under the **validated user id** and the connection is admitted.
5. Only now does the client record `last_zone` with the address/port it actually reached, so the next
   login boots the player straight back in.

To enable token-verified joins on any zone server, bake its NetworkManager with token auth on
(`MMOConnectionSettings` → `useTokenAuth`). The runtime re-wires the join gate's validator from that
serialized flag (`TokenAuthBootstrap`), so the same baked scene works in any process. With the flag
off, the join gate keeps the legacy open-join path used by tests and local demos, and the client
never sends a token.

## Testing

- EditMode tests cover the pure mapping, DTO round-trip, the save queue/flush and the join gate's
  load deferral with a fake repository — no live backend.
- PlayMode tests cover the full connect → load → save lifecycle against a fake repository, the
  code-built Main Menu login flow end to end, and the gate refusing/admitting joins by token over a
  real local socket.
- Open via **Window > General > Test Runner**.

## Layout

- `Assets/Scripts/Gameplay` — server-owned player stats, combat, abilities.
- `Assets/Scripts/Inventory` — grid bag + equipment slots, `SyncList<ItemInstance>` containers.
- `Assets/Scripts/Net` — FishNet wiring: join gate, handshake, factory, spawners.
- `Assets/Scripts/UI` — code-built screens (Main Menu, HUD).
- `Assets/Scripts/Persistence` — `PlayerSaveRecord` DTO, `IPlayerRepository`, mapper,
  Nakama-backed repository and the per-player save service; plus the client-side API gateway,
  token validation and the `last_zone` record.