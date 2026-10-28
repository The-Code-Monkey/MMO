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
        private const string WallPrefab = GrayboxFolder + "Wall_2x2.prefab";
        private const string TallWallPrefab = GrayboxFolder + "Wall_1x3.prefab";
        private const string RampPrefab = GrayboxFolder + "Ramp_1x2.prefab";
        private const string RedMaterial = "Assets/Materials/DevTextures/DevGrid_Red.mat";
        private const int ScatterSeed = 20261015;
        private const int ScatterClusters = 14;
        private const int ScatterReservedAttempts = 100;
        private const float ScatterInset = 3f;

        public static void BuildGround(Transform parent)
        {
            GameObject floor = ZoneSceneBaker.LoadPrefab(FloorTilePrefab);
            int tiles = Mathf.RoundToInt(ZoneSceneBaker.GroundExtent / ZoneSceneBaker.GroundTile);
            float start = -ZoneSceneBaker.GroundExtent / 2f + ZoneSceneBaker.GroundTile / 2f;

            for (int x = 0; x < tiles; x++)
                for (int z = 0; z < tiles; z++)
                    ZoneSceneBaker.PlaceKit(floor, parent, start + x * ZoneSceneBaker.GroundTile, start + z * ZoneSceneBaker.GroundTile);
        }

        public static void BuildBoundary(Transform parent)
        {
            GameObject wall = ZoneSceneBaker.LoadPrefab(WallPrefab);
            int segments = Mathf.RoundToInt(ZoneSceneBaker.GroundExtent / ZoneSceneBaker.GroundTile);
            float half = ZoneSceneBaker.GroundExtent / 2f;
            float start = -half + ZoneSceneBaker.GroundTile / 2f;

            // The two central segments of the +Z edge are left out: that 4 m opening is the
            // doorway the portal volume sits in.
            int doorA = segments / 2 - 1;
            int doorB = segments / 2;

            for (int i = 0; i < segments; i++)
            {
                float offset = start + i * ZoneSceneBaker.GroundTile;

                if (i != doorA && i != doorB)
                    ZoneSceneBaker.PlaceKit(wall, parent, offset, half);

                ZoneSceneBaker.PlaceKit(wall, parent, offset, -half);
                ZoneSceneBaker.PlaceKit(wall, parent, half, offset, 90f);
                ZoneSceneBaker.PlaceKit(wall, parent, -half, offset, 90f);
            }
        }

        public static void BuildTown(Transform parent)
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
            Transform root = ZoneSceneScaffold.CreateChild(name, parent).transform;
            GameObject wall = ZoneSceneBaker.LoadPrefab(TallWallPrefab);
            GameObject roof = ZoneSceneBaker.LoadPrefab(FloorTilePrefab);

            float halfWidth = width / 2f;
            float halfDepth = depth / 2f;

            // Walls running along X, on the ±Z edges. The -Z wall carries the doorway.
            for (int i = 0; i < width; i++)
            {
                float x = center.x - halfWidth + 0.5f + i;

                if (i != width / 2 - 1 && i != width / 2)
                    ZoneSceneBaker.PlaceKit(wall, root, x, center.z - halfDepth);

                ZoneSceneBaker.PlaceKit(wall, root, x, center.z + halfDepth);
            }

            // Walls running along Z, on the ±X edges (rotated 90°).
            for (int i = 0; i < depth; i++)
            {
                float z = center.z - halfDepth + 0.5f + i;
                ZoneSceneBaker.PlaceKit(wall, root, center.x - halfWidth, z, 90f);
                ZoneSceneBaker.PlaceKit(wall, root, center.x + halfWidth, z, 90f);
            }

            // Flat roof of 2×2 kit floors at the top of the walls.
            int tilesX = width / 2;
            int tilesZ = depth / 2;
            for (int ix = 0; ix < tilesX; ix++)
                for (int iz = 0; iz < tilesZ; iz++)
                    ZoneSceneBaker.PlaceKit(roof, root,
                        center.x - halfWidth + 1f + ix * ZoneSceneBaker.GroundTile,
                        center.z - halfDepth + 1f + iz * ZoneSceneBaker.GroundTile,
                        y: 3f);
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
