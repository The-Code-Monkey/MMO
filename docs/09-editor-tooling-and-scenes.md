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
| `Tools > MMO > Bake All (Prefabs + Scenes)` | `BakeAll` | Runs every prefab baker, rebuilds the main menu and all four zone scenes, then does one FishNet default-prefab refresh. |
| `Tools > MMO > Configure URP` | `UrpSetupBaker` | Creates/assigns the URP pipeline and renderer assets. |
| `Tools > MMO > Rebuild Ground Grass Assets` | `GroundAssetBaker` | Imports the grass maps and builds the `Ground_Grass` material and 2 m prefab. |
| `Tools > MMO > Rebuild Ground Cobblestone Assets` | `GroundAssetBaker` | Imports the cobblestone maps and builds the `Ground_Cobblestone` material and 2 m prefab. |
| `Tools > MMO > Rebuild Ground Worn Cobblestone Assets` | `GroundAssetBaker` | Imports the worn maps and builds the `Path_Cobblestone_Worn` overlay prefab. |
| `Tools > MMO > Rebuild Building Assets` | `BuildingAssetBaker` | Imports the generic wall and roof-tile maps and builds their materials. |
| `Tools > MMO > Rebuild Player Character Assets` | `PlayerCharacterAssetBaker` | Imports the rigged character FBX, builds its material, AnimatorController and visual prefab. |
| `Tools > MMO > Rebuild Donkey Mount Assets` | `DonkeyMountAssetBaker` | Imports the static donkey FBX, builds its URP material, mask and mount prefab, then points `Resources/Mounts/Donkey.asset` at it. |
| `Tools > MMO > Create Player Prefab` | `PlayerPrefabBaker` | Builds and saves the networked player prefab. |
| `Tools > MMO > Create Player Prefab And Wire Scene` | `PlayerPrefabBaker` | The above, then wires a `NetworkManager` into the current scene. |
| `Tools > MMO > Create Target Dummy Prefab` | `TargetDummyBaker` | Builds the training-dummy prefab and wires its spawner. |
| `Tools > MMO > Create Crafting Station Prefab` | `CraftingStationBaker` | Builds the crafting-station prefab and wires its spawner. |
| `Tools > MMO > Rebuild Crafting Station Assets` | `CraftingStationAssetBaker` | Imports the anvil/furnace maps, builds their materials, and refreshes the station prefab's models. |
| `Tools > MMO > Create Resource Node Prefab` | `ResourceNodeBaker` | Builds the resource-node prefab a zone's markers spawn. |
| `Tools > MMO > Rebuild Resource Node Assets` | `ResourceNodeAssetBaker` | Imports the iron, copper, tin, cobalt and veridium maps, builds their materials, and mounts the full/depleted models on every node prefab. |
| `Tools > MMO > Rebuild Item Icons` | `ItemIconBaker` | Imports `Assets/Textures/Items` as UI sprites and wires each `ItemDefinition` to the icon matching its item name. |
| `Tools > MMO > Create Loot Bag Prefab` | `LootBagBaker` | Builds the loot-bag prefab a black-zone death drops. |
| `Tools > MMO > Create Camera Rig` | `LocalCameraBaker` | Drops the scene's `ThirdPersonCameraRig` camera rig into the active scene. |
| `Tools > MMO > Create NetworkManager In Scene` | `NetworkManagerSceneBaker` | Drops a fully wired `NetworkManager` into the current scene. |
| `Tools > MMO > Create Main Menu Scene` | `MainMenuSceneBaker` | Rebuilds `Assets/Scenes/MainMenu.unity` with a token-auth manager and the bootstrap. |
| `Tools > MMO > Wire Zone With World Bootstrap` | `WorldBootstrapBaker` | Connects an open scene to the server: adds `WorldBootstrap`, tags spawn points, registers marker prefabs. |
| `Tools > MMO > Build Zones` | `ZoneSceneBaker` | Rebuilds the `Tutorial`, `SafeZone`, `Wilderness` and `TestZone` scenes from the graybox prefab kit and registers all four in Build Settings. |
| `Tools > MMO > Build Safe Zone` | `ZoneSceneBaker` | Rebuilds only `Assets/Scenes/SafeZone.unity`. |
| `Tools > MMO > Build Wilderness Zone` | `ZoneSceneBaker` | Rebuilds only `Assets/Scenes/Wilderness.unity`. |
| `Tools > MMO > Build Tutorial Zone` | `ZoneSceneBaker` | Rebuilds only `Assets/Scenes/Tutorial.unity`. |
| `Tools > MMO > Build Test Zone` | `ZoneSceneBaker` | Rebuilds only `Assets/Scenes/TestZone.unity`, the ore testing ground. |

