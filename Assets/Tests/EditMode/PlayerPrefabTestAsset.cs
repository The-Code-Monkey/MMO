using MMO.Editor;
using UnityEditor;

namespace MMO.Tests
{
    /// <summary>
    /// Tracks whether the player prefab asset (and the folder holding it) were already part of the
    /// project when a test started, so teardown removes exactly what the test created and nothing
    /// the repository had checked in.
    ///
    /// Instantiate in <c>[SetUp]</c>, call <see cref="Cleanup"/> in <c>[TearDown]</c>.
    /// </summary>
    internal sealed class PlayerPrefabTestAsset
    {
        private readonly bool _folderExisted;
        private readonly bool _assetExisted;

        public PlayerPrefabTestAsset()
        {
            _folderExisted = AssetDatabase.IsValidFolder(PlayerPrefabBaker.PrefabFolder);
            _assetExisted = AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(
                PlayerPrefabBaker.PrefabAssetPath) != null;
        }

        public void Cleanup()
        {
            if (!_assetExisted)
                PlayerPrefabBaker.DeletePlayerPrefab();

            if (!_folderExisted && AssetDatabase.IsValidFolder(PlayerPrefabBaker.PrefabFolder))
                AssetDatabase.DeleteAsset(PlayerPrefabBaker.PrefabFolder);
        }
    }
}