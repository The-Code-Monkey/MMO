using System.Collections.Generic;
using FishNet.Object;
using MMO.Editor;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MMO.Tests
{
    public sealed class NetworkManagerBuildRulesTests
    {
        private readonly List<Object> _scratch = new();

        [TearDown]
        public void TearDown()
        {
            foreach (Object owned in _scratch)
            {
                if (owned != null)
                    Object.DestroyImmediate(owned);
            }

            _scratch.Clear();
        }

        private NetworkObject NewPrefab(string name)
        {
            var go = new GameObject(name);
            _scratch.Add(go);
            return go.AddComponent<NetworkObject>();
        }

        [Test]
        public void FindViolations_FlagsEnabledSceneWithoutNetworkManager()
        {
            var scenes = new List<SceneBuildInfo>
            {
                new SceneBuildInfo("Assets/Scenes/A.unity", true, false),
                new SceneBuildInfo("Assets/Scenes/B.unity", true, true),
                new SceneBuildInfo("Assets/Scenes/C.unity", false, false)
            };

            IReadOnlyList<string> violations = NetworkManagerBuildRules.FindViolations(scenes);

            Assert.AreEqual(1, violations.Count);
            Assert.AreEqual("Assets/Scenes/A.unity", violations[0]);
        }

        [Test]
        public void FindViolations_NoViolationsWhenAllEnabledScenesHaveOne()
        {
            var scenes = new List<SceneBuildInfo>
            {
                new SceneBuildInfo("A", true, true),
                new SceneBuildInfo("B", false, false)
            };

            Assert.IsEmpty(NetworkManagerBuildRules.FindViolations(scenes));
        }

        #region Spawnable / player prefab wiring.
        [Test]
        public void FindPrefabViolations_FlagsANullEntryInTheSpawnableList()
        {
            // The exact shape of a scene baked against an older prefab file: the guid still
            // resolves but the fileID behind an entry no longer exists, so it loads as null and
            // FishNet logs "Prefab on id N is null" the moment the manager wakes up.
            NetworkObject alive = NewPrefab("Alive");
            var scenes = new List<SceneBuildInfo>
            {
                new SceneBuildInfo("Assets/Scenes/MainMenu.unity", true, true,
                    new NetworkObject[] { alive, null }, true)
            };

            IReadOnlyList<string> violations = NetworkManagerBuildRules.FindPrefabViolations(scenes);

            Assert.AreEqual(1, violations.Count);
            StringAssert.Contains("Assets/Scenes/MainMenu.unity", violations[0]);
            StringAssert.Contains("SpawnablePrefabs[1]", violations[0]);
        }

        [Test]
        public void FindPrefabViolations_FlagsAMissingSpawnableCollection()
        {
            var scenes = new List<SceneBuildInfo>
            {
                new SceneBuildInfo("Assets/Scenes/A.unity", true, true, null, true)
            };

            IReadOnlyList<string> violations = NetworkManagerBuildRules.FindPrefabViolations(scenes);

            Assert.AreEqual(1, violations.Count);
            StringAssert.Contains("Assets/Scenes/A.unity", violations[0]);
            StringAssert.Contains("SpawnablePrefabs", violations[0]);
        }

        [Test]
        public void FindPrefabViolations_FlagsAMissingPlayerPrefab()
        {
            NetworkObject alive = NewPrefab("Alive");
            var scenes = new List<SceneBuildInfo>
            {
                new SceneBuildInfo("Assets/Scenes/A.unity", true, true,
                    new NetworkObject[] { alive }, false)
            };

            IReadOnlyList<string> violations = NetworkManagerBuildRules.FindPrefabViolations(scenes);

            Assert.AreEqual(1, violations.Count);
            StringAssert.Contains("Assets/Scenes/A.unity", violations[0]);
            StringAssert.Contains("PlayerSpawner", violations[0]);
        }

        [Test]
        public void FindPrefabViolations_CollectsEveryProblemInOneScene()
        {
            var scenes = new List<SceneBuildInfo>
            {
                new SceneBuildInfo("Assets/Scenes/A.unity", true, true,
                    new NetworkObject[] { null }, false)
            };

            IReadOnlyList<string> violations = NetworkManagerBuildRules.FindPrefabViolations(scenes);

            // Both halves of the wiring are broken, and one message each is what tells the
            // baker what to run.
            Assert.AreEqual(2, violations.Count);
        }

        [Test]
        public void FindPrefabViolations_PassesWhenTheWiringIsClean()
        {
            NetworkObject alive = NewPrefab("Alive");
            var scenes = new List<SceneBuildInfo>
            {
                new SceneBuildInfo("Assets/Scenes/A.unity", true, true,
                    new NetworkObject[] { alive }, true),
                new SceneBuildInfo("Assets/Scenes/B.unity", true, true,
                    new NetworkObject[0], true)
            };

            Assert.IsEmpty(NetworkManagerBuildRules.FindPrefabViolations(scenes));
        }

        [Test]
        public void FindPrefabViolations_IgnoresDisabledScenes()
        {
            var scenes = new List<SceneBuildInfo>
            {
                new SceneBuildInfo("Assets/Scenes/A.unity", false, true, null, false)
            };

            Assert.IsEmpty(NetworkManagerBuildRules.FindPrefabViolations(scenes));
        }

        [Test]
        public void FindPrefabViolations_LeavesTheMissingNetworkManagerToTheOtherRule()
        {
            // No manager means no prefab data was gathered; FindViolations already owns that
            // message and this rule must not double-report the scene.
            var scenes = new List<SceneBuildInfo>
            {
                new SceneBuildInfo("Assets/Scenes/A.unity", true, false, null, false)
            };

            Assert.IsEmpty(NetworkManagerBuildRules.FindPrefabViolations(scenes));
        }
        #endregion
    }
}
