using MMO.Client;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MMO.Tests
{
    /// <summary>
    /// What claiming and releasing the rig does.
    ///
    /// This is the mechanics the <c>if (!IsOwner) return;</c> in
    /// <see cref="LocalPlayerCamera"/> protects: claim turns the camera on and points it at one
    /// object, release turns it off. Only one object may hold it, which is what makes "point it at
    /// my player" safe to write on a prefab every client receives.
    /// </summary>
    public sealed class LocalCameraRigTests
    {
        private LocalCameraRig _rig;
        private GameObject _firstPlayer;
        private GameObject _secondPlayer;

        [SetUp]
        public void SetUp()
        {
            _rig = LocalCameraRigFactory.Create();
            _firstPlayer = new GameObject("FirstPlayer");
            _secondPlayer = new GameObject("SecondPlayer");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_firstPlayer);
            Object.DestroyImmediate(_secondPlayer);
            Object.DestroyImmediate(_rig.gameObject);
        }

        #region Claiming.
        [Test]
        public void Claim_TurnsTheCameraOn()
        {
            _rig.Claim(_firstPlayer.transform);

            Assert.IsTrue(_rig.OutputCamera.enabled,
                "A rig nobody is looking through still has to render once it is claimed.");
        }

        [Test]
        public void Claim_MakesTheRigLiveToCinemachine()
        {
            _rig.Claim(_firstPlayer.transform);

            // The brain only ever considers enabled virtual cameras, so this flag is what actually
            // hands the rig to Cinemachine rather than leaving it inert.
            Assert.IsTrue(_rig.VirtualCamera.enabled);
            Assert.IsTrue(_rig.IsClaimed);
        }

        [Test]
        public void Claim_FollowsTheClaimingPlayer()
        {
            _rig.Claim(_firstPlayer.transform);

            Assert.AreSame(_firstPlayer.transform, _rig.FollowTarget);
        }

        [Test]
        public void Claim_LooksAtTheClaimingPlayer()
        {
            _rig.Claim(_firstPlayer.transform);

            // Follow and look-at are separate targets in Cinemachine; setting only one of them gives
            // a camera that trails a player but never faces it.
            Assert.AreSame(_firstPlayer.transform, _rig.LookAtTarget);
            Assert.IsTrue(_rig.VirtualCamera.Target.CustomLookAtTarget,
                "A look-at target is ignored unless the camera is told to use one separately.");
        }

        [Test]
        public void Claim_AimsTheLookAtSeparatelyFromTheFollowTarget()
        {
            var lookAt = new GameObject("LookAtPoint");
            try
            {
                _rig.VirtualCamera.Target.CustomLookAtTarget = true;
                _rig.VirtualCamera.Target.LookAtTarget = lookAt.transform;

                _rig.Claim(_firstPlayer.transform);

                Assert.AreSame(_firstPlayer.transform, _rig.FollowTarget);
                Assert.AreSame(lookAt.transform, _rig.LookAtTarget,
                    "A camera may follow one object and aim at another; claiming must not collapse that.");
            }
            finally
            {
                Object.DestroyImmediate(lookAt);
            }
        }

        [Test]
        public void Claim_IsIdempotent()
        {
            _rig.Claim(_firstPlayer.transform);
            _rig.Claim(_firstPlayer.transform);

            Assert.AreSame(_firstPlayer.transform, _rig.FollowTarget);
            Assert.IsTrue(_rig.IsClaimed);
        }

        [Test]
        public void Claim_MovesTheCameraWhenAnotherPlayerClaimsIt()
        {
            _rig.Claim(_firstPlayer.transform);

            _rig.Claim(_secondPlayer.transform);

            Assert.AreSame(_secondPlayer.transform, _rig.FollowTarget,
                "A respawn hands the camera to the new object; there is only ever one camera.");
        }

        [Test]
        public void Claim_IgnoresANullTarget()
        {
            _rig.Claim(null);

            Assert.IsFalse(_rig.IsClaimed, "Claiming nothing must not leave a live camera with no target.");
            Assert.IsNull(_rig.FollowTarget);
        }
        #endregion

        #region Releasing.
        [Test]
        public void Release_TakesTheVirtualCameraOutOfCinemachinesRunning()
        {
            _rig.Claim(_firstPlayer.transform);

            _rig.Release();

            Assert.IsFalse(_rig.VirtualCamera.enabled);
            Assert.IsFalse(_rig.IsClaimed);
        }

        [Test]
        public void Release_TurnsTheOutputCameraOff()
        {
            _rig.Claim(_firstPlayer.transform);

            _rig.Release();

            Assert.IsFalse(_rig.OutputCamera.enabled,
                "A rig that keeps rendering after its player despawned stares at a stale position.");
        }

        [Test]
        public void Release_DropsBothTargets()
        {
            _rig.Claim(_firstPlayer.transform);

            _rig.Release();

            Assert.IsNull(_rig.FollowTarget);
            Assert.IsNull(_rig.LookAtTarget,
                "A live virtual camera with a null aim target composes frames against nothing.");
        }

        [Test]
        public void Release_OnAnUnclaimedRigIsHarmless()
        {
            Assert.DoesNotThrow(() => _rig.Release());

            Assert.IsFalse(_rig.IsClaimed);
        }

        [Test]
        public void Claim_AfterRelease_WorksAgain()
        {
            _rig.Claim(_firstPlayer.transform);
            _rig.Release();

            _rig.Claim(_secondPlayer.transform);

            Assert.IsTrue(_rig.IsClaimed);
            Assert.AreSame(_secondPlayer.transform, _rig.FollowTarget);
        }
        #endregion

        #region Release leaves other players alone.
        [Test]
        public void APlayerThatIsNotLookingAtTheRigMustNotBeAbleToSwitchItOff()
        {
            _rig.Claim(_firstPlayer.transform);

            // What LocalPlayerCamera.ReleaseCamera does for a player it never claimed: compare, then
            // leave the rig alone. Without the comparison a joining player would blank the screen.
            if (_rig.FollowTarget == _secondPlayer.transform)
                _rig.Release();

            Assert.IsTrue(_rig.IsClaimed, "Somebody else's despawn must not switch the camera off.");
            Assert.AreSame(_firstPlayer.transform, _rig.FollowTarget);
        }

        [Test]
        public void ReleasingTheRigLeavesTheOtherPlayerUnclaimed()
        {
            _rig.Claim(_firstPlayer.transform);
            _rig.Release();

            if (_rig.FollowTarget == _secondPlayer.transform)
                _rig.Release();

            Assert.IsFalse(_rig.IsClaimed);
        }
        #endregion
    }
}
