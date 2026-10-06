using MMO.Client;
using NUnit.Framework;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MMO.Tests
{
    /// <summary>
    /// The rig the owning client looks through. Built here rather than in a scene so a fresh
    /// process, a baked scene and a test all describe the same camera.
    ///
    /// The default state is the point of most of these: an unclaimed rig has to be invisible to
    /// Cinemachine, because a client between joining and receiving its player object should not be
    /// looking at anybody.
    /// </summary>
    public sealed class LocalCameraRigFactoryTests
    {
        private readonly System.Collections.Generic.List<Object> _built = new();

        [TearDown]
        public void TearDown()
        {
            foreach (Object owned in _built)
            {
                if (owned != null)
                    Object.DestroyImmediate(owned);
            }

            _built.Clear();
        }

        private LocalCameraRig Create()
        {
            LocalCameraRig rig = LocalCameraRigFactory.Create();
            _built.Add(rig.gameObject);
            return rig;
        }

        #region Shape.
        [Test]
        public void Create_ProducesARootGameObjectWithAKnownName()
        {
            LocalCameraRig rig = Create();

            Assert.IsNull(rig.transform.parent, "A second rig would mean a second camera.");
            Assert.AreEqual("LocalCameraRig", rig.gameObject.name);
            Assert.AreEqual("LocalCameraRig", LocalCameraRigFactory.GameObjectName);
        }

        [Test]
        public void Create_LandsInTheActiveScene()
        {
            LocalCameraRig rig = Create();

            Assert.AreEqual(SceneManager.GetActiveScene(), rig.gameObject.scene);
        }

        [Test]
        public void Create_GivesTheRigAnOutputCamera()
        {
            LocalCameraRig rig = Create();

            Assert.IsNotNull(rig.OutputCamera);
            Assert.AreSame(rig.gameObject, rig.OutputCamera.gameObject,
                "The brain can only drive the camera it sits on.");
        }

        [Test]
        public void Create_GivesTheRigABrainToDriveIt()
        {
            LocalCameraRig rig = Create();

            Assert.IsNotNull(rig.Brain);
            Assert.AreSame(rig.gameObject, rig.Brain.gameObject);
        }

        [Test]
        public void Create_GivesTheRigAVirtualCamera()
        {
            LocalCameraRig rig = Create();

            Assert.IsNotNull(rig.VirtualCamera);
            Assert.AreSame(rig.gameObject, rig.VirtualCamera.gameObject);
        }

        [Test]
        public void Create_WiresTheRigToItsOwnComponents()
        {
            LocalCameraRig rig = Create();

            // Awake does not run for code-built objects in edit mode, so this proves the factory
            // binds explicitly rather than relying on play mode to fix it up.
            Assert.IsNotNull(rig.OutputCamera);
            Assert.IsNotNull(rig.Brain);
            Assert.IsNotNull(rig.VirtualCamera);
        }
        #endregion

        #region Cinemachine pipeline.
        [Test]
        public void Create_GivesTheVirtualCameraAStageThatFollows()
        {
            LocalCameraRig rig = Create();

            // Cinemachine 3 composes a virtual camera from one component per stage and adds none of
            // them for you. Without a Body stage the rig would never move off its last position.
            var follow = rig.gameObject.GetComponent<CinemachineFollow>();
            Assert.IsNotNull(follow, "A follow camera needs a Body stage or it holds still.");
            Assert.AreEqual(CinemachineCore.Stage.Body, follow.Stage);
        }

        [Test]
        public void Create_GivesTheVirtualCameraAStageThatAims()
        {
            LocalCameraRig rig = Create();

            // Without an Aim stage the rig could follow a player but never turn to face one.
            var aim = rig.gameObject.GetComponent<CinemachineRotationComposer>();
            Assert.IsNotNull(aim, "Looking at the player is half of what the issue asks for.");
            Assert.AreEqual(CinemachineCore.Stage.Aim, aim.Stage);
        }

        [Test]
        public void Create_HoldsTheCameraBehindAndAboveTheTarget()
        {
            LocalCameraRig rig = Create();

            var follow = rig.gameObject.GetComponent<CinemachineFollow>();
            Assert.AreEqual(
                new Vector3(0f, LocalCameraRigFactory.DefaultFollowHeight,
                    -LocalCameraRigFactory.DefaultFollowDistance),
                follow.FollowOffset);
        }

        [Test]
        public void Create_AimsAtTheMiddleOfThePlayerRatherThanItsFeet()
        {
            LocalCameraRig rig = Create();

            var aim = rig.gameObject.GetComponent<CinemachineRotationComposer>();
            Assert.AreEqual(LocalCameraRigFactory.DefaultAimHeight, aim.TargetOffset.y,
                "Aiming at the capsule's origin puts the player's head in the bottom of the frame.");
        }

        [Test]
        public void Create_DampsTheFollowRatherThanGlueingTheCameraToTheTarget()
        {
            LocalCameraRig rig = Create();

            var follow = rig.gameObject.GetComponent<CinemachineFollow>();
            Assert.Greater(follow.TrackerSettings.PositionDamping.x, 0f,
                "Zero damping makes every replicated jitter a camera jolt.");
            Assert.AreEqual(
                Vector3.one * LocalCameraRigFactory.FollowDampingSeconds,
                follow.TrackerSettings.PositionDamping);
        }

        [Test]
        public void Create_DampsTheAimSoTheViewDoesNotSnap()
        {
            LocalCameraRig rig = Create();

            var aim = rig.gameObject.GetComponent<CinemachineRotationComposer>();
            Assert.AreEqual(
                new Vector2(LocalCameraRigFactory.AimDampingSeconds,
                    LocalCameraRigFactory.AimDampingSeconds),
                aim.Damping);
        }

        [Test]
        public void Create_TellsTheBrainToEaseOntoTheVirtualCamera()
        {
            LocalCameraRig rig = Create();

            Assert.AreEqual(LocalCameraRigFactory.BlendSeconds, rig.Brain.DefaultBlend.Time);
            Assert.AreNotEqual(CinemachineBlendDefinition.Styles.Cut, rig.Brain.DefaultBlend.Style,
                "A cut teleports the view the instant a player claims the rig.");
        }
        #endregion

        #region The unclaimed default.
        [Test]
        public void Create_LeavesTheVirtualCameraSwitchedOff()
        {
            LocalCameraRig rig = Create();

            // An enabled virtual camera is a live camera to the brain. Anything enabled here would
            // be one frame of somebody else's shoulder before the owner claims it.
            Assert.IsFalse(rig.VirtualCamera.enabled);
        }

        [Test]
        public void Create_LeavesTheOutputCameraSwitchedOff()
        {
            LocalCameraRig rig = Create();

            Assert.IsFalse(rig.OutputCamera.enabled,
                "This is the static scene camera the issue asks to replace; it must not render until claimed.");
        }

        [Test]
        public void Create_LeavesTheRigUnclaimed()
        {
            LocalCameraRig rig = Create();

            Assert.IsFalse(rig.IsClaimed);
            Assert.IsNull(rig.FollowTarget);
            Assert.IsNull(rig.LookAtTarget);
        }

        [Test]
        public void Create_LeavesTheGameObjectActiveEvenThoughTheCameraIsOff()
        {
            LocalCameraRig rig = Create();

            // Ensure() finds rigs by searching active objects, so an inactive rig would be invisible
            // to it and every player would build another.
            Assert.IsTrue(rig.gameObject.activeSelf);
        }
        #endregion

        #region Find and Ensure.
        [Test]
        public void Find_MissesAnEmptyScene()
        {
            Assert.IsNull(LocalCameraRigFactory.Find());
        }

        [Test]
        public void Find_ReturnsTheRigInTheScene()
        {
            LocalCameraRig rig = Create();

            Assert.AreSame(rig, LocalCameraRigFactory.Find());
        }

        [Test]
        public void Ensure_BuildsARigWhenTheSceneHasNone()
        {
            LocalCameraRig rig = LocalCameraRigFactory.Ensure();
            _built.Add(rig.gameObject);

            Assert.IsNotNull(rig, "A client with no camera rig has nothing to look through.");
            Assert.AreEqual(1, Object.FindObjectsByType<LocalCameraRig>(FindObjectsSortMode.None).Length);
        }

        [Test]
        public void Ensure_ReusesTheRigAlreadyInTheScene()
        {
            LocalCameraRig existing = Create();

            LocalCameraRig rig = LocalCameraRigFactory.Ensure();

            Assert.AreSame(existing, rig, "Two rigs means two cameras fighting over the screen.");
        }

        [Test]
        public void Ensure_LeavesAnAlreadyClaimedRigAlone()
        {
            LocalCameraRig existing = Create();
            var player = new GameObject("Player");
            _built.Add(player);
            existing.Claim(player.transform);

            LocalCameraRigFactory.Ensure();

            Assert.IsTrue(existing.IsClaimed, "Ensure must never take the camera off its owner.");
            Assert.AreSame(player.transform, existing.FollowTarget);
        }

        [Test]
        public void Ensure_ReturnsAReadyToClaimRig()
        {
            LocalCameraRig rig = LocalCameraRigFactory.Ensure();
            _built.Add(rig.gameObject);

            Assert.IsFalse(rig.IsClaimed);
            Assert.IsNotNull(rig.VirtualCamera);
            Assert.IsNotNull(rig.OutputCamera);
        }

        [Test]
        public void Create_BuildsIndependentRigs()
        {
            // Not a supported way to make a second rig — but the factory must not hand the same
            // object out twice, or a test that cleans up after itself would corrupt another test.
            LocalCameraRig first = Create();
            LocalCameraRig second = Create();

            Assert.AreNotSame(first, second);
            Assert.AreNotSame(first.OutputCamera, second.OutputCamera);
        }
        #endregion
    }
}
