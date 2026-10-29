using System;
using System.Collections.Generic;
using System.IO;
using FishNet.Component.Spawning;
using FishNet.Managing;
using FishNet.Transporting.Tugboat;
using MMO.Banking;
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
        public const string SafeZoneServerId = ZoneServerIds.SafeZone;
        public const string WildernessServerId = ZoneServerIds.Wilderness;
        public const string TutorialServerId = ZoneServerIds.Tutorial;

        /// <summary>Host the transfer triggers point at when the destination is a local process.</summary>
        public const string DefaultTargetHost = "127.0.0.1";

        // ---- Layout -------------------------------------------------------

        /// <summary>Tutorial width/depth. Also the extent used to keep wilderness content central.</summary>
        public const float GroundExtent = 40f;

        /// <summary>Safe zone and wilderness are four times longer on each side than the tutorial.</summary>
        public const float SafeZoneGroundExtent = GroundExtent * 4f;
        public const float WildernessGroundExtent = GroundExtent * 4f;
        public const float TutorialGroundExtent = GroundExtent;

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
        public const string StreetsName = "Streets";
        public const string ScatterName = "Scatter";
        public const string HazardsName = "Hazards";
        public const string SpawnPointsName = "SpawnPoints";
        public const string MarkersName = "Markers";
        public const string RiskConfigName = "ZoneRiskConfig";
        public const string TerritoryName = "Territory";

        // ---- Prefabs ------------------------------------------------------


        // ---- Wilderness scatter ------------------------------------------


        internal enum ZoneLayout
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
            ZoneBuildFiles.AddToBuildSettings(SafeZonePath, WildernessZonePath, TutorialZonePath);
        }

        /// <summary>Rebuilds <c>Assets/Scenes/SafeZone.unity</c>.</summary>
        [MenuItem("Tools/MMO/Build Safe Zone")]
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
        [MenuItem("Tools/MMO/Build Wilderness Zone")]
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
        [MenuItem("Tools/MMO/Build Tutorial Zone")]
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
            ZoneBuildFiles.EnsureSceneFolder();

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            ZoneNetworkAuthoring.CreateSun();

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
            ZoneNetworkAuthoring.ConfigureZoneIdentity(manager, serverId, templateId, port);
            ZoneNetworkAuthoring.EnableTransportReuseAddress(manager);

            // NetworkManagerFactory attaches a fixed single target-dummy spawner and
            // crafting-station spawner so a bare dev scene has one of each. A zone authors those —
            // and every other world object — as markers instead, so strip the fixed pair or each
            // zone would spawn two dummies (and the tutorial, which is only a dummy, would also get
            // an unwanted station). The PlayerSpawner stays: WorldBootstrap hands it the zone's
            // spawn points.
            ZoneNetworkAuthoring.RemoveFixedSpawners(manager);

            var zoneRoot = new GameObject(ZoneRootName);
            Transform ground = CreateChild(GroundName, zoneRoot.transform).transform;
            Transform boundary = CreateChild(BoundaryWallsName, zoneRoot.transform).transform;
            float groundExtent = GroundExtentFor(layout);

            GrayboxZoneGeometry.BuildGround(ground, GroundPrefabFor(layout), groundExtent);
            GrayboxZoneGeometry.BuildBoundary(boundary, groundExtent);

            if (layout == ZoneLayout.SafeZone)
            {
                GrayboxZoneGeometry.BuildTown(
                    CreateChild(BuildingsName, zoneRoot.transform).transform,
                    CreateChild(StreetsName, zoneRoot.transform).transform);
            }
            else if (layout == ZoneLayout.Wilderness)
            {
                GrayboxZoneGeometry.BuildWilderness(
                    CreateChild(ScatterName, zoneRoot.transform).transform,
                    CreateChild(HazardsName, zoneRoot.transform).transform);
                ZoneRiskAuthor.CreateClaimFlag(zoneRoot.transform);
            }
            // The tutorial zone is deliberately bare: flat ground and boundary only.

            ZoneSpawnAuthor.Wire(manager, ZoneSpawnAuthor.CreateSpawnPoints(zoneRoot.transform, SpawnCountFor(layout)));
            ZoneMarkerAuthor.AddMarkers(zoneRoot.transform, layout);

            // Connects the scene to the server: spawn points, marker-driven world objects and
            // player placement. Added after the geometry so the bootstrap sees the finished scene.
            WorldBootstrapBaker.EnsureInActiveScene();
            WorldBootstrapBaker.RegisterMarkerPrefabs(manager);

            ZoneRiskAuthor.CreateRiskConfig(zoneRoot.transform, risk);
            ZonePortalAuthor.Create(
                zoneRoot.transform, portalName, serverId, targetHost, targetPort, targetServerId, targetZoneName,
                groundExtent);

            // --- INJECT NAVMESH BAKING HERE ---
            // No obstacle is placed here: the crafting station carries its own NavMeshObstacle
            // and is spawned at the marker, so the hole in the NavMesh is carved around the
            // station wherever it appears — a second copy baked into the scene would only
            // duplicate that hole.

            var surface = ZoneNavMeshBaking.Bake(zoneRoot.transform);
            ZoneNavMeshBaking.SaveAsset(surface, scenePath);
            // ----------------------------------

            ZoneBuildFiles.SaveScene(scene, scenePath);
        }

        /// <summary>Ground prefab per zone. Change one branch to give that zone its own surface.</summary>
        private static string GroundPrefabFor(ZoneLayout layout) => layout switch
        {
            ZoneLayout.SafeZone => GrayboxZoneGeometry.CobblestoneTilePrefab,
            ZoneLayout.Wilderness => GrayboxZoneGeometry.GrassTilePrefab,
            _ => GrayboxZoneGeometry.GrassTilePrefab,
        };

        private static float GroundExtentFor(ZoneLayout layout) => layout switch
        {
            ZoneLayout.SafeZone => SafeZoneGroundExtent,
            ZoneLayout.Wilderness => WildernessGroundExtent,
            _ => TutorialGroundExtent,
        };

        private static int SpawnCountFor(ZoneLayout layout) => layout switch
        {
            ZoneLayout.Wilderness => WildernessSpawnPointCount,
            ZoneLayout.Tutorial => TutorialSpawnPointCount,
            _ => SpawnPointCount,
        };

        // ---- Geometry ------------------------------------------------------

        // ---- Spawns, risk and portal ---------------------------------------

        // ---- Server-spawned world objects ----------------------------------

        /// <summary>
        /// Authors the markers the server turns into networked world objects on start. A node, a
        /// station and an enemy are all just a marker here: geometry the server instantiates and
        /// publishes, never a loose object in the scene. Heights sit each object on the ground
        /// (a cube's mesh is centred on its origin).
        /// </summary>
        // ---- Helpers -------------------------------------------------------

        private static GameObject CreateChild(string name, Transform parent)
            => ZoneSceneScaffold.CreateChild(name, parent);

        /// <summary>
        /// Instantiates a graybox kit prefab under <paramref name="parent"/>. The prefab's stored
        /// local Y is kept (the kit authors each piece so its base sits on Y = 0) unless an explicit
        /// <paramref name="y"/> is given for stacked pieces such as roof tiles.
        /// </summary>
        internal static GameObject PlaceKit(GameObject prefab, Transform parent, float x, float z, float yRotation = 0f, float? y = null)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            Transform transform = instance.transform;
            transform.localPosition = new Vector3(x, y ?? transform.localPosition.y, z);
            transform.localRotation = Quaternion.Euler(0f, yRotation, 0f);
            return instance;
        }

        internal static GameObject LoadPrefab(string path)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
                throw new InvalidOperationException($"Graybox kit prefab missing at '{path}'.");

            return prefab;
        }

    }
}