## 3. The prefab bakers

Each prefab baker follows the same shape: build the object via the matching **runtime factory**,
save it with `PrefabUtility.SaveAsPrefabAsset`, and destroy the in-scene copy afterwards. The
runtime factories live in `Assets/Scripts/Net` (see [04](04-networking-and-the-join-flow.md)).

A `NetworkObject` built by a factory carries `AssetPathHash = 0`. Saving it that way makes FishNet
log a hash collision against every other prefab still at zero, because its incremental
`AssetPostprocessor` does not rehash a prefab that was overwritten in place. Bakers that rebuild a
prefab which already exists therefore copy the old hash onto the new object before saving
(`ResourceNodeAssetBaker.PreserveAssetPathHash`). `BakeAll` finishes with one
`FishNetPrefabCollectionRefresher.Refresh()` so the default prefab collection is regenerated from
the final assets, in sorted order, and genuinely new prefabs get their hash.

### PlayerPrefabBaker

`Assets/Editor/PlayerPrefabBaker.cs:24`. Saves to `Assets/Prefabs/Player.prefab`
(`PrefabAssetPath`). `EnsurePlayerPrefab` (`:66`) rebuilds via `PlayerPrefabFactory.Create()`
whenever the asset is missing **or has drifted from the factory** (its NetworkBehaviour set no
longer matches, e.g. a component was added to `Create` since the last bake) — the asset is what
every client spawns from, so a stale one is a real bug, and overwriting in place keeps its GUID so
scene references stay valid. `EquipDefaultWeapon` (`:231`) equips
`Assets/Data/Weapons/Broadsword.asset` by writing the private `_equippedWeapon` field through a
`SerializedObject`, and `MakeNetworkTransformServerAuthoritative` clears the private
`_clientAuthoritative` flag the same way. `WirePlayerPrefab` (`:168`) registers the
prefab in the spawnable collection and attaches a `PlayerSpawner`.

The baked prefab also nests the cosmetic character under `Visuals/`. The networked root keeps its
capsule collider and mesh; only its `MeshRenderer` is disabled. `PlayerCharacterAssetBaker` imports
`Assets/Models/Player/Base.fbx` as a Humanoid rig, extracts the `Idle`, `Walk`, `Run` and `Dying`
clips, builds `Assets/Animations/Player/Player.controller` and saves
`Assets/Prefabs/PlayerVisual.prefab`. The visual is a plain `MonoBehaviour`
(`MMO.Client.PlayerCharacterVisual`) driven by the replicated root movement, so it adds no
NetworkBehaviour index and never becomes authoritative state.

### TargetDummyBaker

`Assets/Editor/TargetDummyBaker.cs:20`. Builds via `TargetDummyPrefabFactory.Create()`.
`WireTargetDummy` (`:97`) registers the prefab and attaches `TargetDummySpawner`, which spawns it
server-side on startup.

### CraftingStationBaker

`Assets/Editor/CraftingStationBaker.cs:11`. Builds via `CraftingStationPrefabFactory.Create()`.
`WireCraftingStation` (`:67`) registers the prefab and attaches `CraftingStationSpawner`.

The prefab is one networked station with two visual anchors under `Visuals/`: the imported
`Assets/Models/Crafting/Forge/Forge.fbx` under `Furnace`, and a procedural `Anvil` on a wood stump.
`CraftingStation.ApplyVisuals` shows the variant matching the replicated
`StationType`; `None` and `Alchemy` fall back to the furnace until an alchemy model exists.
`CraftingStationAssetBaker` (`Tools > MMO > Rebuild Crafting Station Assets`) imports
`Assets/Textures/Crafting/{Anvil,Furnace}_{Albedo,Normal,Mask}.png` and the forge's
`Forge_{Diffuse,Normal,Mask,Emissive}.png`, builds
`Assets/Materials/Crafting/{Anvil_Metal,Furnace_Stone,Wood_Stump,Forge}.mat`, drops the forge FBX
under the `Furnace` anchor (rotated to face the player, scaled to the footprint), and refreshes the
prefab in place so its GUID and scene references survive.

