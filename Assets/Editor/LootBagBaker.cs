using System.IO;
using FishNet.Managing;
using FishNet.Managing.Object;
using FishNet.Object;
using MMO.Net;
using UnityEditor;
using UnityEngine;

namespace MMO.Editor
{
    /// <summary>
    /// Turns the gold marker built by <see cref="LootBagPrefabFactory"/> into a real prefab asset
    /// and deletes the scene copy it was built from — so the only loot bag in the project is the
    /// reusable template, never a scene instance.
    ///
    /// The asset is what the scene's NetworkManager registers as a spawnable and what a full-loot
    /// death instantiates and server-spawns.
    /// </summary>
    public static class LootBagBaker
    {
        public const string MenuPath = "Tools/MMO/Create Loot Bag Prefab";

        /// <summary>Folder the prefab lives in, relative to the project root.</summary>
        public const string PrefabFolder = "Assets/Prefabs";

        /// <summary>Where the saved prefab asset ends up.</summary>
        public const string PrefabAssetPath = PrefabFolder + "/" + LootBagPrefabFactory.PrefabName + ".prefab";

        /// <summary>
        /// <see cref="PrefabAssetPath"/> resolved against the project root, for callers that need
        /// to touch the real file rather than the AssetDatabase.
        /// </summary>
        public static string AbsolutePrefabPath => Path.Combine(
            Path.GetDirectoryName(Application.dataPath) ?? ".",
            PrefabAssetPath);

        [MenuItem(MenuPath)]
        public static void BakeLootBagPrefabMenu()
        {
            NetworkObject prefab = EnsureLootBagPrefab();
            if (prefab == null)
                return;

            Selection.activeObject = prefab.gameObject;
            EditorGUIUtility.PingObject(prefab.gameObject);
        }

        /// <summary>
        /// Returns the loot bag prefab asset, creating it on the first call.
        /// Idempotent: a second call loads the asset that is already on disk.
        /// </summary>
        public static NetworkObject EnsureLootBagPrefab()
        {
            NetworkObject existing = LoadLootBagPrefab();
            if (existing != null)
                return existing;

            EnsureFolder();

            // Built in the active scene, saved, then destroyed: the scene never keeps the copy.
            NetworkObject built = LootBagPrefabFactory.Create();
            try
            {
                GameObject asset = PrefabUtility.SaveAsPrefabAsset(built.gameObject, PrefabAssetPath);
                if (asset == null)
                {
                    Debug.LogError(
                        $"Could not save the loot bag prefab to '{PrefabAssetPath}'. " +
                        "A full-loot death would have nothing to drop.");
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

        /// <summary>The saved prefab's NetworkObject, or null when it has not been baked yet.</summary>
        public static NetworkObject LoadLootBagPrefab()
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabAssetPath);
            return asset != null ? asset.GetComponent<NetworkObject>() : null;
        }

        /// <summary>
        /// Registers the loot bag as a spawnable on the scene's NetworkManager so every end agrees
        /// on its prefab id and a full-loot death can spawn it.
        /// </summary>
        /// <returns>The loot bag prefab that is now wired up, or null when it could not be loaded.</returns>
        public static NetworkObject WireLootBag(NetworkManager manager)
        {
            if (manager == null)
                return null;

            NetworkObject lootBag = EnsureLootBagPrefab();
            if (lootBag == null)
                return null;

            if (manager.SpawnablePrefabs == null)
            {
                var prefabs = ScriptableObject.CreateInstance<SinglePrefabObjects>();
                prefabs.name = NetworkManagerFactory.SpawnablePrefabsName;
                manager.SpawnablePrefabs = prefabs;
            }

            LootBagPrefabFactory.Register(manager.SpawnablePrefabs, lootBag);
            return lootBag;
        }

        /// <summary>
        /// Deletes the prefab asset. Used by the tests so a test run cannot leave generated assets
        /// behind, and by anyone who wants to re-bake from scratch.
        /// </summary>
        /// <returns>True when an asset was deleted.</returns>
        public static bool DeleteLootBagPrefab()
        {
            return AssetDatabase.DeleteAsset(PrefabAssetPath);
        }

        /// <summary>
        /// Creates the folder the prefab is saved into. <see cref="AssetDatabase.CreateFolder"/>
        /// rather than <c>Directory.CreateDirectory</c> so Unity generates the matching .meta.
        /// </summary>
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
