using System;
using MMO.Gameplay;
using UnityEditor;
using UnityEngine;

namespace MMO.Editor
{
    /// <summary>Builds the graybox ground, walls, town, scatter and hazards for a zone.</summary>
    public static class GrayboxZoneGeometry
    {
        private const string GrayboxFolder = "Assets/Prefabs/Graybox/";
        private const string FloorTilePrefab = GrayboxFolder + "Floor_2x2.prefab";
        public const string GrassTilePrefab = "Assets/Prefabs/Ground/Ground_Grass.prefab";
        public const string CobblestoneTilePrefab = "Assets/Prefabs/Ground/Ground_Cobblestone.prefab";
        private const string WallPrefab = GrayboxFolder + "Wall_2x2.prefab";
        private const string TallWallPrefab = GrayboxFolder + "Wall_1x3.prefab";
        private const string RampPrefab = GrayboxFolder + "Ramp_1x2.prefab";
        public const string WornCobblestonePathPrefab = "Assets/Prefabs/Ground/Path_Cobblestone_Worn.prefab";
        private const string WallMaterial = "Assets/Materials/Building/Wall_Generic.mat";
        private const string RoofMaterial = "Assets/Materials/Building/Roof_Tiles.mat";
        private const string RedMaterial = "Assets/Materials/DevTextures/DevGrid_Red.mat";
        private const int ScatterSeed = 20261015;
        private const int ScatterClusters = 14;
        private const int ScatterReservedAttempts = 100;
        private const float ScatterInset = 3f;

        public static void BuildGround(Transform parent, string tilePrefab = GrassTilePrefab,
            float extent = ZoneSceneBaker.GroundExtent)
        {
            GameObject floor = ZoneSceneBaker.LoadPrefab(tilePrefab);
            int tiles = Mathf.RoundToInt(extent / ZoneSceneBaker.GroundTile);
            float start = -extent / 2f + ZoneSceneBaker.GroundTile / 2f;

            for (int x = 0; x < tiles; x++)
                for (int z = 0; z < tiles; z++)
                    ZoneSceneBaker.PlaceKit(floor, parent, start + x * ZoneSceneBaker.GroundTile, start + z * ZoneSceneBaker.GroundTile);
        }

        public static void BuildBoundary(
            Transform parent,
            float extent = ZoneSceneBaker.GroundExtent,
            int portalCount = 1)
        {
            GameObject wall = ZoneSceneBaker.LoadPrefab(WallPrefab);
            int segments = Mathf.RoundToInt(extent / ZoneSceneBaker.GroundTile);
            float half = extent / 2f;
            float start = -half + ZoneSceneBaker.GroundTile / 2f;

            for (int i = 0; i < segments; i++)
            {
                float offset = start + i * ZoneSceneBaker.GroundTile;

                if (!IsPortalOpening(offset, half, half, portalCount, extent))
                    ZoneSceneBaker.PlaceKit(wall, parent, offset, half);

                if (!IsPortalOpening(offset, -half, half, portalCount, extent))
                    ZoneSceneBaker.PlaceKit(wall, parent, offset, -half);

                if (!IsPortalOpening(half, offset, half, portalCount, extent))
                    ZoneSceneBaker.PlaceKit(wall, parent, half, offset, 90f);

                if (!IsPortalOpening(-half, offset, half, portalCount, extent))
                    ZoneSceneBaker.PlaceKit(wall, parent, -half, offset, 90f);
            }
        }

        /// <summary>
        /// True when a wall segment centre falls inside a portal's 4 m doorway. The x/z meaning
        /// depends on which edge is being built: north/south walls vary in x, east/west in z.
        /// </summary>
        private static bool IsPortalOpening(
            float along, float edge, float half, int portalCount, float extent)
        {
            bool horizontalEdge = Mathf.Abs(Mathf.Abs(edge) - half) < 0.01f;
            for (int slot = 0; slot < portalCount; slot++)
            {
                Vector3 point = ZonePortalAuthor.EdgePointFor(slot, portalCount, extent);
                if (horizontalEdge)
                {
                    if (Mathf.Abs(edge - point.z) < 0.01f &&
                        Mathf.Abs(along - point.x) < ZoneSceneBaker.GroundTile)
                        return true;
                }
                else
                {
                    if (Mathf.Abs(edge - point.x) < 0.01f &&
                        Mathf.Abs(along - point.z) < ZoneSceneBaker.GroundTile)
                        return true;
                }
            }

            return false;
        }

