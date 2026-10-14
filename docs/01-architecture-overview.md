# 01 — Architecture overview

This chapter gives you the mental model for the whole project. Read it once and the rest of the
docs will slot into place.

## 1. What kind of game is this?

It is a **3D, server-authoritative MMO** built with Unity (Universal Render Pipeline) and FishNet
4.7.3 as the networking library. "MMO" here means more than one player can be connected at once,
and the players live on server processes ("zones") that all share one database.

The single most important rule is **server authority**:

- The **server** owns the true state of every player: position, health, mana, level, inventory,
  equipped gear, cooldowns and social membership.
- A **client** only sends *intents* — "I want to cast this ability", "I want to equip this item".
  It never changes authoritative state on its own machine and expects the server to obey.
- The server checks each intent against the rules, and only then updates the true state. FishNet
  copies that changed state back to every client so they can draw it.

If you remember only one sentence, remember: **the client asks, the server decides.**

### Why bother?

If the client could change its own health or inventory, a modified client could give itself infinite
health or free items. By putting all decisions on the server, cheating becomes "ask the server
nicely for something it will refuse". This is why nearly every method that changes something
important is marked `[Server]` (only the server may run it) or `[ServerRpc]` (a client asks the
server to run something).

## 2. The two halves of the repository

The project is a normal Unity project. The parts that matter for development are:

```
Assets/
├── Scripts/        the game code (runtime)
├── Editor/         editor-only tools ("bakers") and build guards
├── Tests/          EditMode and PlayMode test suites
├── Data/           hand-authored ScriptableObject balance data (abilities, weapons, armor)
├── Resources/       items that must load by name at runtime
├── Prefabs/         prefabs produced by the editor bakers
└── Scenes/          the Main Menu scene and zone scenes
Packages/            Unity package manifest (FishNet, URP, Cinemachine, ...)
ProjectSettings/     Unity project settings (includes the exact editor version)
docker-compose.yml   the local Nakama + PostgreSQL backend
```

Everything under `Assets/Scripts` and `Assets/Editor` is current. The `Data`, `Resources` and
`Prefabs` folders are largely generated or authored through the editor tooling described in
[09-editor-tooling-and-scenes](09-editor-tooling-and-scenes.md).

## 3. How the code is split into assemblies

Unity compiles code into **assemblies** (groups of `.cs` files that compile together). Splitting
code into assemblies keeps compile times low and enforces boundaries. This project has four:

| Assembly file | Name | Purpose | Platforms |
|---|---|---|---|
| `Assets/Scripts/MMO.Runtime.asmdef` | `MMO.Runtime` | All runtime game code. References FishNet, Unity UI and Cinemachine. | all |
| `Assets/Editor/MMO.Editor.asmdef` | `MMO.Editor` | Editor-only bakers, URP setup and the build guard. References `MMO.Runtime`. | Editor only |
| `Assets/Tests/EditMode/MMO.Tests.EditMode.asmdef` | `MMO.Tests.EditMode` | Fast unit tests. Also references `MMO.Editor` so it can test bakers. | Editor only |
| `Assets/Tests/PlayMode/MMO.Tests.PlayMode.asmdef` | `MMO.Tests.PlayMode` | Tests that need the real game loop and a real socket. Does **not** reference `MMO.Editor`. | Editor only |

Inside `MMO.Runtime` the code is organised by feature folder, and each folder has a C# namespace:

| Folder | Namespace | What lives there |
|---|---|---|
| `Assets/Scripts/Gameplay` | `MMO.Gameplay` | Player stats, combat, abilities, gathering, resource nodes, NPCs. |
| `Assets/Scripts/Inventory` | `MMO.Inventory` | The grid bag, equipment slots, item definitions and the networked inventory. |
| `Assets/Scripts/Crafting` | `MMO.Crafting` | Recipes, stations and crafting validation. |
| `Assets/Scripts/Net` | `MMO.Net` | FishNet wiring: the join gate, prefab factories/spawners, zone transfers. |
| `Assets/Scripts/Persistence` | `MMO.Persistence` | Save records, the Nakama repository, the REST client, login/token rules. |
| `Assets/Scripts/Social` | `MMO.Social` | Parties, guilds and the social hub. |
| `Assets/Scripts/UI` | `MMO.UI` | Code-built screens (Main Menu, HUD). |
| `Assets/Scripts/Bootstrap` | `MMO.Bootstrap` | The Main Menu boot script and login connector. |
| `Assets/Scripts/Client` | `MMO.Client` | Client-only camera rig. |

