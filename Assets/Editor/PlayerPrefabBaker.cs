using System.IO;
using System.Text;
using FishNet.Component.Transforming;
using FishNet.Managing;
using FishNet.Managing.Object;
using FishNet.Object;
using MMO.Gameplay;
using MMO.Net;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MMO.Editor
{
    /// <summary>
    /// Turns the capsule built by <see cref="PlayerPrefabFactory"/> into a real prefab asset and
    /// deletes the scene copy it was built from — so the only player object in the project is the
    /// reusable template, never a scene instance.
    ///
    /// The asset is what the scene's NetworkManager registers as a spawnable and hands to FishNet's
    /// <see cref="FishNet.Component.Spawning.PlayerSpawner"/>.
    /// </summary>
    public static class PlayerPrefabBaker
    {
        public const string MenuPath = "Tools/MMO/Create Player Prefab";

        /// <summary>Folder the prefab lives in, relative to the project root.</summary>
        public const string PrefabFolder = "Assets/Prefabs";

        /// <summary>Where the saved prefab asset ends up.</summary>
        public const string PrefabAssetPath = PrefabFolder + "/" + PlayerPrefabFactory.PrefabName + ".prefab";

        /// <summary>
        /// The weapon a freshly baked player holds on spawn. Kept as an asset path so the editor
        /// can resolve it with the AssetDatabase and the tests can assert on it in the same units.
        /// </summary>
        public const string DefaultWeaponAssetPath = "Assets/Data/Weapons/Broadsword.asset";

        /// <summary>
        /// <see cref="PrefabAssetPath"/> resolved against the project root, for callers that need
        /// to touch the real file rather than the AssetDatabase.
        /// </summary>
        public static string AbsolutePrefabPath => Path.Combine(
            Path.GetDirectoryName(Application.dataPath) ?? ".",
            PrefabAssetPath);

        [MenuItem(MenuPath)]
        public static void BakePlayerPrefabMenu()
        {
            NetworkObject prefab = EnsurePlayerPrefab();
            if (prefab == null)
                return;

            Selection.activeObject = prefab.gameObject;
            EditorGUIUtility.PingObject(prefab.gameObject);
        }

        /// <summary>
        /// Returns the player prefab asset, creating it on the first call.
        /// Idempotent: a call over a prefab that already matches the factory reuses it. A prefab
        /// that has drifted from the factory — a component added or removed there since it was last
        /// baked — is rebuilt, because the asset is what every client is spawned from and FishNet
        /// wires SyncTypes by NetworkBehaviour index.
        /// </summary>
        public static NetworkObject EnsurePlayerPrefab()
        {
            NetworkObject existing = LoadPlayerPrefab();
            if (existing != null && PlayerPrefabDriftComparer.MatchesFactory(existing))
                return existing;

            PlayerPrefabAsset.EnsureFolder();

            // Built in the active scene, saved, then destroyed: the scene never keeps the copy.
            NetworkObject built = PlayerPrefabFactory.Create();
            try
            {
                PlayerPrefabAuthoring.EquipDefaultWeapon(built);
                PlayerPrefabAuthoring.MakeNetworkTransformServerAuthoritative(built);

                // SaveAsPrefabAsset overwrites any asset already at this path, keeping its GUID —
                // so every scene that references the player prefab stays valid across a rebake.
                GameObject asset = PrefabUtility.SaveAsPrefabAsset(built.gameObject, PrefabAssetPath);

                if (asset == null)
                {
                    Debug.LogError(
                        $"Could not save the player prefab to '{PrefabAssetPath}'. " +
                        "Player objects would have nothing to spawn from.");
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
        public static NetworkObject LoadPlayerPrefab()
            => PlayerPrefabAsset.Load();

        /// <summary>
        /// Points the scene's NetworkManager at the player prefab: registers it as a spawnable and
        /// gives the manager a PlayerSpawner, which is how FishNet is told to spawn a player object
        /// whenever a client connects.
        /// </summary>
        /// <returns>The player prefab that is now wired up, or null when it could not be loaded.</returns>
        public static NetworkObject WirePlayerPrefab(NetworkManager manager)
        {
            if (manager == null)
                return null;

            NetworkObject playerPrefab = EnsurePlayerPrefab();
            if (playerPrefab == null)
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

            PlayerPrefabFactory.Register(manager.SpawnablePrefabs, playerPrefab);
            PlayerPrefabFactory.Attach(manager, playerPrefab);
            return playerPrefab;
        }

        /// <summary>
        /// Bakes the prefab and wires it into the active scene's NetworkManager, creating that
        /// manager if the scene has none. The menu items that build a scene and a player object
        /// both end up here, so the scene cannot end up half wired.
        /// </summary>
        [MenuItem(MenuPath + " And Wire Scene", priority = 1)]
        public static void BakePlayerPrefabAndWireSceneMenu()
        {
            // CreateInActiveScene bakes the prefab and wires it in as part of building the manager.
            NetworkManagerSceneBaker.CreateInActiveScene(new MMOConnectionSettings());
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

            NetworkObject playerPrefab = LoadPlayerPrefab();
            if (playerPrefab == null)
                return;

            Selection.activeObject = playerPrefab.gameObject;
            EditorGUIUtility.PingObject(playerPrefab.gameObject);
        }

        /// <summary>
        /// Deletes the prefab asset. Used by the tests so a test run cannot leave generated assets
        /// behind, and by anyone who wants to re-bake from scratch.
        /// </summary>
        /// <returns>True when an asset was deleted.</returns>
        public static bool DeletePlayerPrefab()
        {
            // DeleteAsset answers false when there is nothing there, so there is no need to test
            // the filesystem first — and no way for the two to disagree.
            return PlayerPrefabAsset.Delete();
        }

    }
}