### GroundAssetBaker

`Assets/Editor/GroundAssetBaker.cs`. `RebuildGrass`, `RebuildCobblestone` and
`RebuildCobblestoneWorn` (the matching `Tools > MMO > Rebuild Ground ... Assets` commands) set the
import settings on the surface's `{Albedo,Normal,Mask}.png` maps, then create or update its material
and prefab. The 2 m prefab
matches the graybox floor footprint. It assigns the packed mask as both the metallic and occlusion
map, with smoothness read from the metallic map's alpha. Re-running a command is idempotent and does
not touch scenes.

The source maps are generated by `tools/generate_ground_textures.py`:

```
python3 tools/generate_ground_textures.py --kind grass
python3 tools/generate_ground_textures.py --kind cobblestone
python3 tools/generate_ground_textures.py --kind cobblestone-worn
python3 tools/generate_ground_textures.py --kind wall --out Assets/Textures/Building
python3 tools/generate_ground_textures.py --kind roof --out Assets/Textures/Building
python3 tools/generate_ground_textures.py --kind anvil
python3 tools/generate_ground_textures.py --kind furnace
```

The script is deterministic for a given seed, so regenerating does not churn the PNGs.

### BuildingAssetBaker

`Assets/Editor/BuildingAssetBaker.cs`. `RebuildBuildingAssets` (`Tools > MMO > Rebuild Building
Assets`) imports `Assets/Textures/Building/Wall_Generic_{Albedo,Normal,Mask}.png` and
`RoofTiles_{Albedo,Normal,Mask}.png`, then creates or updates
`Assets/Materials/Building/Wall_Generic.mat` and `Roof_Tiles.mat`. `GrayboxZoneGeometry.BuildBuilding`
applies them to the town's wall and roof prefab instances. Boundary walls and wilderness scatter keep
the shared graybox material.

### ResourceNodeBaker

`Assets/Editor/ResourceNodeBaker.cs:19`. Builds via `ResourceNodePrefabFactory.Create()`. It has no
`Wire...` method of its own — resource nodes are authored as `ResourceNodeMarker`s in a zone and the
prefab is registered into the scene by `WorldBootstrapBaker.RegisterMarkerPrefabs` (see below).

`ResourceNodeAssetBaker` (`Tools > MMO > Rebuild Resource Node Assets`) imports
`Assets/Models/Resources/IronOre/IronOre.fbx` (full),
`Assets/Models/Resources/StoneOre/StoneOreEmpty.fbx` (depleted),
`Assets/Models/Resources/IronOre/IronOreLarge.fbx` (large) and
`Assets/Models/Resources/StoneOre/StoneOreLargeEmpty.fbx` (large depleted), plus their texture sets
under `Assets/Textures/Resources/`, and builds
`IronOre.mat` / `StoneOreEmpty.mat` / `IronOreLarge.mat` / `StoneOreLargeEmpty.mat`. It refreshes `ResourceNode.prefab` in
place: the root keeps the unit click collider and NavMesh carve, and both models hang under
`Visuals/` scaled to 1.6 m and floor-aligned. It also writes `ResourceNode_Large.prefab`, which
carries `Iron_Ore_Large` (15 ore, same reset/interval/range), its own full and empty models scaled
to 2.4 m, and a 2.8 x 2.4 x 2.2 m body/carve.
`ResourceNode.ApplyDepletedState` swaps `IronOreMesh` for `StoneOreEmptyMesh`, drops the collider and
carve so the empty rock cannot be gathered, and restores the full model on respawn.

To place a large vein, point a `ResourceNodeMarker`'s `NodePrefab` at
`Assets/Prefabs/ResourceNode_Large.prefab`; `MarkerPrefabRegistrar` adds it to the scene's
spawnable collection. The Main Menu's NetworkManager also carries the large prefab, because that
manager survives into the zone as the client's manager and must be able to resolve every prefab the
server spawns.

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
bootstrap builds it at runtime. The token-auth manager's `ZoneServer` is pointed at the **Tutorial**
zone (`ZoneSceneBaker.TutorialZonePort`), which is the fallback a login with no stored "last zone"
lands in — see [04](04-networking-and-the-join-flow.md).

### LocalCameraBaker

