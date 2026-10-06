using MMO.Client;
using MMO.Editor;
using NUnit.Framework;
using Unity.Cinemachine;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MMO.Tests
{
    /// <summary>
    /// The issue's editor-facing half: a Virtual Camera that follows and looks at the player prefab,
    /// authored into the scene so it can be tuned like any other object.
    ///
    /// Authoring one is optional — a fresh process builds its own rig on demand — so these tests are
    /// about the scene never ending up with two cameras or with a rig that is somehow live before a
    /// player has claimed it.
    /// </summary>
    public sealed class LocalCameraBakerTests
    {
        private Scene _scene;

        [SetUp]
        public void SetUp()
        {
            _scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject root in _scene.GetRootGameObjects())
                Object.DestroyImmediate(root);

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        private int CountRootsNamed(string name)
        {
            int count = 0;
            foreach (GameObject root in _scene.GetRootGameObjects())
            {
                if (root.name == name)
                    count++;
            }

            return count;
        }

        #region Baking.
        [Test]
        public void EnsureInActiveScene_PutsARigInTheScene()
        {
            LocalCameraRig rig = LocalCameraBaker.EnsureInActiveScene();

            Assert.IsNotNull(rig);
            Assert.IsTrue(LocalCameraBaker.TryFind(_scene, out _));
        }

        [Test]
        public void EnsureInActiveScene_PutsTheRigAtTheSceneRoot()
        {
            LocalCameraRig rig = LocalCameraBaker.EnsureInActiveScene();

            Assert.IsNull(rig.transform.parent);
            Assert.AreEqual(_scene, rig.gameObject.scene);
            Assert.AreEqual(1, _scene.GetRootGameObjects().Length);
        }

        [Test]
        public void EnsureInActiveScene_AddsExactlyOneRig()
        {
            LocalCameraBaker.EnsureInActiveScene();

            Assert.AreEqual(1, CountRootsNamed(LocalCameraRigFactory.GameObjectName));
        }

        [Test]
        public void EnsureInActiveScene_IsIdempotent()
        {
            LocalCameraRig first = LocalCameraBaker.EnsureInActiveScene();

            LocalCameraRig second = LocalCameraBaker.EnsureInActiveScene();

            Assert.AreSame(first, second, "A second rig means a second camera competing for the screen.");
            Assert.AreEqual(1, _scene.GetRootGameObjects().Length);
        }

        [Test]
        public void EnsureInActiveScene_LeavesTheRigUnclaimedAndDark()
        {
            LocalCameraRig rig = LocalCameraBaker.EnsureInActiveScene();

            Assert.IsFalse(rig.IsClaimed, "A rig saved into a scene must not be live before a player claims it.");
            Assert.IsFalse(rig.VirtualCamera.enabled);
            Assert.IsFalse(rig.OutputCamera.enabled);
        }

        [Test]
        public void EnsureInActiveScene_BakesTheSameRigTheRuntimeBuilds()
        {
            LocalCameraRig rig = LocalCameraBaker.EnsureInActiveScene();

            var follow = rig.gameObject.GetComponent<CinemachineFollow>();
            Assert.IsNotNull(follow);
            Assert.AreEqual(LocalCameraRigFactory.DefaultFollowOffset, follow.FollowOffset);
        }
        #endregion

        #region Find and validate.
        [Test]
        public void TryFind_FindsTheBakedRig()
        {
            LocalCameraRig baked = LocalCameraBaker.EnsureInActiveScene();

            Assert.IsTrue(LocalCameraBaker.TryFind(_scene, out LocalCameraRig found));
            Assert.AreSame(baked, found);
        }

        [Test]
        public void TryFind_MissesAnEmptyScene()
        {
            Assert.IsFalse(LocalCameraBaker.TryFind(_scene, out LocalCameraRig found));
            Assert.IsNull(found);
        }

        [Test]
        public void TryFind_MissesANameOnlyLookalike()
        {
            // Right name, no rig on it: a plain GameObject is somebody's placeholder, not a camera.
            new GameObject(LocalCameraRigFactory.GameObjectName);

            Assert.IsFalse(LocalCameraBaker.TryFind(_scene, out _));
        }

        [Test]
        public void TryFind_MissesANestedRig()
        {
            var parent = new GameObject("Parent");
            LocalCameraRig nested = LocalCameraRigFactory.Create();
            nested.transform.SetParent(parent.transform);

            Assert.IsFalse(LocalCameraBaker.TryFind(_scene, out _),
                "The rig has to be a root named LocalCameraRig so the rules stay discoverable.");
        }

        [Test]
        public void Validate_Fails_ForSceneWithoutARig()
        {
            Assert.IsFalse(LocalCameraBaker.Validate(_scene, out string error));
            StringAssert.Contains(LocalCameraRigFactory.GameObjectName, error);
            StringAssert.Contains(LocalCameraBaker.MenuPath, error);
        }

        [Test]
        public void Validate_Passes_AfterBaking()
        {
            LocalCameraBaker.EnsureInActiveScene();

            Assert.IsTrue(LocalCameraBaker.Validate(_scene, out string error), error);
            Assert.IsNull(error);
        }

        [Test]
        public void Validate_Fails_ForARigThatLostItsCinemachineParts()
        {
            LocalCameraRig rig = LocalCameraBaker.EnsureInActiveScene();
            Object.DestroyImmediate(rig.gameObject.GetComponent<CinemachineCamera>());

            Assert.IsFalse(LocalCameraBaker.Validate(_scene, out string error),
                "A rig that cannot drive a camera is worse than no rig: it looks correct and does nothing.");
            StringAssert.Contains(LocalCameraBaker.MenuPath, error);
        }
        #endregion
    }
}
