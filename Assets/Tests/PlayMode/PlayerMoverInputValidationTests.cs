using System.Collections;
using FishNet.Managing;
using FishNet.Object;
using MMO.Gameplay;
using MMO.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MMO.Tests
{
    /// <summary>
    /// Tests that PlayerMover integrates input on the server and rejects implausible positions.
    /// Uses the same host-only pattern as the existing PlayerMoverPlayModeTests.
    /// </summary>
    public sealed class PlayerMoverInputValidationTests
    {
        private NetworkManager _manager;
        private NetworkObject _player;
        private PlayerMover _mover;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _manager = NetworkManagerTestHelper.GetOrCreate();

            _player = null;
            _mover = null;

            // NetworkManager.Awake runs on the frame the object activates and the join gate
            // subscribes in Start, so give the manager a frame before anything connects.
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            NetworkManagerTestHelper.StopIfListening(_manager);

            // Stopping is asynchronous; let it finish so the next test starts genuinely offline.
            yield return null;
        }

        [UnityTest]
        public IEnumerator ApplyInput_MovesThePlayerWithinTheSpeedBudget()
        {
            yield return StartHostWithPlayer();

            Vector3 origin = _player.transform.position;

            _mover.ApplyInput(new Vector2(1f, 0f), 0.5);

            float travelled = Vector3.Distance(origin, _player.transform.position);
            Assert.AreEqual(_mover.MaxSpeed * 0.5f, travelled, 0.05f,
                "The server must integrate input at its own speed, not at whatever the client hoped for.");
        }

        [UnityTest]
        public IEnumerator DiagonalInput_DoesNotOutrunAStraightLine()
        {
            yield return StartHostWithPlayer();

            Vector3 origin = _player.transform.position;

            // A full diagonal (1, 1) has length √2; normalising it keeps the step equal to a
            // straight push instead of letting a diagonal grant the client extra distance.
            _mover.ApplyInput(new Vector2(1f, 1f), 0.5f);

            float travelled = Vector3.Distance(origin, _player.transform.position);
            Assert.AreEqual(_mover.MaxSpeed * 0.5f, travelled, 0.05f,
                "Diagonal input must be normalised so it does not outrun a straight line.");
        }

        [UnityTest]
        public IEnumerator ApplyReportedPosition_AcceptsPlausiblePosition()
        {
            yield return StartHostWithPlayer();

            Vector3 origin = _player.transform.position;
            var target = origin + Vector3.forward * (_mover.MaxSpeed * 0.1f * 0.5f); // well within budget

            bool accepted = _mover.ApplyReportedPosition(target, 0.1);

            Assert.IsTrue(accepted, "A position reachable within the budget must be accepted.");
            Assert.AreEqual(target, _player.transform.position);
            Assert.AreEqual(target, _mover.LastAuthoritativePosition);
        }

        [UnityTest]
        public IEnumerator ApplyReportedPosition_RejectsImplausibleTeleport()
        {
            yield return StartHostWithPlayer();

            Vector3 origin = _player.transform.position;

            // A thousand metres in a single 16 ms tick is a teleport by any measure.
            Vector3 cheat = origin + Vector3.right * 1000f;

            bool accepted = _mover.ApplyReportedPosition(cheat, 0.016);

            Assert.IsFalse(accepted, "An impossible delta must be refused.");
            Assert.AreEqual(origin, _player.transform.position,
                "A refused position must leave the player exactly where the server last saw them.");
            Assert.AreEqual(origin, _mover.LastAuthoritativePosition,
                "A refused report must not advance the authoritative baseline.");
        }

        [UnityTest]
        public IEnumerator Teleport_UpdatesTheAuthoritativeBaseline()
        {
            yield return StartHostWithPlayer();

            Vector3 destination = _player.transform.position + Vector3.right * 500f;

            _mover.Teleport(destination);

            Assert.AreEqual(destination, _player.transform.position);
            Assert.AreEqual(destination, _mover.LastAuthoritativePosition,
                "A privileged reposition moves the baseline; later small reports are judged from here.");

            // From the new baseline a modest step is plausible again — the snap-back did not leave
            // the mover stuck validating against a stale position.
            Assert.IsTrue(_mover.ApplyReportedPosition(destination + Vector3.right * 0.1f, 0.1f));
        }

        [UnityTest]
        public IEnumerator PlayerPrefab_CarriesTheMoverAndInputDriver()
        {
            yield return StartHostWithPlayer();

            Assert.IsNotNull(_mover, "PlayerPrefabFactory must put PlayerMover on the player.");
            Assert.IsTrue(_player.IsServer, "The host owns the server half of the player object.");

            var inputDriver = _player.GetComponent<MMO.Input.PlayerMoverInput>();
            Assert.IsNotNull(inputDriver, "PlayerPrefabFactory must put PlayerMoverInput on the player.");
        }

        private IEnumerator StartHostWithPlayer()
        {
            NetworkManagerTestHelper.StartHost(_manager);
            yield return NetworkManagerTestHelper.WaitForHostAuthenticated(_manager);
            yield return NetworkManagerTestHelper.WaitForPlayerObject(_manager);

            _player = NetworkManagerTestHelper.FindPlayerObject(_manager);
            Assert.IsNotNull(_player, "A connecting client must receive a player object.");

            _mover = _player.GetComponent<PlayerMover>();
            Assert.IsNotNull(_mover, "PlayerPrefabFactory must put PlayerMover on the player.");
        }
    }
}