`Assets/Editor/LocalCameraBaker.cs:28`. `EnsureInActiveScene` (`:51`) reuses an existing rig or
instantiates `Assets/Prefabs/Camera/CameraRig.prefab` (the single `ThirdPersonCameraRig` plus
its child camera, tagged `MainCamera`), registers an undo step, and marks the scene dirty. The
`CameraRig` is how a client sees the world: it is authored in the zone scenes by `ZoneSceneBaker`
and deliberately **not** on the player prefab or in the main menu (the controller is a
`DontDestroyOnLoad` singleton and is bound to the local owner by `LocalPlayerCameraTarget`, so a
second copy would race the singleton).
`Validate` (`:116`) checks the rig exists, still has a child `Camera`, and that a camera is tagged
`MainCamera` (the camera world-space raycasts are cast through).

The rig is the third-person camera: a perspective eye on an orbit arm whose pivot sits at the
character's neck (`CameraOrbitMath.NeckHeightAboveFeet`, measured from the feet, not the capsule's
centre). Dragging either mouse button orbits — yaw around up, pitch between `-35°` and `+70°` — and
never turns the character; the heading is the mover's business. The pointer is captured only while a
button is held over the world, and released on release, on Esc, or when a modal takes the keyboard.
The arm is a fixed length until the zoom ticket wires the wheel to it.

### ZoneSceneBaker

`Assets/Editor/ZoneSceneBaker.cs`. `BuildBoth` (`Tools > MMO > Build Zones`) assembles the four
always-on zones — the flat 40 m `Tutorial` training ground, the walkable 160 m `SafeZone` town,
the hostile 160 m `Wilderness` and the 80 m `TestZone` ore ground — each as a **self-contained
zone server**. Town buildings, hazards
and markers stay clustered near the origin; the extra ground is open space.

The safe zone town is a 4×4 block grid on a 16 m pitch. Each block is a 6–8 m kit building, the four
central blocks are left open as the town square, and the roads are 4 m wide worn-cobblestone overlays
under `ZoneRoot/Streets`. Town buildings use the generic wall material and terracotta roof tiles from
`Assets/Materials/Building`. `BuildSafeZone`/`BuildWilderness`/
`BuildTutorial`/`BuildTestZone` build one scene each; `ZoneSceneBakerTests` and `WorldBootstrapBakerTests` cover
them. Every scene gets a baked `NetworkManager` (via `NetworkManagerSceneBaker.CreateInActiveScene`,
token auth on, its own port: tutorial `7772`, safe `7770`, wilderness `7771`, test `7773`), an authored camera
rig, a `ZoneRiskConfig`, spawn points carrying `PlayerSpawnPoint` (four in the town, two in the
wilderness, one in the tutorial; the first is the zone default) wired into the `PlayerSpawner`, a
`WorldBootstrap`, and one `ZoneTransferTrigger` per connection (`tutorial → safe → wilderness` and
back, plus `safe ↔ testzone`).
The owning client shows that gate's destination as a prompt; pressing E asks the server to transfer.
Each scene also authors `ResourceNodeMarker` / `CraftingStationMarker` / `EnemyMarker` children under
`ZoneRoot/Markers` that the server spawns from or configures a spawn controller for — the town has a
smithy (furnace + anvil) and edge veins, the wilderness has scattered veins and a few enemy spawn
points, and the tutorial has a target dummy plus
a furnace (the placeholder forge the first iron-ore refining recipe runs at; see
[07](07-crafting-and-gathering.md)). The zone's `SocialHubService.ZoneId` and raid-registry
host/port/template are set so the servers are addressable and distinct, and all four scenes are
added to Build Settings so the `NetworkManager` build guard checks them.

The tutorial is the **new-player** zone: it is the Main Menu's default (fallback) zone, so a login
with no stored "last zone" lands there. See `MainMenuSceneBaker` above and
[04](04-networking-and-the-join-flow.md) for the routing.

Geometry is assembled with `PrefabUtility.InstantiatePrefab` over the primitive **graybox prefab kit**
in `Assets/Prefabs/Graybox` — plain built-in-mesh prefabs, deliberately **not** ProBuilder. The ground
layer uses `Assets/Prefabs/Ground/Ground_Grass.prefab` in the tutorial and wilderness, and
`Assets/Prefabs/Ground/Ground_Cobblestone.prefab` in the safe zone; walls, roofs and scatter pieces
stay on the graybox kit. `GroundPrefabFor` (`ZoneSceneBaker.cs`) selects the ground prefab per layout,
so one zone can be given a different surface without changing the others. The baker is idempotent: a second run
regenerates the same scene (the wilderness scatter uses a fixed random seed) rather than duplicating
geometry or managers.