        public static void BuildTown(Transform buildings, Transform streets)
        {
            BuildStreetGrid(streets);

            BuildBuilding(buildings, "TownHall", new Vector3(-24f, 0f, -24f), width: 8, depth: 8);
            BuildBuilding(buildings, "House_A", new Vector3(-24f, 0f, -8f), width: 8, depth: 6);
            BuildBuilding(buildings, "House_B", new Vector3(-24f, 0f, 8f), width: 6, depth: 8);
            BuildBuilding(buildings, "House_C", new Vector3(-24f, 0f, 24f), width: 8, depth: 8);
            BuildBuilding(buildings, "House_D", new Vector3(-8f, 0f, -24f), width: 8, depth: 6);
            BuildBuilding(buildings, "House_E", new Vector3(-8f, 0f, 24f), width: 6, depth: 8);
            BuildBuilding(buildings, "House_F", new Vector3(8f, 0f, -24f), width: 6, depth: 8);
            BuildBuilding(buildings, "House_G", new Vector3(8f, 0f, 24f), width: 8, depth: 6);
            BuildBuilding(buildings, "House_H", new Vector3(24f, 0f, -24f), width: 8, depth: 8);
            BuildBuilding(buildings, "House_I", new Vector3(24f, 0f, -8f), width: 6, depth: 8);
            BuildBuilding(buildings, "House_J", new Vector3(24f, 0f, 8f), width: 8, depth: 6);
            BuildBuilding(buildings, "House_K", new Vector3(24f, 0f, 24f), width: 8, depth: 8);
        }

        /// <summary>
        /// A 4x4 block grid on 16 m pitch. Roads are two worn-cobblestone tiles wide and the four
        /// central blocks are left open as the town square.
        /// </summary>
        private static void BuildStreetGrid(Transform parent)
        {
            GameObject path = ZoneSceneBaker.LoadPrefab(WornCobblestonePathPrefab);
            float[] roadLines = { -32f, -16f, 0f, 16f, 32f };
            const float edge = 35f;
            const float offset = 1f;
            const float y = 0.055f;

            for (int lineIndex = 0; lineIndex < roadLines.Length; lineIndex++)
            {
                float line = roadLines[lineIndex];

                for (float along = -edge; along <= edge; along += ZoneSceneBaker.GroundTile)
                {
                    ZoneSceneBaker.PlaceKit(path, parent, along, line - offset, y: y);
                    ZoneSceneBaker.PlaceKit(path, parent, along, line + offset, y: y);
                }
            }

            for (int lineIndex = 0; lineIndex < roadLines.Length; lineIndex++)
            {
                float line = roadLines[lineIndex];

                for (float along = -edge; along <= edge; along += ZoneSceneBaker.GroundTile)
                {
                    if (Mathf.Abs(along - NearestRoadLine(along, roadLines)) < ZoneSceneBaker.GroundTile)
                        continue;

                    ZoneSceneBaker.PlaceKit(path, parent, line - offset, along, y: y);
                    ZoneSceneBaker.PlaceKit(path, parent, line + offset, along, y: y);
                }
            }
        }

        private static float NearestRoadLine(float value, float[] roadLines)
        {
            float nearest = roadLines[0];
            float best = Mathf.Abs(value - nearest);
            for (int i = 1; i < roadLines.Length; i++)
            {
                float distance = Mathf.Abs(value - roadLines[i]);
                if (distance < best)
                {
                    best = distance;
                    nearest = roadLines[i];
                }
            }

            return nearest;
        }

