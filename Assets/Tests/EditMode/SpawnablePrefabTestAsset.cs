using MMO.Editor;
using UnityEditor;

namespace MMO.Tests
{
    /// <summary>
    /// Tracks whether the player, target dummy and resource node prefab assets (and the folder
    /// holding them) were already part of the project when a test started, so teardown removes
    /// exactly what the test created and nothing the repository had checked in.
    ///
    /// Instantiate in <c>[SetUp]</c>, call <see cref="Cleanup"/> in <c>[TearDown]</c>.
    /// </summary>
    internal sealed class SpawnablePrefabTestAsset
    {
        private readonly bool _folderExisted;
        private readonly bool _playerExisted;
        private readonly bool _targetDummyExisted;
        private readonly bool _resourceNodeExisted;

        public SpawnablePrefabTestAsset()
        {
            _folderExisted = AssetDatabase.IsValidFolder(PlayerPrefabBaker.PrefabFolder);
            _playerExisted = AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(
                PlayerPrefabBaker.PrefabAssetPath) != null;
            _targetDummyExisted = AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(
                TargetDummyBaker.PrefabAssetPath) != null;
            _resourceNodeExisted = AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(
                ResourceNodeBaker.PrefabAssetPath) != null;
        }

        public void Cleanup()
        {
            if (!_playerExisted)
                PlayerPrefabBaker.DeletePlayerPrefab();

            if (!_targetDummyExisted)
                TargetDummyBaker.DeleteTargetDummyPrefab();

            if (!_resourceNodeExisted)
                ResourceNodeBaker.DeleteResourceNodePrefab();

            // A folder that already existed is someone else's; the folder this test had to create
            // is removed only once everything we put into it is gone.
            if (!_folderExisted && AssetDatabase.IsValidFolder(PlayerPrefabBaker.PrefabFolder))
                AssetDatabase.DeleteAsset(PlayerPrefabBaker.PrefabFolder);
        }
    }
}