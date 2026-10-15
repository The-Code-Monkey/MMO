# 09 — Editor tooling and scenes

The repository stores code, not scene objects. The scene objects and prefabs are produced by editor
menu commands under `Tools > MMO`. This chapter lists every command, what it writes, and the build
guard that stops you shipping a broken scene.

## 1. Why "bakers"?

A **baker** is an editor command that builds a prefab or a scene object from code. The reason to
generate rather than hand-author is the same as for the UI (chapter [08](08-ui-main-menu-and-hud.md)):
the code factories (`NetworkManagerFactory`, `PlayerPrefabFactory`, ...) are the single source of
truth, so a baked scene and a test-created object are guaranteed to match.

All bakers live in `Assets/Editor` in namespace `MMO.Editor`. They are **idempotent**: running one
again reuses or overwrites its output instead of duplicating it.

## 2. The menu commands

| Menu item | Class | What it does |
|---|---|---|
| `Tools > MMO > Configure URP` | `UrpSetupBaker` | Creates/assigns the URP pipeline and renderer assets. |
| `Tools > MMO > Create Player Prefab` | `PlayerPrefabBaker` | Builds and saves the networked player prefab. |
| `Tools > MMO > Create Player Prefab And Wire Scene` | `PlayerPrefabBaker` | The above, then wires a `NetworkManager` into the current scene. |
| `Tools > MMO > Create Target Dummy Prefab` | `TargetDummyBaker` | Builds the training-dummy prefab and wires its spawner. |
| `Tools > MMO > Create Crafting Station Prefab` | `CraftingStationBaker` | Builds the crafting-station prefab and wires its spawner. |
| `Tools > MMO > Create Resource Node Prefab` | `ResourceNodeBaker` | Builds the resource-node prefab (placed into levels by hand). |
| `Tools > MMO > Create Loot Bag Prefab` | `LootBagBaker` | Builds the loot-bag prefab a black-zone death drops. |
| `Tools > MMO > Create Local Camera` | `LocalCameraBaker` | Adds the Cinemachine-based local camera rig to the scene. |
| `Tools > MMO > Create NetworkManager In Scene` | `NetworkManagerSceneBaker` | Drops a fully wired `NetworkManager` into the current scene. |
| `Tools > MMO > Create Main Menu Scene` | `MainMenuSceneBaker` | Rebuilds `Assets/Scenes/MainMenu.unity` with a token-auth manager and the bootstrap. |
| `Tools > MMO > Build Zones` | `ZoneSceneBaker` | Rebuilds `Assets/Scenes/SafeZone.unity` and `Assets/Scenes/Wilderness.unity` from the graybox prefab kit and registers both in Build Settings. |

## 3. The prefab bakers

Each prefab baker follows the same shape: build the object via the matching **runtime factory**,
save it with `PrefabUtility.SaveAsPrefabAsset`, and destroy the in-scene copy afterwards. The
runtime factories live in `Assets/Scripts/Net` (see [04](04-networking-and-the-join-flow.md)).

### PlayerPrefabBaker

`Assets/Editor/PlayerPrefabBaker.cs:22`. Saves to `Assets/Prefabs/Player.prefab`
(`PrefabAssetPath`). `EnsurePlayerPrefab` (`:61`) builds via `PlayerPrefabFactory.Create()`, then
`EquipDefaultWeapon` (`:170`) equips `Assets/Data/Weapons/Broadsword.asset` by writing the private
`_equippedWeapon` field through a `SerializedObject`. `WirePlayerPrefab` (`:107`) registers the
prefab in the spawnable collection and attaches a `PlayerSpawner`.

### TargetDummyBaker

`Assets/Editor/TargetDummyBaker.cs:20`. Builds via `TargetDummyPrefabFactory.Create()`.
`WireTargetDummy` (`:97`) registers the prefab and attaches `TargetDummySpawner`, which spawns it
server-side on startup.

### CraftingStationBaker

`Assets/Editor/CraftingStationBaker.cs:11`. Builds via `CraftingStationPrefabFactory.Create()`.
`WireCraftingStation` (`:67`) registers the prefab and attaches `CraftingStationSpawner`.

### ResourceNodeBaker

`Assets/Editor/ResourceNodeBaker.cs:19`. Builds via `ResourceNodePrefabFactory.Create()`. It has no
`Wire...` method — resource nodes are placed into levels by hand and registered elsewhere.

### LootBagBaker

`Assets/Editor/LootBagBaker.cs:19`. Builds via `LootBagPrefabFactory.Create()` and saves to
`Assets/Prefabs/LootBag.prefab`. `EnsureLootBagPrefab` (`:52`) is idempotent (reuses the saved asset),
`WireLootBag` (`:94`) registers the prefab in the `NetworkManager`'s spawnable collection so a
black-zone death can resolve it, and `DeleteLootBagPrefab` (`:119`) is the cleanup path. The same
registration is what `PlayerDeath` scans for, so the bag needs no per-prefab reference.

## 4. The scene bakers

### NetworkManagerSceneBaker

`Assets/Editor/NetworkManagerSceneBaker.cs:18`. `CreateInActiveScene(settings)` (`:47`) reuses an
existing named `NetworkManager` if there is one (wiring the player, dummy and loot-bag prefabs),
otherwise bakes the player, dummy and loot-bag prefabs and calls
`NetworkManagerFactory.Create(settings, out _, ...)` to build the whole object graph. The loot-bag
prefab is produced by `LootBagBaker` (see below). `TryFind` (`:85`) looks for a root object named
`NetworkManagerFactory.GameObjectName`. `Validate` (`:105`) returns human-readable errors used by the
build guard.

