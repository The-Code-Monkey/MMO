using MMO.Editor;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MMO.Tests
{
    /// <summary>
    /// The camera rig half of the editor tooling: the one <see cref="PersistentCameraController"/>
    /// a client looks through, authored into a zone scene so it can be tuned like any other object.
    ///
    /// The tests are about the scene never ending up with two rigs or with a rig that is missing
    /// the child camera it is supposed to drive — both failures look fine in the hierarchy and
    /// then leave the client with nothing to look through.
    /// </summary>
    // These tests bake assets and re-import them per test, which costs seconds each; the
    // Unity Tests workflow shards them by category so they run in parallel with the suite.
    [Category("Bake")]
    [Category("BakeScene")]
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
            PersistentCameraController rig = LocalCameraBaker.EnsureInActiveScene();

            Assert.IsNotNull(rig);
            Assert.IsTrue(LocalCameraBaker.TryFind(_scene, out _));
        }

        [Test]
        public void EnsureInActiveScene_PutsTheRigAtTheSceneRoot()
        {
            PersistentCameraController rig = LocalCameraBaker.EnsureInActiveScene();

            Assert.IsNull(rig.transform.parent);
            Assert.AreEqual(_scene, rig.gameObject.scene);
        }

        [Test]
        public void EnsureInActiveScene_AddsExactlyOneRig()
        {
            LocalCameraBaker.EnsureInActiveScene();

            Assert.AreEqual(1, CountRootsNamed(LocalCameraBaker.GameObjectName));
        }

        [Test]
        public void EnsureInActiveScene_IsIdempotent()
        {
            PersistentCameraController first = LocalCameraBaker.EnsureInActiveScene();
            PersistentCameraController second = LocalCameraBaker.EnsureInActiveScene();

            Assert.AreSame(first, second,
                "A second bake must reuse the rig rather than adding a second camera.");
        }

        [Test]
        public void EnsureInActiveScene_GivesTheRigACameraTaggedMainCamera()
        {
            PersistentCameraController rig = LocalCameraBaker.EnsureInActiveScene();

            // The world pointer raycasts through Camera.main; an untagged camera means clicks
            // silently do nothing.
            Camera camera = rig.GetComponentInChildren<Camera>();
            Assert.IsNotNull(camera, "A rig with no camera has nothing to render through.");
            Assert.AreEqual("MainCamera", camera.gameObject.tag,
                "Click-to-move finds the camera by tag.");
        }

        [Test]
        public void EnsureInActiveScene_BakesTheBakedPrefabNotAHollowCopy()
        {
            PersistentCameraController rig = LocalCameraBaker.EnsureInActiveScene();

            // The prefab is the tuning surface: a bake that clones-and-unprefabs would let the
            // scene drift from the asset the zone baker ships.
            Assert.IsNotNull(UnityEditor.PrefabUtility.GetCorrespondingObjectFromSource(rig.gameObject),
                "The baked rig must remain an instance of the CameraRig prefab.");
        }

        #endregion

        #region Finding.

        [Test]
        public void TryFind_IsFalseInAnEmptyScene()
        {
            Assert.IsFalse(LocalCameraBaker.TryFind(_scene, out PersistentCameraController rig));
            Assert.IsNull(rig);
        }

        [Test]
        public void TryFind_IgnoresASameNamedObjectWithoutTheController()
        {
            var decoy = new GameObject(LocalCameraBaker.GameObjectName);

            Assert.IsFalse(LocalCameraBaker.TryFind(_scene, out _),
                "A plain GameObject with the right name is not a camera rig.");
            Object.DestroyImmediate(decoy);
        }

        #endregion

        #region Validating.

        [Test]
        public void Validate_FailsInAnEmptyScene()
        {
            Assert.IsFalse(LocalCameraBaker.Validate(_scene, out string error));
            Assert.IsNotEmpty(error);
        }

        [Test]
        public void Validate_PassesAfterABake()
        {
            LocalCameraBaker.EnsureInActiveScene();

            Assert.IsTrue(LocalCameraBaker.Validate(_scene, out string error), error);
        }

        [Test]
        public void Validate_FailsWhenTheRigLostItsCamera()
        {
            PersistentCameraController rig = LocalCameraBaker.EnsureInActiveScene();
            foreach (Camera camera in rig.GetComponentsInChildren<Camera>(true))
                Object.DestroyImmediate(camera.gameObject);

            Assert.IsFalse(LocalCameraBaker.Validate(_scene, out string error),
                "A rig whose camera was deleted renders nothing and should not validate.");
        }

        [Test]
        public void Validate_FailsWhenTheCameraIsNotTaggedMainCamera()
        {
            PersistentCameraController rig = LocalCameraBaker.EnsureInActiveScene();
            Camera camera = rig.GetComponentInChildren<Camera>();
            camera.gameObject.tag = "Untagged";

            Assert.IsFalse(LocalCameraBaker.Validate(_scene, out string error),
                "An untagged camera leaves click-to-move raycasting from nothing.");
        }

        #endregion
    }
}
