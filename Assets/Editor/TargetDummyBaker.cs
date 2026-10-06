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
    /// Turns the red capsule built by <see cref="TargetDummyPrefabFactory"/> into a real prefab
    /// asset and deletes the scene copy it was built from — so the only target dummy in the
    /// project is the reusable template, never a scene instance.
    ///
    /// The asset is what the scene's NetworkManager registers as a spawnable and hands to the
    /// <see cref="TargetDummySpawner"/>, which instantiates and server-spawns one when the server
    /// starts.
    /// </summary>
    public static class TargetDummyBaker
    {
        public const string MenuPath = "Tools/MMO/Create Target Dummy Prefab";

        /// <summary>Folder the prefab lives in, relative to the project root.</summary>
        public const string PrefabFolder = "Assets/Prefabs";

        /// <summary>Where the saved prefab asset ends up.</summary>
        public const string PrefabAssetPath = PrefabFolder + "/" + TargetDummyPrefabFactory.PrefabName + ".prefab";

        /// <summary>
        /// <see cref="PrefabAssetPath"/> resolved against the project root, for callers that need
        /// to touch the real file rather than the AssetDatabase.
        /// </summary>
        public static string AbsolutePrefabPath => Path.Combine(
            Path.GetDirectoryName(Application.dataPath) ?? ".",
            PrefabAssetPath);

        [MenuItem(MenuPath)]
        public static void BakeTargetDummyPrefabMenu()
        {
            NetworkObject prefab = EnsureTargetDummyPrefab();
            if (prefab == null)
                return;

            Selection.activeObject = prefab.gameObject;
            EditorGUIUtility.PingObject(prefab.gameObject);
        }

        /// <summary>
        /// Returns the target dummy prefab asset, creating it on the first call.
        /// Idempotent: a second call loads the asset that is already on disk.
        /// </summary>
        public static NetworkObject EnsureTargetDummyPrefab()
        {
            NetworkObject existing = LoadTargetDummyPrefab();
            if (existing != null)
                return existing;

            EnsureFolder();

            // Built in the active scene, saved, then destroyed: the scene never keeps the copy.
            NetworkObject built = TargetDummyPrefabFactory.Create();
            try
            {
                GameObject asset = PrefabUtility.SaveAsPrefabAsset(built.gameObject, PrefabAssetPath);
                if (asset == null)
                {
                    Debug.LogError(
                        $"Could not save the target dummy prefab to '{PrefabAssetPath}'. " +
                        "A server would have nothing to spawn an enemy from.");
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

        /// <summary>
        /// The saved prefab's NetworkObject, or null when it has not been baked yet.
        /// </summary>
        public static NetworkObject LoadTargetDummyPrefab()
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabAssetPath);
            return asset != null ? asset.GetComponent<NetworkObject>() : null;
        }

        /// <summary>
        /// Points the scene's NetworkManager at the target dummy: registers it as a spawnable and
        /// adds the spawner that instantiates and server-spawns it when the server starts.
        /// </summary>
        /// <returns>The target dummy prefab that is now wired up, or null when it could not be loaded.</returns>
        public static NetworkObject WireTargetDummy(NetworkManager manager)
        {
            if (manager == null)
                return null;

            NetworkObject targetDummy = EnsureTargetDummyPrefab();
            if (targetDummy == null)
                return null;

            // A NetworkManager that was never given a collection cannot spawn anything, and
            // NetworkManager.Awake would already have logged about it. Give it the same one the
            // factory builds so the wiring is identical either way.
            if (manager.SpawnablePrefabs == null)
            {
                var prefabs = ScriptableObject.CreateInstance<SinglePrefabObjects>();
                prefabs.name = NetworkManagerFactory.SpawnablePrefabsName;
                manager.SpawnablePrefabs = prefabs;
            }

            TargetDummyPrefabFactory.Register(manager.SpawnablePrefabs, targetDummy);
            TargetDummyPrefabFactory.Attach(manager, targetDummy);
            return targetDummy;
        }

        /// <summary>
        /// Deletes the prefab asset. Used by the tests so a test run cannot leave generated assets
        /// behind, and by anyone who wants to re-bake from scratch.
        /// </summary>
        /// <returns>True when an asset was deleted.</returns>
        public static bool DeleteTargetDummyPrefab()
        {
            // DeleteAsset answers false when there is nothing there, so there is no need to test
            // the filesystem first — and no way for the two to disagree.
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

            // Assets always exists, so the parent segment is always present.
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