> **A note on `agents.md`.** The project's AI-facing guidelines file talks about four assemblies
> named `Project.Core`, `Project.Server`, `Project.Client` and `Project.Shared`. That is an
> *aspiration* from early planning, not what the code currently uses. The current reality is the
> single `MMO.Runtime` assembly plus the editor and test assemblies listed above. Treat this
> document as authoritative and use `agents.md` for the behavioural rules (server authority,
> testing), not the assembly names.

## 4. The pure-core / networked-shell pattern

The most useful pattern to understand is that almost every gameplay system is split in two:

- A **pure core** — plain C# classes and static methods that take all of their inputs as arguments
  and never read a clock (`Time.time`), never touch Unity objects, and never touch the network.
  Examples: `AbilityCastRules`, `AbilityCooldownTracker`, `AbilityEffects`, `AbilityCaster`,
  `InventoryGrid`, `InventoryValidator`, `CraftingValidator`, `GatheringRules`, `JoinPolicy`, and the
  anti-cheat rules `MovementRules` and `SightRules` (see [14](14-server-authority-hardening.md)).
- A **networked shell** — a FishNet `NetworkBehaviour` (a component that can replicate over the
  network) that holds the real clock and world references and calls into the pure core. Examples:
  `PlayerStats`, `PlayerInventory`, `PlayerAbilities`, `PlayerGathering`, `ResourceNode`,
  `CraftingStation`.

Time is the tell. A pure-core method takes a `double now` parameter. The shell passes
`Time.timeAsDouble`. This is why the entire rules half can be unit-tested in milliseconds with no
server running (see [10-testing](10-testing.md)).

```
   client presses "cast"                     server
   ──────────────────────                    ──────
   PlayerAbilities.RequestCast ── ServerRpc ─► RpcRequestCast   (re-check sender owns it)
                                                   │
                                                   ▼
                                        AbilityCaster.TryCast  (pure core)
                                                   │  calls
                                                   ▼
                                        AbilityCastRules.Validate  (pure core)
                                                   │
                                          success? ─┴─► spend mana, start cooldowns,
                                                         apply effects to players
```

## 5. How one action travels through the system

Take "the player presses a button to cast a fireball". This is the general shape of *every* action:

```
┌──────────────────────────── CLIENT ────────────────────────────┐
│ UI button → PlayerAbilities.RequestCast(slot, targetPosition)   │
│   • guards: only the owning client may request                  │
│   • sends a FishNet ServerRpc to the server                     │
└───────────────────────────────┬─────────────────────────────────┘
                                │  ServerRpc (network)
┌───────────────────────────────▼─────────────────────────────────┐
│ SERVER (PlayerAbilities.RpcRequestCast)                          │
│   1. reject if the sender is not the object's owner              │
│   2. read the live mana from PlayerStats                         │
│   3. AbilityCaster.TryCast → AbilityCastRules.Validate           │
│   4. on success: spend mana, book cooldowns, queue the effect    │
│   5. later, when the cast time elapses: apply damage/heal        │
│      (writes SyncVars, replicated to all clients)                │
└───────────────────────────────┬─────────────────────────────────┘
                                │  SyncVar replication
┌───────────────────────────────▼─────────────────────────────────┐
│ ALL CLIENTS render the new health/mana values (HUD repaints)     │
└─────────────────────────────────────────────────────────────────┘
```

The same shape applies to equipping an item, gathering ore, crafting, joining a party or moving
between zones. Once you have this shape in your head, the rest is detail.

## 6. State replication in one paragraph

FishNet replicates two kinds of things in this project:

- **SyncVars** — single values owned by the server and copied to clients. In this project they are
  always declared in the object form, e.g.
  `private readonly SyncVar<int> _health = new();`
  (the older `[SyncVar]` *attribute* is obsolete in FishNet 4.x and does nothing). The current value
  is read through `.Value`. When the value changes, an `OnChange` callback fires on both server and
  clients, and the shell raises a plain C# event (`VitalsChanged`, `SlotsChanged`, ...) for the UI.
- **SyncLists** — ordered collections, e.g. `SyncList<ItemInstance>` for the inventory bag and the
  equipment slots. The server adds/removes/replaces entries and FishNet mirrors the operations.

Clients never write SyncVars or SyncLists. Only the server does, through `[Server]` methods.

## 7. The backend in one paragraph

