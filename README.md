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

## Testing

- EditMode tests cover the pure mapping, DTO round-trip, the save queue/flush and the join gate's
  load deferral with a fake repository — no live backend.
- PlayMode tests cover the full connect → load → save lifecycle against a fake repository.
- Open via **Window > General > Test Runner**.

## Layout

- `Assets/Scripts/Gameplay` — server-owned player stats, combat, abilities.
- `Assets/Scripts/Inventory` — grid bag + equipment slots, `SyncList<ItemInstance>` containers.
- `Assets/Scripts/Net` — FishNet wiring: join gate, handshake, factory, spawners.
- `Assets/Scripts/Persistence` — `PlayerSaveRecord` DTO, `IPlayerRepository`, mapper,
  Nakama-backed repository and the per-player save service.