        /// <summary>
        /// A simple square building: 3 m kit walls on all four sides with a 2 m door gap centred on
        /// the -Z wall, and a flat kit roof at 3 m. Everything is on the kit's 1 m grid.
        /// </summary>
        private static void BuildBuilding(Transform parent, string name, Vector3 center, int width, int depth)
        {
            Transform root = ZoneSceneScaffold.CreateChild(name, parent).transform;
            GameObject wall = ZoneSceneBaker.LoadPrefab(TallWallPrefab);
            GameObject roof = ZoneSceneBaker.LoadPrefab(FloorTilePrefab);
            Material wallMaterial = LoadMaterial(WallMaterial);
            Material roofMaterial = LoadMaterial(RoofMaterial);

            float halfWidth = width / 2f;
            float halfDepth = depth / 2f;

            // Walls running along X, on the ±Z edges. The -Z wall carries the doorway.
            for (int i = 0; i < width; i++)
            {
                float x = center.x - halfWidth + 0.5f + i;

                if (i != width / 2 - 1 && i != width / 2)
                    PlaceBuildingPiece(wall, root, x, center.z - halfDepth, wallMaterial);

                PlaceBuildingPiece(wall, root, x, center.z + halfDepth, wallMaterial);
            }

            // Walls running along Z, on the ±X edges (rotated 90°).
            for (int i = 0; i < depth; i++)
            {
                float z = center.z - halfDepth + 0.5f + i;
                PlaceBuildingPiece(wall, root, center.x - halfWidth, z, wallMaterial, 90f);
                PlaceBuildingPiece(wall, root, center.x + halfWidth, z, wallMaterial, 90f);
            }

            // Flat roof of 2×2 kit floors at the top of the walls.
            int tilesX = width / 2;
            int tilesZ = depth / 2;
            for (int ix = 0; ix < tilesX; ix++)
                for (int iz = 0; iz < tilesZ; iz++)
                    PlaceBuildingPiece(roof, root,
                        center.x - halfWidth + 1f + ix * ZoneSceneBaker.GroundTile,
                        center.z - halfDepth + 1f + iz * ZoneSceneBaker.GroundTile,
                        roofMaterial, y: 3f);
        }

        private static void PlaceBuildingPiece(
            GameObject prefab, Transform parent, float x, float z, Material material, float yRotation = 0f, float? y = null)
        {
            GameObject instance = ZoneSceneBaker.PlaceKit(prefab, parent, x, z, yRotation, y);
            var renderer = instance.GetComponent<MeshRenderer>();
            if (renderer == null)
                return;

            renderer.sharedMaterial = material;
            PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
        }

        private static Material LoadMaterial(string path)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
                throw new InvalidOperationException($"Building material missing at '{path}'. Run Tools > MMO > Rebuild Building Assets.");
            return material;
        }

        public static void BuildWilderness(Transform scatter, Transform hazards)
        {
            // Two obvious danger pockets. Their red-tinted kit floor makes the volume visible while
            // the ZoneRiskConfig makes it real to the death rules.
            BuildHazard(hazards, 1, new Vector2(12f, -12f), tiles: 3);
            BuildHazard(hazards, 2, new Vector2(-12f, 12f), tiles: 3);

            // Deterministic scatter: a fixed seed keeps the second baker run identical to the first,
            // which is what makes the baker idempotent.
            var random = new System.Random(ScatterSeed);
            GameObject wall = ZoneSceneBaker.LoadPrefab(WallPrefab);
            GameObject tallWall = ZoneSceneBaker.LoadPrefab(TallWallPrefab);
            GameObject ramp = ZoneSceneBaker.LoadPrefab(RampPrefab);

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
                    ZoneSceneBaker.PlaceKit(prefab, scatter, spot.x + jitterX, spot.y + jitterZ, yaw);
                }
            }
        }

        private static Vector2 PickScatterSpot(System.Random random)
        {
            float bound = ZoneSceneBaker.GroundExtent / 2f - ScatterInset;
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
            Transform root = ZoneSceneScaffold.CreateChild($"Hazard_{index}", parent).transform;
            root.localPosition = new Vector3(center.x, 0f, center.y);

            var collider = root.gameObject.AddComponent<BoxCollider>();
            collider.isTrigger = true;
            collider.center = new Vector3(0f, 1f, 0f);
            collider.size = new Vector3(tiles * ZoneSceneBaker.GroundTile, 2f, tiles * ZoneSceneBaker.GroundTile);

            var config = root.gameObject.AddComponent<ZoneRiskConfig>();
            config.SetTier(ZoneRiskTier.Red);

            GameObject marker = ZoneSceneBaker.LoadPrefab(FloorTilePrefab);
            Material red = AssetDatabase.LoadAssetAtPath<Material>(RedMaterial);
            float start = -tiles * ZoneSceneBaker.GroundTile / 2f + ZoneSceneBaker.GroundTile / 2f;

            for (int x = 0; x < tiles; x++)
            {
                for (int z = 0; z < tiles; z++)
                {
                    GameObject tile = ZoneSceneBaker.PlaceKit(marker, root, start + x * ZoneSceneBaker.GroundTile, start + z * ZoneSceneBaker.GroundTile);
                    var renderer = tile.GetComponent<MeshRenderer>();
                    if (red != null && renderer != null)
                    {
                        renderer.sharedMaterial = red;
                        PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                    }
                }
            }
        }
    }
}