### WorldBootstrapBaker

`Assets/Editor/WorldBootstrapBaker.cs`. `WireActiveScene` (`Tools > MMO > Wire Zone With World
Bootstrap`) is the generic "connect this scene to the server" step for hand-authored scenes:
`EnsureInActiveScene` adds a `WorldBootstrap` to the scene's `NetworkManager`,
`EnsureSpawnPointComponents` tags every `PlayerSpawn_*` under a `SpawnPoints` root with a
`PlayerSpawnPoint` (marking the first as the default), and `RegisterMarkerPrefabs` bakes the shared
resource-node prefab (via `ResourceNodeBaker`) plus any marker-referenced prefabs into the scene's
spawnable collection, so the server and every client that loads the scene agree on prefab ids.
`ZoneSceneBaker.BuildScene` calls `EnsureInActiveScene` and `RegisterMarkerPrefabs` as it assembles a
zone, so a generated zone is wired by construction and needs no second pass.

### UrpSetupBaker

`Assets/Editor/UrpSetupBaker.cs:22`. `EnsureUrp` (`:52`) loads or creates the
`UniversalRenderPipelineAsset` and its renderer, saves them under `Assets/Settings`, and assigns the
pipeline to `GraphicsSettings.defaultRenderPipeline` and every quality level.

## 5. The build guard

A client cannot join a scene without a `NetworkManager`, so the project refuses to build if any
enabled scene is missing one. The same pass also refuses to build a scene whose `NetworkManager`
cannot spawn a player. This is split into pure rules and an editor hook:

- `NetworkManagerBuildRules.FindViolations(IEnumerable<SceneBuildInfo>)`
  (`Assets/Editor/NetworkManagerBuildRules.cs`) is pure LINQ: it returns the paths of enabled
  scenes with no `NetworkManager`. It is unit tested (see `NetworkManagerBuildRulesTests`).
- `NetworkManagerBuildRules.FindPrefabViolations(IEnumerable<SceneBuildInfo>)` is the second pure
  rule: for every scene that *has* a `NetworkManager` it flags a missing spawnable-prefab
  collection, a collection holding a null entry, and a `PlayerSpawner` with no player prefab —
  any of which leaves a client connected but unspawned. Also unit tested.
- `NetworkManagerBuildGuard` (`Assets/Editor/NetworkManagerBuildGuard.cs`) is an
  `IPreprocessBuildWithReport`. On preprocess it collects scene info and throws a
  `BuildFailedException` listing every violation from both rules, aborting the build.

`CollectSceneInfo` (`NetworkManagerBuildGuard.cs`) opens each enabled build scene additively,
checks it with `NetworkManagerSceneBaker.TryFind`, gathers its spawnable prefabs and the
`PlayerSpawner`'s prefab (via reflection — FishNet exposes `_playerPrefab` only as a private
field), and closes it.

## 5a. Launching a dedicated zone server

The build contains every scene (MainMenu first, then the zones), so a bare launch would sit on the
menu. Passing `-zone <name>` turns the same binary into a dedicated zone server:

```
server.x86_64 -zone Tutorial     # listens on 7772
server.x86_64 -zone SafeZone     # listens on 7770
server.x86_64 -zone Wilderness   # listens on 7771
server.x86_64 -zone TestZone     # listens on 7773
```

The launch is a three-step handshake between existing components, so no scene needs re-baking:

1. `MainMenuBootstrap.TryRedirectToZone` (`Assets/Scripts/Bootstrap/MainMenuBootstrap.cs`) parses the
   switch with `DedicatedServerArgs.ParseZone`, confirms the name is an enabled build scene, calls
   `DedicatedServerMode.Enable(zone)` and `SceneManager.LoadScene(zone)` — and returns before any menu
   UI or Nakama login is wired. An unknown zone name logs an error and quits with code 1.
2. The loaded zone scene's `WorldBootstrap.Start` sees `DedicatedServerMode.IsActive` and calls
   `ServerManager.StartConnection()`, which opens the zone's baked port and spawns the world.
