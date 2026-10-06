using System.Collections;
using FishNet.Managing;
using FishNet.Object;
using MMO.Client;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MMO.Tests
{
    /// <summary>
    /// The issue's warning made into a test: joining as a second player must not hijack the first
    /// player's camera, because both prefabs carry the same script.
    ///
    /// Every player object exists on every client, so the only thing separating "my character" from
    /// "somebody else's" is FishNet's <c>IsOwner</c>. These tests run against a real host going
    /// through a real handshake, and they assert the state of the actual
    /// <see cref="Unity.Cinemachine.CinemachineCamera"/> rather than a mock, because a rig that is
    /// enabled when it should not be renders somebody else's shoulder without a single warning.
    ///
    /// FishNet allows one NetworkManager per process, so a genuine second client cannot join this
    /// test run. "A player this client does not own" is staged the way a remote player's copy of the
    /// prefab actually looks here: spawned and alive, with ownership handed away.
    /// </summary>
    public sealed class LocalPlayerCameraTests
    {
        private const int MaxWaitFrames = 300;

        private NetworkManager _manager;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _manager = NetworkManagerTestHelper.GetOrCreate();

            // Player objects from a previous test may have left a rig behind. Start from a scene
            // with none so "a rig was built" means this test caused it.
            DestroyAnyRig();

            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            NetworkManagerTestHelper.StopIfListening(_manager);
            DestroyAnyRig();

            yield return null;
        }

        private static void DestroyAnyRig()
        {
            foreach (LocalCameraRig rig in Object.FindObjectsByType<LocalCameraRig>(
                         FindObjectsSortMode.None))
            {
                if (rig != null)
                    Object.Destroy(rig.gameObject);
            }
        }

        /// <summary>Waits until <paramref name="condition"/> holds, so a frame count is not the test.</summary>
        private static IEnumerator WaitUntil(System.Func<bool> condition)
        {
            for (int i = 0; i < MaxWaitFrames && !condition(); i++)
                yield return null;
        }

        [UnityTest]
        public IEnumerator ConnectingClient_ReceivesTheScriptThatClaimsTheCamera()
        {
            NetworkManagerTestHelper.StartHost(_manager);
            yield return NetworkManagerTestHelper.WaitForHostAuthenticated(_manager);
            yield return NetworkManagerTestHelper.WaitForPlayerObject(_manager);

            NetworkObject player = NetworkManagerTestHelper.FindPlayerObject(_manager);
            Assert.IsNotNull(player, "A connecting client must receive a player object.");

            Assert.IsNotNull(player.GetComponent<LocalPlayerCamera>(),
                "The camera script travels with the prefab, so a remote client's copy carries it too.");
        }

        [UnityTest]
        public IEnumerator OwnedPlayer_BuildsTheRigWithoutAnySceneSetup()
        {
            // No scene has been baked: nothing but the prefab itself can bring a camera into being.
            Assert.IsNull(LocalCameraRigFactory.Find(), "Precondition: the scene starts with no rig.");

            NetworkManagerTestHelper.StartHost(_manager);
            yield return NetworkManagerTestHelper.WaitForHostAuthenticated(_manager);
            yield return NetworkManagerTestHelper.WaitForPlayerObject(_manager);

            Assert.IsNotNull(LocalCameraRigFactory.Find(),
                "The client needs something to look through, and nothing else is going to build it.");
        }

        [UnityTest]
        public IEnumerator OwnedPlayer_TakesTheCamera()
        {
            NetworkManagerTestHelper.StartHost(_manager);
            yield return NetworkManagerTestHelper.WaitForHostAuthenticated(_manager);
            yield return NetworkManagerTestHelper.WaitForPlayerObject(_manager);

            NetworkObject player = NetworkManagerTestHelper.FindPlayerObject(_manager);
            Assert.IsNotNull(player);

            LocalCameraRig rig = LocalCameraRigFactory.Find();
            Assert.IsNotNull(rig);
            Assert.IsTrue(rig.IsClaimed, "We own this player, so the camera is ours to drive.");
            Assert.IsTrue(rig.VirtualCamera.enabled,
                "An enabled virtual camera is what hands the rig to Cinemachine's brain.");
        }

        [UnityTest]
        public IEnumerator OwnedPlayer_HasTheCameraFollowItAndLookAtIt()
        {
            NetworkManagerTestHelper.StartHost(_manager);
            yield return NetworkManagerTestHelper.WaitForHostAuthenticated(_manager);
            yield return NetworkManagerTestHelper.WaitForPlayerObject(_manager);

            NetworkObject player = NetworkManagerTestHelper.FindPlayerObject(_manager);
            Assert.IsNotNull(player);

            LocalCameraRig rig = LocalCameraRigFactory.Find();
            Assert.AreSame(player.transform, rig.FollowTarget);
            Assert.AreSame(player.transform, rig.LookAtTarget,
                "Following without looking at is a camera pointed at the last place the player was.");
        }

        [UnityTest]
        public IEnumerator OwnedPlayer_EndsUpWithTheCameraBehindIt()
        {
            NetworkManagerTestHelper.StartHost(_manager);
            yield return NetworkManagerTestHelper.WaitForHostAuthenticated(_manager);
            yield return NetworkManagerTestHelper.WaitForPlayerObject(_manager);

            NetworkObject player = NetworkManagerTestHelper.FindPlayerObject(_manager);
            Assert.IsNotNull(player);

            LocalCameraRig rig = LocalCameraRigFactory.Find();
            Assert.IsNotNull(rig);

            // The brain eases onto the virtual camera over a blend, and the follow stage damps into
            // place, so the offset takes a moment to arrive.
            Vector3 expected = player.transform.position + LocalCameraRigFactory.DefaultFollowOffset;
            yield return WaitUntil(() =>
                Vector3.Distance(rig.OutputCamera.transform.position, expected) < 0.5f);

            Vector3 where = rig.OutputCamera.transform.position;
            Vector3 settled = where - player.transform.position;

            // At the issue's 70° the camera sits about 2m behind and 5.6m above the origin —
            // mostly overhead, but with enough "behind" left to read as a third-person view.
            Assert.Less(settled.z, -1f,
                $"Expected the camera behind the player, but it ended up at {where}.");
            Assert.Greater(settled.y, 4f,
                $"Expected the camera well above the player's origin, but it ended up at {where}.");
            Assert.IsTrue(rig.OutputCamera.enabled);
        }

        [UnityTest]
        public IEnumerator APlayerThisClientDoesNotOwn_NeverTakesTheCamera()
        {
            NetworkManagerTestHelper.StartHost(_manager);
            yield return NetworkManagerTestHelper.WaitForHostAuthenticated(_manager);
            yield return NetworkManagerTestHelper.WaitForPlayerObject(_manager);

            NetworkObject player = NetworkManagerTestHelper.FindPlayerObject(_manager);
            Assert.IsNotNull(player);

            var behaviour = player.GetComponent<LocalPlayerCamera>();
            Assert.IsTrue(behaviour.TryClaimCamera(), "Precondition: an owned player does get the camera.");

            // Hand ownership away on the server. This is the state a remote player's copy of the
            // prefab is in on this client: spawned, alive, and not ours.
            player.RemoveOwnership();
            yield return WaitUntil(() => !behaviour.IsOwner);
            Assert.IsFalse(behaviour.IsOwner, "Precondition: this player is no longer ours.");

            Assert.IsFalse(behaviour.TryClaimCamera(),
                "The guard is the whole feature: a player this client does not own must never move the camera.");

            LocalCameraRig rig = LocalCameraRigFactory.Find();
            Assert.IsNotNull(rig);
            Assert.IsFalse(rig.IsClaimed, "Losing ownership has to hand the camera back.");
            Assert.IsNull(rig.FollowTarget);
        }

        [UnityTest]
        public IEnumerator LosingOwnership_StopsTheRigFollowingAStalePlayer()
        {
            NetworkManagerTestHelper.StartHost(_manager);
            yield return NetworkManagerTestHelper.WaitForHostAuthenticated(_manager);
            yield return NetworkManagerTestHelper.WaitForPlayerObject(_manager);

            NetworkObject player = NetworkManagerTestHelper.FindPlayerObject(_manager);
            Assert.IsNotNull(player);

            LocalCameraRig rig = LocalCameraRigFactory.Find();
            Assert.IsNotNull(rig);
            Assert.AreSame(player.transform, rig.FollowTarget);

            player.RemoveOwnership();
            yield return WaitUntil(() => rig.FollowTarget != player.transform);

            Assert.IsNull(rig.FollowTarget,
                "The camera must not stay glued to a player this client no longer owns.");
        }

        [UnityTest]
        public IEnumerator DespawningTheOwnedPlayer_HandsTheCameraBack()
        {
            NetworkManagerTestHelper.StartHost(_manager);
            yield return NetworkManagerTestHelper.WaitForHostAuthenticated(_manager);
            yield return NetworkManagerTestHelper.WaitForPlayerObject(_manager);

            NetworkObject player = NetworkManagerTestHelper.FindPlayerObject(_manager);
            Assert.IsNotNull(player);

            LocalCameraRig rig = LocalCameraRigFactory.Find();
            Assert.IsNotNull(rig);
            Assert.IsTrue(rig.IsClaimed, "Precondition: we own this player, so the camera is live.");

            NetworkManagerTestHelper.StopIfListening(_manager);
            yield return WaitUntil(() => rig.FollowTarget == null);

            Assert.IsFalse(rig.IsClaimed, "A disconnected client must not keep the camera switched on.");
            Assert.IsNull(rig.FollowTarget);
            Assert.IsTrue(player == null, "Unity's == is true for a destroyed object; the player is gone.");
        }
    }
}