All player saves and all social state live in a central **Nakama** server backed by **PostgreSQL**.
Any zone server can read the same player because every server process authenticates as the *same*
shared game-server account and reads under that account. The game code never calls Nakama directly:
it talks to a small interface called `IPlayerRepository`, whose production implementation
(`NakamaPlayerRepository`) is the only code that knows about Nakama storage. That boundary is what
lets tests run with no database at all. Chapter [03](03-backend-and-persistence.md) covers this in
depth.

## 8. Component diagram

This is the same picture the introduction drew, at the level of named classes.

```
┌──────────────────────────── CLIENT ────────────────────────────┐
│ Main Menu scene                                                 │
│   MainMenuBootstrap ─► LoginConnector ─► NakamaApiGateway       │
│         │  authenticates, learns last_zone, connects            │
│         ▼                                                       │
│   JoinRequestClient  ──(name, accountId, authToken)──►  (TCP)   │
│   HUD (NetworkHud) renders replicated SyncVars/SyncLists        │
└───────────────────────────────┬─────────────────────────────────┘
                                │ Tugboat transport
┌───────────────────────────────▼─────────────────────────────────┐
│ ZONE SERVER (one Unity process per zone)                        │
│   MMOConnectionGate (FishNet Authenticator)                     │
│     • parks the connection in JoinHandshakeTracker              │
│     • waits for the join request broadcast                      │
│     • optionally validates the session token                    │
│     • optionally loads the save record                          │
│     • admits or rejects                                         │
│   NetworkManagerFactory builds this whole object graph          │
│   PlayerSpawner spawns the Player prefab on admission           │
│   PlayerStats / PlayerInventory / PlayerAbilities own the truth  │
│   PlayerPersistenceService saves/loads through IPlayerRepository │
│   SocialHubService owns party/guild state                        │
└───────────────────────────────┬─────────────────────────────────┘
                                │ HTTP (REST /v2/...)
┌───────────────────────────────▼─────────────────────────────────┐
│ BACKEND: Nakama 3.41  ─►  PostgreSQL 18                          │
│   storage collection "players"        (owned by the game server) │
│   storage collection "player_metadata" (owned by each player)    │
│   collections parties / guilds / social_index / social_presence  │
└─────────────────────────────────────────────────────────────────┘
```

## 9. Glossary

| Term | Meaning |
|---|---|
| **Zone** | One server process running one scene, with its own `NetworkManager`. Several zones share the backend. |
| **NetworkManager** | The FishNet root object. It owns the transport, the authenticator and the spawnable prefab list. |
| **Authenticator / gate** | A FishNet component that decides whether an incoming connection is admitted. Here it is `MMOConnectionGate`. |
| **SyncVar** | A single value the server replicates to all clients. Object form in this project. |
| **SyncList** | A replicated list the server mutates. |
| **ServerRpc** | A method a client calls that runs on the server (with a sender argument to check ownership). |
| **TargetRpc** | A method the server calls that runs on one specific client. |
| **Broadcast** | A network message registered by name; used for the join handshake and zone-transfer redirect. |
| **ScriptableObject** | A Unity asset that holds data (not behaviour) and can be edited in the Inspector. Used for abilities, weapons, armor, items and recipes. |
| **Baker** | An editor menu command under `Tools > MMO` that builds a prefab or scene object for you. |
| **CAS** | "Compare And Swap": read a record with a version, write it back expecting the same version. Used for social state. |
| **InstanceId** | The server-minted unique id of one item instance. What the ownership registry tracks, so the same physical item cannot be held twice. |
| **Ownership registry** | `ItemOwnershipRegistry`: the in-process, single-writer authority for who holds which item instance. |
| **Line of sight** | Whether a server raycast reaches a target without a wall first. Checked before an attack or ability effect lands. |

## Common tasks

- **Find where a feature lives** — match the folder names above to the feature. Networking wiring is
  always in `Assets/Scripts/Net`; data is always in `Assets/Data` or `Assets/Resources`.
- **Understand why a client action is allowed or refused** — find the `[ServerRpc]` handler on the
  relevant `NetworkBehaviour`, then follow it into the pure-core validator it calls.
- **Add a brand-new server-authoritative feature** — write the pure rules first (with tests), then a
  `NetworkBehaviour` shell that calls them from `[Server]`/`[ServerRpc]` methods. Chapter
  [11](11-extending-the-game.md) walks through examples.
- **Check whether a change is allowed** — if it lets a client set authoritative state instead of
  asking the server, it is not allowed. See [12](12-conventions-and-checklist.md).