### MainMenuSceneBaker

`Assets/Editor/MainMenuSceneBaker.cs:25`. `CreateMainMenuScene` (`:31`) creates a new scene, builds a
`NetworkManager` with **token auth on**, adds a `MainMenuBootstrap` object, saves the scene to
`Assets/Scenes/MainMenu.unity`, and adds it to Build Settings. Note the UI is **not** baked — the
bootstrap builds it at runtime.

### LocalCameraBaker

`Assets/Editor/LocalCameraBaker.cs:19`. `EnsureInActiveScene` (`:38`) reuses an existing camera rig or
creates one via `LocalCameraRigFactory.Create()`, registers an undo step, and marks the scene dirty.
`Validate` (`:99`) checks the Cinemachine parts are present.

### ZoneSceneBaker

`Assets/Editor/ZoneSceneBaker.cs`. `BuildBoth` (`Tools > MMO > Build Zones`) assembles the two
always-on zones — a walkable `SafeZone` town and a hostile `Wilderness` — each as a **self-contained
zone server**. `BuildSafeZone`/`BuildWilderness` build one scene each; `ZoneSceneBakerTests` covers
both. Every scene gets a baked `NetworkManager` (via `NetworkManagerSceneBaker.CreateInActiveScene`,
token auth on, its own port), an authored camera rig, a `ZoneRiskConfig` (`Safe` / `Red`, plus red
hazard volumes in the wilderness), four spawn points wired into the `PlayerSpawner`, and one
`ZoneTransferTrigger` road to the other zone (`server-a:7770` ⇄ `server-b:7771`). The zone's
`SocialHubService.ZoneId` and raid-registry host/port/template are set so the two servers are
addressable and distinct, and both scenes are added to Build Settings so the `NetworkManager` build
guard checks them.

Geometry is assembled with `PrefabUtility.InstantiatePrefab` over the primitive **graybox prefab kit**
in `Assets/Prefabs/Graybox` — plain built-in-mesh prefabs, deliberately **not** ProBuilder. The baker
is idempotent: a second run regenerates the same scene (the wilderness scatter uses a fixed random
seed) rather than duplicating geometry or managers.

### UrpSetupBaker

`Assets/Editor/UrpSetupBaker.cs:22`. `EnsureUrp` (`:52`) loads or creates the
`UniversalRenderPipelineAsset` and its renderer, saves them under `Assets/Settings`, and assigns the
pipeline to `GraphicsSettings.defaultRenderPipeline` and every quality level.

## 5. The build guard

A client cannot join a scene without a `NetworkManager`, so the project refuses to build if any
enabled scene is missing one. This is split into a pure rule and an editor hook:

- `NetworkManagerBuildRules.FindViolations(IEnumerable<SceneBuildInfo>)`
  (`Assets/Editor/NetworkManagerBuildRules.cs:22`) is pure LINQ: it returns the paths of enabled
  scenes with no `NetworkManager`. It is unit tested (see `NetworkManagerBuildRulesTests`).
- `NetworkManagerBuildGuard` (`Assets/Editor/NetworkManagerBuildGuard.cs:15`) is an
  `IPreprocessBuildWithReport`. On preprocess (`:19`) it collects scene info and throws a
  `BuildFailedException` listing the violations, aborting the build.

`CollectSceneInfo` (`NetworkManagerBuildGuard.cs:29`) opens each enabled build scene additively,
checks it with `NetworkManagerSceneBaker.TryFind`, and closes it.

## 6. How a fresh zone scene comes together

```
Tools > MMO > Configure URP
Tools > MMO > Create Player Prefab
Tools > MMO > Create Target Dummy Prefab
Tools > MMO > Create NetworkManager In Scene   ← builds/wires the NetworkManager
(place resource nodes, crafting stations, a ZoneTransferTrigger, terrain, lighting)
save the scene and add it to Build Settings
```

Or, for the main menu specifically:

```
Tools > MMO > Create Main Menu Scene
```

Or, for the always-on zone pair (town + wilderness) at once:

```
Tools > MMO > Build Zones
```

`Build Zones` is the one-shot path from the "fresh zone scene" recipe above, repeated for both zones
and wired to each other: it bakes the `NetworkManager`, assembles the ground/boundary/buildings (or
scatter + hazards) from the graybox prefab kit, drops the spawn points and the `ZoneRiskConfig`, adds
the cross-zone `ZoneTransferTrigger`, saves both scenes and registers them in Build Settings.

## Common tasks

- **Scene is missing its NetworkManager.** Run `Tools > MMO > Create NetworkManager In Scene`.
- **Player prefab is missing or out of date.** Run `Tools > MMO > Create Player Prefab`. If you
  changed the player component list, this picks it up because it calls `PlayerPrefabFactory.Create`.
- **Build refuses to start.** Read the `BuildFailedException`: it names the enabled scene(s) without
  a `NetworkManager`. Either add one (bake it) or disable the scene in Build Settings.
- **Add a new baker.** Add a static class in `Assets/Editor` with a `[MenuItem("Tools/MMO/...")]`
  method that calls a runtime factory, saves the prefab with `PrefabUtility.SaveAsPrefabAsset`, and
  destroys the scene copy in a `finally`. Add an EditMode test that cleans up after itself (see the
  `*TestAsset` helpers in [10](10-testing.md)).
- **Change the default player weapon.** Edit `PlayerPrefabBaker.DefaultWeaponAssetPath` (`:36`) and
  re-run the player prefab baker.
