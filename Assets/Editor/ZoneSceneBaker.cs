using System;
using System.Collections.Generic;
using System.IO;
using FishNet.Component.Spawning;
using FishNet.Managing;
using FishNet.Transporting.Tugboat;
using MMO.Crafting;
using MMO.Gameplay;
using MMO.Net;
using MMO.Social;
using UnityEditor;
using UnityEditor.SceneManagement;
using Unity.AI.Navigation; // Add at top of ZoneSceneBaker.cs
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MMO.Editor
{
    /// <summary>
    /// Assembles the three always-on zones the multi-server architecture is tested against: the
    /// flat <c>Tutorial</c> training ground, the walkable <c>SafeZone</c> town and the hostile
    /// <c>Wilderness</c>. Each is a self-contained scene — its own <see cref="NetworkManager"/> on
    /// its own port, its own <see cref="ZoneRiskConfig"/>, its own
    /// <see cref="SocialHubService.ZoneId"/> and a <see cref="ZoneTransferTrigger"/> road to the
    /// next one — so the set really is separate servers rather than rooms of one.
    ///
    /// <para>
    /// Geometry is assembled from the primitive <b>graybox prefab kit</b>
    /// (<c>Assets/Prefabs/Graybox</c>) with <see cref="PrefabUtility.InstantiatePrefab"/>: this is
    /// plain, dependency-free geometry, deliberately not ProBuilder. Re-running the baker
    /// regenerates each zone from scratch into the same file, so it is idempotent: a second run
    /// produces an identical scene instead of duplicating geometry or managers.
    /// </para>
    /// </summary>
    public static class ZoneSceneBaker
    {
        public const string MenuPath = "Tools/MMO/Build Zones";
        public const string SafeZonePath = "Assets/Scenes/SafeZone.unity";
        public const string WildernessZonePath = "Assets/Scenes/Wilderness.unity";
        public const string TutorialZonePath = "Assets/Scenes/Tutorial.unity";

        /// <summary>Scene/zone names used as the transfer trigger's target zone label.</summary>
        public const string SafeZoneName = "SafeZone";
        public const string WildernessZoneName = "Wilderness";
        public const string TutorialZoneName = "Tutorial";

        /// <summary>
        /// Distinct ports so both zones can run side by side on one machine — the whole point of
        /// testing the multi-server architecture locally. A real deployment overrides host/port
        /// (see issues #57/#61).
        /// </summary>
        public const ushort SafeZonePort = 7770;
        public const ushort WildernessZonePort = 7771;
        public const ushort TutorialZonePort = 7772;

        /// <summary>
        /// Stable server ids. The same ids are written to the zone's
        /// <see cref="SocialHubService.ZoneId"/> and to the other zone's transfer trigger source
        /// and target, so a handoff is addressable end to end.
        /// </summary>
        public const string SafeZoneServerId = "server-a";
        public const string WildernessServerId = "server-b";
        public const string TutorialServerId = "server-tutorial";

        /// <summary>Host the transfer triggers point at when the destination is a local process.</summary>
        public const string DefaultTargetHost = "127.0.0.1";

        // ---- Layout -------------------------------------------------------

        /// <summary>Full width and depth of the walkable ground, in metres.</summary>
        public const float GroundExtent = 40f;

        /// <summary>Footprint of a single ground tile. The kit's Floor_2x2 is 2 m on a side.</summary>
        public const float GroundTile = 2f;

        public const int SpawnPointCount = 4;
        public const int WildernessSpawnPointCount = 2;
        public const int TutorialSpawnPointCount = 1;
        public const float PortalHeight = 3f;

        // ---- Names --------------------------------------------------------

        public const string ZoneRootName = "ZoneRoot";
        public const string GroundName = "Ground";
        public const string BoundaryWallsName = "BoundaryWalls";
        public const string BuildingsName = "Buildings";
        public const string ScatterName = "Scatter";
        public const string HazardsName = "Hazards";
        public const string SpawnPointsName = "SpawnPoints";
        public const string MarkersName = "Markers";
        public const string RiskConfigName = "ZoneRiskConfig";
        public const string TerritoryName = "Territory";

        // ---- Prefabs ------------------------------------------------------

        private const string GrayboxFolder = "Assets/Prefabs/Graybox/";
        private const string FloorTilePrefab = GrayboxFolder + "Floor_2x2.prefab";
        private const string WallPrefab = GrayboxFolder + "Wall_2x2.prefab";
        private const string TallWallPrefab = GrayboxFolder + "Wall_1x3.prefab";
        private const string RampPrefab = GrayboxFolder + "Ramp_1x2.prefab";
        private const string RedMaterial = "Assets/Materials/DevTextures/DevGrid_Red.mat";

        // ---- Wilderness scatter ------------------------------------------

        private const int ScatterSeed = 20261015;
        private const int ScatterClusters = 14;
        private const int ScatterReservedAttempts = 100;
        private const float ScatterInset = 3f;

        private enum ZoneLayout
        {
            SafeZone,
            Wilderness,
            Tutorial,
        }

        [MenuItem(MenuPath)]
        public static void BuildBoth()
        {
            BuildSafeZone();
            BuildWilderness();
            BuildTutorial();
            AddToBuildSettings(SafeZonePath, WildernessZonePath, TutorialZonePath);
        }

        /// <summary>Rebuilds <c>Assets/Scenes/SafeZone.unity</c>.</summary>
        public static void BuildSafeZone() => BuildScene(
            scenePath: SafeZonePath,
            risk: ZoneRiskTier.Safe,
            port: SafeZonePort,
            serverId: SafeZoneServerId,
            templateId: "safezone",
            targetHost: DefaultTargetHost,
            targetPort: WildernessZonePort,
            targetZoneName: WildernessZoneName,
            targetServerId: WildernessServerId,
            portalName: "PortalToWilderness",
            layout: ZoneLayout.SafeZone);

        /// <summary>Rebuilds <c>Assets/Scenes/Wilderness.unity</c>.</summary>
        public static void BuildWilderness() => BuildScene(
            scenePath: WildernessZonePath,
            risk: ZoneRiskTier.Red,
            port: WildernessZonePort,
            serverId: WildernessServerId,
            templateId: "wilderness",
            targetHost: DefaultTargetHost,
            targetPort: SafeZonePort,
            targetZoneName: SafeZoneName,
            targetServerId: SafeZoneServerId,
            portalName: "PortalToSafeZone",
            layout: ZoneLayout.Wilderness);

        /// <summary>
        /// Rebuilds <c>Assets/Scenes/Tutorial.unity</c>: the flat training ground a brand-new player
        /// lands in. Deliberately empty — flat ground, one spawn point and a target dummy to hit —
        /// with a single road out to the SafeZone, so the first thing a new character sees is the
        /// game's tag-loops rather than a cliff, and the only thing to do is try the combat the
        /// loop is built on. New players are routed here by the Main Menu's default zone (the
        /// fallback when the backend reports no last zone).
        /// </summary>
        public static void BuildTutorial() => BuildScene(
            scenePath: TutorialZonePath,
            risk: ZoneRiskTier.Safe,
            port: TutorialZonePort,
            serverId: TutorialServerId,
            templateId: "tutorial",
            targetHost: DefaultTargetHost,
            targetPort: SafeZonePort,
            targetZoneName: SafeZoneName,
            targetServerId: SafeZoneServerId,
            portalName: "PortalToSafeZone",
            layout: ZoneLayout.Tutorial);

        private static void BuildScene(
            string scenePath,
            ZoneRiskTier risk,
            ushort port,
            string serverId,
            string templateId,
            string targetHost,
            ushort targetPort,
            string targetZoneName,
            string targetServerId,
            string portalName,
            ZoneLayout layout)
        {
            EnsureSceneFolder();

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            CreateSun();

            // The one camera a client looks through. Authored in the zone (rather than on the
            // player prefab) so the menu never gets one and the rig survives scene loads. Every
            // zone scene needs its own: DontDestroyOnLoad keeps the first one alive, but a client
            // that joins a zone directly has nothing to look through without it.
            LocalCameraBaker.EnsureInActiveScene();
            // Token auth on: the join gate validates the session the backend issued, and a
            // cross-zone handoff carries both that session and the transfer token (see #67).
            var settings = new MMOConnectionSettings(
                port,
                MMOConnectionSettings.DefaultMaxClients,
                MMOConnectionSettings.DefaultHandshakeTimeoutSeconds,
                useTokenAuth: true);

            NetworkManager manager = NetworkManagerSceneBaker.CreateInActiveScene(settings);
            ConfigureZoneIdentity(manager, serverId, templateId, port);
            EnableTransportReuseAddress(manager);

            // NetworkManagerFactory attaches a fixed single target-dummy spawner and
            // crafting-station spawner so a bare dev scene has one of each. A zone authors those —
            // and every other world object — as markers instead, so strip the fixed pair or each
            // zone would spawn two dummies (and the tutorial, which is only a dummy, would also get
            // an unwanted station). The PlayerSpawner stays: WorldBootstrap hands it the zone's
            // spawn points.
            RemoveFixedSpawners(manager);

            var zoneRoot = new GameObject(ZoneRootName);
            Transform ground = CreateChild(GroundName, zoneRoot.transform).transform;
            Transform boundary = CreateChild(BoundaryWallsName, zoneRoot.transform).transform;

            BuildGround(ground);
            BuildBoundary(boundary);

            if (layout == ZoneLayout.SafeZone)
            {
                BuildTown(CreateChild(BuildingsName, zoneRoot.transform).transform);
            }
            else if (layout == ZoneLayout.Wilderness)
            {
                BuildWilderness(
                    CreateChild(ScatterName, zoneRoot.transform).transform,
                    CreateChild(HazardsName, zoneRoot.transform).transform);
                CreateClaimFlag(zoneRoot.transform);
            }
            // The tutorial zone is deliberately bare: flat ground and boundary only.

            WireSpawnPoints(manager, CreateSpawnPoints(zoneRoot.transform, SpawnCountFor(layout)));
            AddMarkers(zoneRoot.transform, layout);

            // Connects the scene to the server: spawn points, marker-driven world objects and
            // player placement. Added after the geometry so the bootstrap sees the finished scene.
            WorldBootstrapBaker.EnsureInActiveScene();
            WorldBootstrapBaker.RegisterMarkerPrefabs(manager);

            CreateRiskConfig(zoneRoot.transform, risk);
            CreatePortal(zoneRoot.transform, portalName, serverId, targetHost, targetPort, targetServerId, targetZoneName);

            // --- INJECT NAVMESH BAKING HERE ---
            // No obstacle is placed here: the crafting station carries its own NavMeshObstacle
            // and is spawned at the marker, so the hole in the NavMesh is carved around the
            // station wherever it appears — a second copy baked into the scene would only
            // duplicate that hole.

            // Create the NavMeshSurface object as a child of ZoneRoot
            var navMeshGo = new GameObject("NavMeshSurface");
            navMeshGo.transform.SetParent(zoneRoot.transform, false);
            var surface = navMeshGo.AddComponent<Unity.AI.Navigation.NavMeshSurface>();

            // Configure it to scan all generated geometry and bake the mesh
            surface.collectObjects = Unity.AI.Navigation.CollectObjects.All;
            surface.BuildNavMesh();

            // BuildNavMesh() only creates in-memory NavMeshData. SaveScene cannot persist a
            // reference to a non-asset object, so the scene would save with a null m_NavMeshData
            // and the runtime NavMesh would never load (NavMesh.SamplePosition fails everywhere).
            // Persist the data as an asset at a deterministic path — the same layout the AI
            // Navigation editor uses — deleting any prior bake so re-baking does not accumulate
            // orphans.
            SaveNavMeshAsset(surface, scenePath);
            // ----------------------------------

            SaveScene(scene, scenePath);
        }

        private static int SpawnCountFor(ZoneLayout layout) => layout switch
        {
            ZoneLayout.Wilderness => WildernessSpawnPointCount,
            ZoneLayout.Tutorial => TutorialSpawnPointCount,
            _ => SpawnPointCount,
        };

        /// <summary>
        /// Removes the factory's single fixed dummy/station spawners so the zone's markers are the
        /// only thing that puts interactive objects in the world.
        /// </summary>
        private static void RemoveFixedSpawners(NetworkManager manager)
        {
            foreach (TargetDummySpawner dummy in manager.GetComponents<TargetDummySpawner>())
                UnityEngine.Object.DestroyImmediate(dummy);

            foreach (CraftingStationSpawner station in manager.GetComponents<CraftingStationSpawner>())
                UnityEngine.Object.DestroyImmediate(station);
        }

        // ---- Zone identity -------------------------------------------------

        /// <summary>
        /// Turns on <c>SO_REUSEADDR</c> for the zone's Tugboat socket. Tugboat exposes this only as
        /// a private serialized field (<c>_reuseAddress</c>, no public setter) and defaults it to
        /// off, so a dedicated server restarted while sockets from the previous run are still in
        /// TIME_WAIT fails to bind its port with "Address already in use" and the zone silently
        /// comes up offline. Baking the flag into every zone scene makes rebinding the port a
        /// property of the scene rather than something each launch has to arrange.
        /// </summary>
        private static void EnableTransportReuseAddress(NetworkManager manager)
        {
            var tugboat = manager.GetComponent<Tugboat>();
            if (tugboat == null)
                return;

            var serialized = new SerializedObject(tugboat);
            serialized.FindProperty("_reuseAddress").boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigureZoneIdentity(NetworkManager manager, string serverId, string templateId, ushort port)
        {
            SocialHubService hub = manager.GetComponent<SocialHubService>();
            if (hub == null)
                return;

            hub.ZoneId = serverId;

            // The raid registry heartbeat (issue #57) advertises this zone's host/port/template;
            // the fields are private serialized so they are set the same way the project sets other
            // inspector-only data (see PlayerPrefabBaker).
            var serialized = new SerializedObject(hub);
            serialized.FindProperty("_instanceHostAddress").stringValue = DefaultTargetHost;
            serialized.FindProperty("_instanceHostPort").intValue = port;
            serialized.FindProperty("_zoneTemplateId").stringValue = templateId;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        // ---- Geometry ------------------------------------------------------

        private static void BuildGround(Transform parent)
        {
            GameObject floor = LoadPrefab(FloorTilePrefab);
            int tiles = Mathf.RoundToInt(GroundExtent / GroundTile);
            float start = -GroundExtent / 2f + GroundTile / 2f;

            for (int x = 0; x < tiles; x++)
                for (int z = 0; z < tiles; z++)
                    PlaceKit(floor, parent, start + x * GroundTile, start + z * GroundTile);
        }

        private static void BuildBoundary(Transform parent)
        {
            GameObject wall = LoadPrefab(WallPrefab);
            int segments = Mathf.RoundToInt(GroundExtent / GroundTile);
            float half = GroundExtent / 2f;
            float start = -half + GroundTile / 2f;

            // The two central segments of the +Z edge are left out: that 4 m opening is the
            // doorway the portal volume sits in.
            int doorA = segments / 2 - 1;
            int doorB = segments / 2;

            for (int i = 0; i < segments; i++)
            {
                float offset = start + i * GroundTile;

                if (i != doorA && i != doorB)
                    PlaceKit(wall, parent, offset, half);

                PlaceKit(wall, parent, offset, -half);
                PlaceKit(wall, parent, half, offset, 90f);
                PlaceKit(wall, parent, -half, offset, 90f);
            }
        }

        private static void BuildTown(Transform parent)
        {
            BuildBuilding(parent, "TownHall", new Vector3(-10f, 0f, -10f), width: 8, depth: 6);
            BuildBuilding(parent, "House_A", new Vector3(10f, 0f, -10f), width: 6, depth: 6);
            BuildBuilding(parent, "House_B", new Vector3(10f, 0f, 10f), width: 6, depth: 6);
        }

        /// <summary>
        /// A simple square building: 3 m kit walls on all four sides with a 2 m door gap centred on
        /// the -Z wall, and a flat kit roof at 3 m. Everything is on the kit's 1 m grid.
        /// </summary>
        private static void BuildBuilding(Transform parent, string name, Vector3 center, int width, int depth)
        {
            Transform root = CreateChild(name, parent).transform;
            GameObject wall = LoadPrefab(TallWallPrefab);
            GameObject roof = LoadPrefab(FloorTilePrefab);

            float halfWidth = width / 2f;
            float halfDepth = depth / 2f;

            // Walls running along X, on the ±Z edges. The -Z wall carries the doorway.
            for (int i = 0; i < width; i++)
            {
                float x = center.x - halfWidth + 0.5f + i;

                if (i != width / 2 - 1 && i != width / 2)
                    PlaceKit(wall, root, x, center.z - halfDepth);

                PlaceKit(wall, root, x, center.z + halfDepth);
            }

            // Walls running along Z, on the ±X edges (rotated 90°).
            for (int i = 0; i < depth; i++)
            {
                float z = center.z - halfDepth + 0.5f + i;
                PlaceKit(wall, root, center.x - halfWidth, z, 90f);
                PlaceKit(wall, root, center.x + halfWidth, z, 90f);
            }

            // Flat roof of 2×2 kit floors at the top of the walls.
            int tilesX = width / 2;
            int tilesZ = depth / 2;
            for (int ix = 0; ix < tilesX; ix++)
                for (int iz = 0; iz < tilesZ; iz++)
                    PlaceKit(roof, root,
                        center.x - halfWidth + 1f + ix * GroundTile,
                        center.z - halfDepth + 1f + iz * GroundTile,
                        y: 3f);
        }

        private static void BuildWilderness(Transform scatter, Transform hazards)
        {
            // Two obvious danger pockets. Their red-tinted kit floor makes the volume visible while
            // the ZoneRiskConfig makes it real to the death rules.
            BuildHazard(hazards, 1, new Vector2(12f, -12f), tiles: 3);
            BuildHazard(hazards, 2, new Vector2(-12f, 12f), tiles: 3);

            // Deterministic scatter: a fixed seed keeps the second baker run identical to the first,
            // which is what makes the baker idempotent.
            var random = new System.Random(ScatterSeed);
            GameObject wall = LoadPrefab(WallPrefab);
            GameObject tallWall = LoadPrefab(TallWallPrefab);
            GameObject ramp = LoadPrefab(RampPrefab);

            for (int cluster = 0; cluster < ScatterClusters; cluster++)
            {
                Vector2 spot = PickScatterSpot(random);
                int pieces = 1 + random.Next(3);
                for (int p = 0; p < pieces; p++)
                {
                    GameObject prefab = random.Next(3) switch
                    {
                        0 => wall,
                        1 => tallWall,
                        _ => ramp,
                    };

                    float jitterX = (float)(random.NextDouble() - 0.5);
                    float jitterZ = (float)(random.NextDouble() - 0.5);
                    float yaw = (float)(random.NextDouble() * 360.0);
                    PlaceKit(prefab, scatter, spot.x + jitterX, spot.y + jitterZ, yaw);
                }
            }
        }

        private static Vector2 PickScatterSpot(System.Random random)
        {
            float bound = GroundExtent / 2f - ScatterInset;
            for (int attempt = 0; attempt < ScatterReservedAttempts; attempt++)
            {
                float x = (float)(random.NextDouble() * 2.0 - 1.0) * bound;
                float z = (float)(random.NextDouble() * 2.0 - 1.0) * bound;
                if (!IsReserved(x, z))
                    return new Vector2(x, z);
            }

            // Fall back to a corner rather than overlap the spawn clearing.
            return new Vector2(-bound, -bound);
        }

        private static bool IsReserved(float x, float z)
        {
            if (Mathf.Abs(x) < 6f && Mathf.Abs(z) < 6f)
                return true; // spawn clearing

            if (z > 13f && Mathf.Abs(x) < 4f)
                return true; // portal corridor

            if (Mathf.Abs(x - 12f) < 5f && Mathf.Abs(z + 12f) < 5f)
                return true; // hazard 1

            if (Mathf.Abs(x + 12f) < 5f && Mathf.Abs(z - 12f) < 5f)
                return true; // hazard 2

            return false;
        }

        private static void BuildHazard(Transform parent, int index, Vector2 center, int tiles)
        {
            Transform root = CreateChild($"Hazard_{index}", parent).transform;
            root.localPosition = new Vector3(center.x, 0f, center.y);

            var collider = root.gameObject.AddComponent<BoxCollider>();
            collider.isTrigger = true;
            collider.center = new Vector3(0f, 1f, 0f);
            collider.size = new Vector3(tiles * GroundTile, 2f, tiles * GroundTile);

            var config = root.gameObject.AddComponent<ZoneRiskConfig>();
            config.SetTier(ZoneRiskTier.Red);

            GameObject marker = LoadPrefab(FloorTilePrefab);
            Material red = AssetDatabase.LoadAssetAtPath<Material>(RedMaterial);
            float start = -tiles * GroundTile / 2f + GroundTile / 2f;

            for (int x = 0; x < tiles; x++)
            {
                for (int z = 0; z < tiles; z++)
                {
                    GameObject tile = PlaceKit(marker, root, start + x * GroundTile, start + z * GroundTile);
                    var renderer = tile.GetComponent<MeshRenderer>();
                    if (red != null && renderer != null)
                    {
                        renderer.sharedMaterial = red;
                        PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                    }
                }
            }
        }

        // ---- Spawns, risk and portal ---------------------------------------

        private static GameObject[] CreateSpawnPoints(Transform zoneRoot, int count)
        {
            Transform parent = CreateChild(SpawnPointsName, zoneRoot).transform;

            // Y is set to 1.0f so a 2-unit tall capsule with a center pivot rests on top of the floor (Y = 0)
            Vector3[] positions =
            {
                new Vector3(0f, 1.0f, 0f),
                new Vector3(3f, 1.0f, 0f),
                new Vector3(-3f, 1.0f, 0f),
                new Vector3(0f, 1.0f, 3f),
            };

            count = Mathf.Clamp(count, 1, positions.Length);

            var spawns = new GameObject[count];
            for (int i = 0; i < count; i++)
            {
                var spawn = new GameObject($"PlayerSpawn_{i + 1}");
                spawn.transform.SetParent(parent, false);
                spawn.transform.localPosition = positions[i];

                // The marker the server reads to place a joining player; the first one is the
                // zone's default (where a fresh character with no saved position appears).
                var point = spawn.AddComponent<PlayerSpawnPoint>();
                point.SetDefault(i == 0);
                spawns[i] = spawn;
            }

            return spawns;
        }

        private static void WireSpawnPoints(NetworkManager manager, GameObject[] spawns)
        {
            // FishNet's PlayerSpawner is what actually places a connecting client's player object;
            // without the Spawns array the player would appear at the origin.
            var spawner = manager.GetComponent<PlayerSpawner>();
            if (spawner == null)
                return;

            var transforms = new Transform[spawns.Length];
            for (int i = 0; i < spawns.Length; i++)
                transforms[i] = spawns[i].transform;

            spawner.Spawns = transforms;
        }

        // ---- Server-spawned world objects ----------------------------------

        /// <summary>
        /// Authors the markers the server turns into networked world objects on start. A node, a
        /// station and an enemy are all just a marker here: geometry the server instantiates and
        /// publishes, never a loose object in the scene. Heights sit each object on the ground
        /// (a cube's mesh is centred on its origin).
        /// </summary>
        private static void AddMarkers(Transform zoneRoot, ZoneLayout layout)
        {
            Transform markers = CreateChild(MarkersName, zoneRoot).transform;

            float nodeY = ResourceNodePrefabFactory.DefaultHeight / 2f;
            float stationY = CraftingStationPrefabFactory.DefaultHeight / 2f;
            float enemyY = TargetDummyPrefabFactory.DefaultHeight / 2f;

            if (layout == ZoneLayout.SafeZone)
            {
                // The smithy: a furnace and an anvil beside the town square.
                AddStationMarker(markers, 1, new Vector3(-6f, stationY, -6f), CraftingStationType.Furnace);
                AddStationMarker(markers, 2, new Vector3(-4f, stationY, -7f), CraftingStationType.Anvil);

                // A few veins at the city edge to gather from.
                AddNodeMarker(markers, 1, new Vector3(-15f, nodeY, 5f));
                AddNodeMarker(markers, 2, new Vector3(-15f, nodeY, -4f));
                AddNodeMarker(markers, 3, new Vector3(15f, nodeY, 8f));
                return;
            }

            if (layout == ZoneLayout.Wilderness)
            {
                // Resources worth the risk, clear of the spawn clearing, portal and hazards.
                AddNodeMarker(markers, 1, new Vector3(-8f, nodeY, -12f));
                AddNodeMarker(markers, 2, new Vector3(8f, nodeY, 12f));
                AddNodeMarker(markers, 3, new Vector3(-14f, nodeY, -6f));
                AddNodeMarker(markers, 4, new Vector3(6f, nodeY, -16f));

                // Enemies. The AI is #60's job; for now a target dummy is the enemy placed here.
                AddEnemyMarker(markers, 1, new Vector3(5f, enemyY, -8f));
                AddEnemyMarker(markers, 2, new Vector3(-8f, enemyY, 8f));
                AddEnemyMarker(markers, 3, new Vector3(10f, enemyY, 2f));
                return;
            }

            // Tutorial: the one dummy a new player learns to hit, a few metres in front of spawn,
            // plus the furnace they refine their first gathered ore at — the furnace is what makes
            // the iron ore -> iron ingot recipe reachable from a fresh character. The vein is
            // bare-handed (the shipped definition asks for no tool), so the first ore needs no
            // gear the player has not been given yet.
            AddNodeMarker(markers, 1, new Vector3(-3f, nodeY, 3f));
            AddEnemyMarker(markers, 1, new Vector3(0f, enemyY, 4f));
            AddStationMarker(markers, 1, new Vector3(4f, stationY, 4f), CraftingStationType.Furnace);
        }

        private static void AddNodeMarker(Transform parent, int index, Vector3 position)
        {
            var go = CreateChild($"ResourceNodeMarker_{index}", parent);
            go.transform.localPosition = position;
            go.AddComponent<ResourceNodeMarker>();
        }

        private static void AddStationMarker(Transform parent, int index, Vector3 position, CraftingStationType type)
        {
            var go = CreateChild($"CraftingStationMarker_{index}", parent);
            go.transform.localPosition = position;
            var marker = go.AddComponent<CraftingStationMarker>();
            marker.SetStationType(type);
        }

        private static void AddEnemyMarker(Transform parent, int index, Vector3 position)
        {
            var go = CreateChild($"EnemyMarker_{index}", parent);
            go.transform.localPosition = position;
            go.AddComponent<EnemyMarker>();
        }

        private static void CreateRiskConfig(Transform zoneRoot, ZoneRiskTier tier)
        {
            var go = CreateChild(RiskConfigName, zoneRoot);
            var config = go.AddComponent<ZoneRiskConfig>();
            config.SetTier(tier);
        }

        /// <summary>
        /// Authors the Wilderness claim flag: a scene volume (no prefab, no NetworkObject) clear of
        /// the spawn clearing, portal corridor, nodes and hazards.
        /// </summary>
        private static void CreateClaimFlag(Transform zoneRoot)
        {
            var go = CreateChild(TerritoryName, zoneRoot);
            go.transform.localPosition = new Vector3(-16f, 0f, 16f);

            var collider = go.AddComponent<BoxCollider>();
            collider.isTrigger = true;
            collider.center = new Vector3(0f, 1f, 0f);
            collider.size = new Vector3(8f, 2f, 8f);

            var flag = go.AddComponent<ClaimFlag>();
            flag.Configure("wilderness-keep", new VulnerabilityWindow
            {
                Days = new[] { DayOfWeek.Saturday, DayOfWeek.Sunday },
                StartMinuteUtc = 18 * 60,
                EndMinuteUtc = 22 * 60,
            });
        }

        private static void CreatePortal(
            Transform zoneRoot,
            string portalName,
            string sourceServerId,
            string targetHost,
            ushort targetPort,
            string targetServerId,
            string targetZoneName)
        {
            var go = CreateChild(portalName, zoneRoot);
            go.transform.localPosition = new Vector3(0f, PortalHeight / 2f, GroundExtent / 2f - 0.5f);

            // ZoneTransferTrigger requires a Collider; add the BoxCollider first so the required
            // component is already satisfied. It is a trigger volume, not an obstacle.
            var collider = go.AddComponent<BoxCollider>();
            collider.isTrigger = true;
            collider.size = new Vector3(GroundTile * 2f, PortalHeight, GroundTile);

            var trigger = go.AddComponent<ZoneTransferTrigger>();
            var serialized = new SerializedObject(trigger);
            serialized.FindProperty("_targetHost").stringValue = targetHost;
            serialized.FindProperty("_targetPort").intValue = targetPort;
            serialized.FindProperty("_targetServerId").stringValue = targetServerId;
            serialized.FindProperty("_targetZoneName").stringValue = targetZoneName;
            serialized.FindProperty("_sourceServerId").stringValue = sourceServerId;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        // ---- Helpers -------------------------------------------------------

        private static void CreateSun()
        {
            var go = new GameObject("Directional Light");
            go.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            go.transform.position = new Vector3(0f, 3f, 0f);

            Light light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1f;
            light.color = new Color(1f, 0.956f, 0.839f);
            light.shadows = LightShadows.Soft;
        }

        private static GameObject CreateChild(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go;
        }

        /// <summary>
        /// Instantiates a graybox kit prefab under <paramref name="parent"/>. The prefab's stored
        /// local Y is kept (the kit authors each piece so its base sits on Y = 0) unless an explicit
        /// <paramref name="y"/> is given for stacked pieces such as roof tiles.
        /// </summary>
        private static GameObject PlaceKit(GameObject prefab, Transform parent, float x, float z, float yRotation = 0f, float? y = null)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            Transform transform = instance.transform;
            transform.localPosition = new Vector3(x, y ?? transform.localPosition.y, z);
            transform.localRotation = Quaternion.Euler(0f, yRotation, 0f);
            return instance;
        }

        private static GameObject LoadPrefab(string path)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
                throw new InvalidOperationException($"Graybox kit prefab missing at '{path}'.");

            return prefab;
        }

        private static void EnsureSceneFolder()
        {
            if (!Directory.Exists("Assets/Scenes"))
                Directory.CreateDirectory("Assets/Scenes");
        }

        private static void SaveScene(Scene scene, string path)
        {
            if (!EditorSceneManager.SaveScene(scene, path))
                throw new InvalidOperationException($"Could not save zone scene to '{path}'.");
        }

        /// <summary>
        /// Persists <paramref name="surface"/>'s baked <c>NavMeshData</c> as an asset next to the
        /// zone scene, mirroring the AI Navigation editor's own layout
        /// (<c>Assets/Scenes/&lt;Zone&gt;/NavMesh-&lt;SurfaceName&gt;.asset</c>). Without this the
        /// scene saves with a null data reference and no NavMesh exists at runtime.
        /// </summary>
        private static void SaveNavMeshAsset(NavMeshSurface surface, string scenePath)
        {
            if (surface == null || surface.navMeshData == null)
                throw new InvalidOperationException(
                    $"NavMeshSurface on '{surface?.name}' has no baked data to save.");

            // Same folder the AI Navigation editor uses: a directory named after the scene,
            // beside the scene file.
            string sceneDir = Path.GetDirectoryName(scenePath);
            string sceneName = Path.GetFileNameWithoutExtension(scenePath);
            string assetDir = Path.Combine(sceneDir ?? "Assets", sceneName);
            if (!Directory.Exists(assetDir))
                Directory.CreateDirectory(assetDir);

            string assetPath = Path.Combine(assetDir, "NavMesh-" + surface.name + ".asset");

            // Delete any prior bake so re-baking overwrites rather than accumulating orphans.
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath) != null)
                AssetDatabase.DeleteAsset(assetPath);

            AssetDatabase.CreateAsset(surface.navMeshData, assetPath);
            AssetDatabase.SaveAssets();
        }

        /// <summary>Adds the scenes to the build list (enabled) unless they are already there.</summary>
        private static void AddToBuildSettings(params string[] paths)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);

            foreach (string path in paths)
            {
                if (scenes.Exists(s => s.path == path))
                    continue;

                scenes.Add(new EditorBuildSettingsScene(path, true));
            }

            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
