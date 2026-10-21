using System.IO;
using FishNet.Managing;
using FishNet.Managing.Object;
using FishNet.Object;
using MMO.Net;
using UnityEditor;
using UnityEngine;

namespace MMO.Editor
{
    public static class CraftingStationBaker
    {
        public const string MenuPath = "Tools/MMO/Create Crafting Station Prefab";
        public const string PrefabFolder = "Assets/Prefabs";
        public const string PrefabAssetPath = PrefabFolder + "/" + CraftingStationPrefabFactory.PrefabName + ".prefab";

        public static string AbsolutePrefabPath => Path.Combine(
            Path.GetDirectoryName(Application.dataPath) ?? ".",
            PrefabAssetPath);

        [MenuItem(MenuPath)]
        public static void BakeCraftingStationPrefabMenu()
        {
            NetworkObject prefab = EnsureCraftingStationPrefab();
            if (prefab == null)
                return;

            Selection.activeObject = prefab.gameObject;
            EditorGUIUtility.PingObject(prefab.gameObject);
        }

        public static NetworkObject EnsureCraftingStationPrefab()
        {
            EnsureFolder();

            NetworkObject built = CraftingStationPrefabFactory.Create();
            try
            {
                GameObject asset = PrefabUtility.SaveAsPrefabAsset(built.gameObject, PrefabAssetPath);
                if (asset == null)
                {
                    Debug.LogError(
                        $"Could not save the crafting station prefab to '{PrefabAssetPath}'. " +
                        "A server would have nothing to spawn a crafting station from.");
                    return null;
                }

                AssetDatabase.SaveAssets();
                return asset.GetComponent<NetworkObject>();
            }
            finally
            {
                Object.DestroyImmediate(built.gameObject);
            }
        }

        public static NetworkObject WireCraftingStation(NetworkManager manager)
        {
            if (manager == null)
                return null;

            NetworkObject craftingStation = EnsureCraftingStationPrefab();
            if (craftingStation == null)
                return null;

            if (manager.SpawnablePrefabs == null)
            {
                var prefabs = ScriptableObject.CreateInstance<SinglePrefabObjects>();
                prefabs.name = NetworkManagerFactory.SpawnablePrefabsName;
                manager.SpawnablePrefabs = prefabs;
            }

            CraftingStationPrefabFactory.Register(manager.SpawnablePrefabs, craftingStation);
            CraftingStationPrefabFactory.Attach(manager, craftingStation);
            return craftingStation;
        }

        public static bool DeleteCraftingStationPrefab()
        {
            return AssetDatabase.DeleteAsset(PrefabAssetPath);
        }

        private static void EnsureFolder()
        {
            if (AssetDatabase.IsValidFolder(PrefabFolder))
                return;

            string parent = Path.GetDirectoryName(PrefabFolder)?.Replace('\\', '/');
            string leaf = Path.GetFileName(PrefabFolder);

            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(leaf))
                return;

            if (!AssetDatabase.IsValidFolder(parent))
                AssetDatabase.CreateFolder("Assets", leaf);
            else
                AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
