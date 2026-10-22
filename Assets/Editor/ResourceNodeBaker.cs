using System.IO;
using FishNet.Object;
using MMO.Net;
using UnityEditor;
using UnityEngine;

namespace MMO.Editor
{
    /// <summary>
    /// Turns the vein built by <see cref="ResourceNodePrefabFactory"/> into a real prefab asset
    /// and deletes the scene copy it was built from — so the only resource node in the project
    /// is the reusable template, never a scene instance.
    ///
    /// The asset is what a level places iron veins from and what a resource spawner (or a test)
    /// instantiates and hands to <see cref="ResourceNodePrefabFactory.Register"/>; the node's
    /// kind, yield, tool requirement, interval and respawn live on the
    /// <see cref="ResourceDefinition"/> the template carries, so re-baking is how a designer
    /// retunes a node.
    /// </summary>
    public static class ResourceNodeBaker
    {
        public const string MenuPath = "Tools/MMO/Create Resource Node Prefab";

        /// <summary>Folder the prefab lives in, relative to the project root.</summary>
        public const string PrefabFolder = "Assets/Prefabs";

        /// <summary>Where the saved prefab asset ends up.</summary>
        public const string PrefabAssetPath = PrefabFolder + "/" + ResourceNodePrefabFactory.PrefabName + ".prefab";

        /// <summary>
        /// <see cref="PrefabAssetPath"/> resolved against the project root, for callers that need
        /// to touch the real file rather than the AssetDatabase.
        /// </summary>
        public static string AbsolutePrefabPath => Path.Combine(
            Path.GetDirectoryName(Application.dataPath) ?? ".",
            PrefabAssetPath);

        [MenuItem(MenuPath)]
        public static void BakeResourceNodePrefabMenu()
        {
            NetworkObject prefab = EnsureResourceNodePrefab();
            if (prefab == null)
                return;

            Selection.activeObject = prefab.gameObject;
            EditorGUIUtility.PingObject(prefab.gameObject);
        }

        /// <summary>
        /// Returns the resource node prefab asset, creating it on the first call.
        /// Idempotent: a second call loads the asset that is already on disk.
        /// </summary>
        public static NetworkObject EnsureResourceNodePrefab()
        {
            NetworkObject existing = LoadResourceNodePrefab();
            if (existing != null)
                return existing;

            EnsureFolder();

            // Built in the active scene, saved, then destroyed: the scene never keeps the copy.
            NetworkObject built = ResourceNodePrefabFactory.Create();
            try
            {
                GameObject asset = PrefabUtility.SaveAsPrefabAsset(built.gameObject, PrefabAssetPath);
                if (asset == null)
                {
                    Debug.LogError(
                        $"Could not save the resource node prefab to '{PrefabAssetPath}'. " +
                        "There would be no template a level could scatter veins from.");
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
        public static NetworkObject LoadResourceNodePrefab()
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabAssetPath);
            return asset != null ? asset.GetComponent<NetworkObject>() : null;
        }

        /// <summary>
        /// Deletes the prefab asset. Used by the tests so a test run cannot leave generated assets
        /// behind, and by anyone who wants to re-bake from scratch.
        /// </summary>
        /// <returns>True when an asset was deleted.</returns>
        public static bool DeleteResourceNodePrefab()
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
