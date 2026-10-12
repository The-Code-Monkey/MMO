# 02 — Getting started

This chapter takes you from a fresh clone to "I can run the game and see the main menu".

## 1. What you need installed

| Tool | Version / notes |
|---|---|
| Unity Editor | **6000.6.5f1** (see `ProjectSettings/ProjectVersion.txt`). Install it through Unity Hub; any 6000.6.x should open the project, but use exactly this one if you can. |
| Docker + Docker Compose | For the backend (Nakama + PostgreSQL). Only needed if you want persistence and real logins; tests and local demos run without it. |
| Git | Normal repository access. |
| A terminal | The commands below assume a Unix-style shell. |

You do **not** need to install FishNet separately — it comes from a Git URL declared in
`Packages/manifest.json` and Unity downloads it on first open.

## 2. The packages the project uses

From `Packages/manifest.json`:

| Package | Version | Why it is here |
|---|---|---|
| `com.firstgeargames.fishnet` | `4.7.3` (Git) | The networking library (SyncVars, RPCs, transports, spawners). |
| `com.unity.render-pipelines.universal` | `17.6.0` | URP, the render pipeline the scenes use. |
| `com.unity.cinemachine` | `6.6.0` | The follow camera (`LocalCameraRig`). |
| `com.unity.inputsystem` | `1.20.0` | New input system (the UI event system prefers it). |
| `com.unity.ugui` | `2.6.0` | The UI toolkit used by the code-built screens. |
| `com.unity.test-framework` | `1.8.0` | Unity Test Framework + NUnit, which run the test suites. |

`Packages/manifest.json` also lists `com.firstgeargames.fishnet` under `testables`, which means
FishNet's own tests are compiled alongside the project's when you run tests.

## 3. Get the project open

1. Clone the repository.
2. Open the folder in Unity Hub with editor **6000.6.5f1**.
3. Let Unity import packages and compile. The first import downloads FishNet from GitHub, so it can
   take a few minutes.
4. The project comes with a **Main Menu** scene at `Assets/Scenes/MainMenu.unity`. Open it (or let
   Unity open it) to see the login screen when you press Play.

If the project does not compile on first open, check the console. Most "missing reference" errors on
a fresh clone mean the packages have not finished resolving yet.

## 4. Boot the backend (optional but recommended)

Player saves live in Nakama on PostgreSQL. Start the whole stack with one command from the
repository root:

```sh
docker compose up -d
```

This starts (see `docker-compose.yml`):

| Service | Address | Used for |
|---|---|---|
| Nakama HTTP API | `127.0.0.1:7350` | Everything the game server and main menu call. |
| Nakama gRPC | `127.0.0.1:7349` | Reserved. |
| Nakama console | `127.0.0.1:7351` | A web UI for inspecting players and storage while developing. |
| PostgreSQL | internal `db:5432` | The actual saves (`nakama` database, user `postgres`, password `password`). |

The database is **not** published to your host machine by the compose file; it is only reachable
from inside Docker as `db:5432`. (The README's table lists `127.0.0.1:5432`, which is a small
discrepancy — the compose file does not map that port.)

The Postgres data lives in a named Docker volume called `nakama-pgdata`, so `docker compose down`
keeps your player saves. If you want a completely clean slate, `docker compose down -v` also removes
the volume.

The game's default connection settings already point at these addresses, so once the stack is up a
zone server connects with no configuration.

## 5. Run the game

### Option A — Main Menu scene (the real flow)

1. Open `Assets/Scenes/MainMenu.unity`.
2. Press Play.
3. Type a username and password and press Sign In. A first-time username is created automatically.
4. The client authenticates against Nakama, finds your last zone (or the default one), connects,
   and — if you have the HUD in the scene — the player spawns.

### Option B — no backend

You can run without Docker. The persistence layer is designed to **fail open**: joins still spawn a
player (fresh, with default stats), and saves are queued but dropped. Nothing crashes and no save is
ever overwritten. This is how tests run.

### Option C — the Test Runner

You do not need Docker for tests. Open **Window > General > Test Runner**, then click **Run All**.
See [10-testing](10-testing.md) for the full story.

## 6. Bake the scenes you need

The repository holds the source code, but the scene objects and prefabs are produced by editor menu
commands under `Tools > MMO`. If a scene is missing its `NetworkManager` or a prefab is missing, run
the matching baker. The full list lives in [09-editor-tooling-and-scenes](09-editor-tooling-and-scenes.md),
but the common ones are:

- `Tools > MMO > Configure URP` — set up the render pipeline (once).
- `Tools > MMO > Create Player Prefab` — build the networked player prefab.
- `Tools > MMO > Create Target Dummy Prefab` — build the training-dummy prefab.
- `Tools > MMO > Create NetworkManager In Scene` — drop a fully wired `NetworkManager` into the
  current scene.
- `Tools > MMO > Create Main Menu Scene` — rebuild `Assets/Scenes/MainMenu.unity`.

## 7. Build a standalone player (optional)

A build runs the same code. The one catch is the **build guard**: `NetworkManagerBuildGuard` checks
each enabled scene in Build Settings and refuses the build if any enabled scene has no
`NetworkManager`. This stops you shipping a scene that clients cannot join. The check is described
in [09-editor-tooling-and-scenes](09-editor-tooling-and-scenes.md).

## 8. Project layout cheat-sheet

```
Assets/Scripts/Gameplay      server-owned stats, combat, abilities, gathering
Assets/Scripts/Inventory     grid bag + equipment, item definitions
Assets/Scripts/Crafting      recipes, stations, crafting rules
Assets/Scripts/Net           FishNet wiring: gate, prefab factories, spawners, zone transfer
Assets/Scripts/Persistence   save records, Nakama repository/REST, login rules, token auth
Assets/Scripts/Social        parties, guilds, social hub
Assets/Scripts/UI            code-built Main Menu and HUD
Assets/Scripts/Bootstrap     Main Menu boot script + login connector
Assets/Scripts/Client        local camera rig
Assets/Editor                Tools > MMO bakers, URP setup, build guard
Assets/Tests/EditMode        fast unit tests
Assets/Tests/PlayMode        socket + game-loop tests
Assets/Data                  ability/weapon/armor ScriptableObjects
Assets/Resources/Items       items that load by name at runtime
```

## 9. How to run the tests from the command line

CI runs them with `game-ci/unity-test-runner` (see `.github/workflows/unity-tests.yml`). Locally,
the simplest path is the Test Runner window. If you want a headless run, use Unity's batch mode with
`-runTests -testPlatform EditMode` (and then `PlayMode`), but the window is the fastest feedback loop
while developing.

## Common tasks

- **"I pressed Play and nothing happens."** Make sure the scene has a `NetworkManager` (run
  `Tools > MMO > Create NetworkManager In Scene`), and for the Main Menu that the scene is
  `Assets/Scenes/MainMenu.unity` with a `MainMenuBootstrap` component.
- **"The login says it cannot reach the backend."** Run `docker compose up -d` and wait for Nakama
  to finish migrating (the first run creates the schema). Check the console at
  `http://127.0.0.1:7351`.
- **"I want to reset my player."** Delete the record from the Nakama console, or run
  `docker compose down -v` to wipe everything.
- **"A Tools > MMO baker left a mess."** The bakers are idempotent — running one again reuses or
  overwrites its output rather than duplicating it. See chapter [09](09-editor-tooling-and-scenes.md).
