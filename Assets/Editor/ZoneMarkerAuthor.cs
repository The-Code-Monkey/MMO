using FishNet.Object;
using MMO.Banking;
using MMO.Crafting;
using MMO.Gameplay;
using MMO.Net;
using UnityEditor;
using UnityEngine;

namespace MMO.Editor
{
    /// <summary>Authors the markers the server turns into networked world objects.</summary>
    public static class ZoneMarkerAuthor
    {
        internal static void AddMarkers(Transform zoneRoot, ZoneSceneBaker.ZoneLayout layout)
        {
            Transform markers = ZoneSceneScaffold.CreateChild(ZoneSceneBaker.MarkersName, zoneRoot).transform;

            float nodeY = ResourceNodePrefabFactory.DefaultHeight / 2f;
            float stationY = CraftingStationPrefabFactory.DefaultHeight / 2f;
            float enemyY = TargetDummyPrefabFactory.DefaultHeight / 2f;
            float tellerY = BankTellerPrefabFactory.DefaultHeight / 2f;

            if (layout == ZoneSceneBaker.ZoneLayout.SafeZone)
            {
                AddBankTellerMarker(markers, 1, new Vector3(2f, tellerY, -4f), BankKind.Personal);
                AddBankTellerMarker(markers, 2, new Vector3(4f, tellerY, -4f), BankKind.Guild);
                AddStationMarker(markers, 3, new Vector3(10f, stationY, -10f), CraftingStationType.Furnace);
                return;
            }

            if (layout == ZoneSceneBaker.ZoneLayout.Wilderness)
            {
                AddNodeMarker(markers, 1, new Vector3(-8f, nodeY, -12f));
                AddNodeMarker(markers, 2, new Vector3(8f, nodeY, 12f));
                AddNodeMarker(markers, 3, new Vector3(-14f, nodeY, -6f));
                AddNodeMarker(markers, 4, new Vector3(6f, nodeY, -16f));
                AddNodeMarker(markers, 5, new Vector3(-2f, nodeY, -20f));
                AddNodeMarker(
                    markers,
                    6,
                    new Vector3(2f, nodeY, -20f),
                    AssetDatabase.LoadAssetAtPath<NetworkObject>(ResourceNodeAssetBaker.LargePrefabPath));
                AddEnemyMarker(markers, 1, new Vector3(5f, enemyY, -8f));
                AddEnemyMarker(markers, 2, new Vector3(-8f, enemyY, 8f));
                AddEnemyMarker(markers, 3, new Vector3(10f, enemyY, 2f));
                return;
            }

            if (layout == ZoneSceneBaker.ZoneLayout.TestZone)
            {
                AddNodeMarker(
                    markers,
                    1,
                    new Vector3(-8f, nodeY, -6f),
                    AssetDatabase.LoadAssetAtPath<NetworkObject>(ResourceNodeBaker.PrefabAssetPath));
                AddNodeMarker(
                    markers,
                    2,
                    new Vector3(8f, nodeY, -6f),
                    AssetDatabase.LoadAssetAtPath<NetworkObject>(ResourceNodeAssetBaker.LargePrefabPath));
                AddNodeMarker(
                    markers,
                    3,
                    new Vector3(-8f, nodeY, 6f),
                    AssetDatabase.LoadAssetAtPath<NetworkObject>(ResourceNodeAssetBaker.CopperPrefabPath));
                AddNodeMarker(
                    markers,
                    4,
                    new Vector3(8f, nodeY, 6f),
                    AssetDatabase.LoadAssetAtPath<NetworkObject>(ResourceNodeAssetBaker.CopperLargePrefabPath));
                AddNodeMarker(
                    markers,
                    5,
                    new Vector3(-8f, nodeY, 14f),
                    AssetDatabase.LoadAssetAtPath<NetworkObject>(ResourceNodeAssetBaker.TinPrefabPath));
                AddNodeMarker(
                    markers,
                    6,
                    new Vector3(8f, nodeY, 14f),
                    AssetDatabase.LoadAssetAtPath<NetworkObject>(ResourceNodeAssetBaker.TinLargePrefabPath));
                AddNodeMarker(
                    markers,
                    7,
                    new Vector3(-8f, nodeY, 24f),
                    AssetDatabase.LoadAssetAtPath<NetworkObject>(ResourceNodeAssetBaker.CobaltPrefabPath));
                AddNodeMarker(
                    markers,
                    8,
                    new Vector3(8f, nodeY, 24f),
                    AssetDatabase.LoadAssetAtPath<NetworkObject>(ResourceNodeAssetBaker.VeridiumPrefabPath));
                return;
            }

            AddNodeMarker(markers, 1, new Vector3(-3f, nodeY, 3f));
            AddEnemyMarker(markers, 1, new Vector3(0f, enemyY, 4f));
            AddStationMarker(markers, 1, new Vector3(4f, stationY, 4f), CraftingStationType.Furnace);
            AddBankTellerMarker(markers, 1, new Vector3(2f, tellerY, 4f), BankKind.Personal);
        }

        private static void AddNodeMarker(
            Transform parent,
            int index,
            Vector3 position,
            NetworkObject prefab = null)
        {
            var go = ZoneSceneScaffold.CreateChild($"ResourceNodeMarker_{index}", parent);
            go.transform.localPosition = position;
            var marker = go.AddComponent<ResourceNodeMarker>();
            if (prefab != null)
                marker.SetNodePrefab(prefab);
        }

        private static void AddStationMarker(Transform parent, int index, Vector3 position, CraftingStationType type)
        {
            var go = ZoneSceneScaffold.CreateChild($"CraftingStationMarker_{index}", parent);
            go.transform.localPosition = position;
            var marker = go.AddComponent<CraftingStationMarker>();
            marker.SetStationType(type);
        }

        private static void AddEnemyMarker(Transform parent, int index, Vector3 position)
        {
            var go = ZoneSceneScaffold.CreateChild($"EnemyMarker_{index}", parent);
            go.transform.localPosition = position;
            go.AddComponent<EnemyMarker>();
        }

        private static void AddBankTellerMarker(Transform parent, int index, Vector3 position, BankKind kind)
        {
            var go = ZoneSceneScaffold.CreateChild($"BankTellerMarker_{index}", parent);
            go.transform.localPosition = position;
            var marker = go.AddComponent<BankTellerMarker>();
            marker.SetKind(kind);
        }
    }
}