3. Once the server reports `Started`, `WorldBootstrap.RegisterGlobalScene` registers the already
   loaded zone scene as a FishNet **global scene** via `SceneManager.LoadGlobalScenes`. The launcher
   loaded the scene with Unity's `SceneManager` directly, which FishNet does not track, so without
   this call `_globalScenes` stays empty and FishNet tells every admitted client "no start scenes" —
   stranding it on the offline (main menu) scene. With the scene registered, FishNet broadcasts a
   load to each admitted client (`ReplaceOption.All`, so the client's main menu is swapped out for
   the zone) and adds the client to the scene so scene-scoped observers can see it. No zone scene
   needs re-baking for this; it mirrors FishNet's own `DefaultScene` component.

`DedicatedServerMode` is the process-wide switch that keeps a *client* build honest: a client that
loads a zone scene (FishNet syncs the scene to clients) never set the flag, so it never opens a
server of its own. `DedicatedServerArgs.ParseZone` and `DedicatedServerMode` are pure and unit tested
(`DedicatedServerLaunchTests`). The repo-root `run-servers.sh` wraps the per-zone launches into one
command (see [02](02-getting-started.md#option-d--a-dedicated-zone-server)).

Every baked zone scene carries **SO_REUSEADDR** on its Tugboat socket (`_reuseAddress: 1`, set by
`ZoneSceneBaker.EnableTransportReuseAddress` — FishNet exposes the flag only as a private serialized
field with no public setter). Without it, a quick restart leaves TIME_WAIT sockets on the zone port
that make the next bind fail with "Address already in use", leaving the zone silently offline.
`run-servers.sh` also kills any process already holding a zone's port before launching.

## 6. How a fresh zone scene comes together

```
Tools > MMO > Configure URP
Tools > MMO > Create Player Prefab
Tools > MMO > Create Target Dummy Prefab
Tools > MMO > Create Camera Rig                  ← the scene's ThirdPersonCameraRig
Tools > MMO > Create NetworkManager In Scene   ← builds/wires the NetworkManager
(place spawn points, markers, a ZoneTransferTrigger, terrain, lighting)
Tools > MMO > Wire Zone With World Bootstrap   ← tags spawn points, attaches WorldBootstrap, registers marker prefabs
save the scene and add it to Build Settings
```

Or, for the main menu specifically:

```
Tools > MMO > Create Main Menu Scene
```

Or, for the always-on zones (tutorial + town + wilderness) at once:

```
Tools > MMO > Build Zones
```

`Build Zones` is the one-shot path from the "fresh zone scene" recipe above, repeated for all four
zones and wired together: it bakes the `NetworkManager`, assembles the ground/boundary/buildings (or
scatter + hazards) from the graybox prefab kit, drops the `PlayerSpawnPoint`s, the markers and the
`ZoneRiskConfig`, attaches the `WorldBootstrap`, adds the cross-zone `ZoneTransferTrigger`, saves the
scenes and registers them in Build Settings.

## Common tasks

- **Scene is missing its NetworkManager.** Run `Tools > MMO > Create NetworkManager In Scene`.
- **Player prefab is missing or out of date.** Run `Tools > MMO > Create Player Prefab`. If you
  changed the player component list, this picks it up because it calls `PlayerPrefabFactory.Create`.
- **The player has no model or animation.** Run `Tools > MMO > Rebuild Player Character Assets`, then
  `Tools > MMO > Create Player Prefab` (or `Bake All`). Rebuilding the player prefab alone reuses the
  existing visual asset.
- **Build refuses to start.** Read the `BuildFailedException`: it names the enabled scene(s) that
  are missing a `NetworkManager`, hold a null spawnable-prefab entry, or have a `PlayerSpawner`
  with no prefab. Either fix the scene (re-bake it) or disable it in Build Settings.
- **No camera when a zone loads.** Run `Tools > MMO > Create Camera Rig` in that scene (or
  `Tools > MMO > Build Zones` to re-bake all four). A zone without a `CameraRig` renders nothing;
  the main menu is the one scene that is *supposed* to have none.
- **Add a new baker.** Add a static class in `Assets/Editor` with a `[MenuItem("Tools/MMO/...")]`
  method that calls a runtime factory, saves the prefab with `PrefabUtility.SaveAsPrefabAsset`, and
  destroys the scene copy in a `finally`. Add an EditMode test that cleans up after itself (see the
  `*TestAsset` helpers in [10](10-testing.md)).
- **Change the default player weapon.** Edit `PlayerPrefabBaker.DefaultWeaponAssetPath` (`:36`) and
  re-run the player prefab baker.
