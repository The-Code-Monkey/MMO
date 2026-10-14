using System;
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
    /// Proves the mover is the server's to write over a live FishNet connection: a real host spawns
    /// its player and the server then judges position-bearing and input messages against the pure
    /// <see cref="MovementRules"/> budget. The input path carries a direction and the server
    /// integrates it itself, so a client has no field in which to smuggle a teleport; the
    /// belt-and-braces path is <see cref="PlayerMover.ApplyReportedPosition"/>, and an impossible
    /// delta there must snap the player straight back.
    ///
    /// <para>
    /// As with the other PlayMode suites, there is no attempt to catch a cheating client: that needs
    /// two processes because FishNet permits exactly one <c>NetworkManager</c> per process and a
    /// host is server and client at once. What <em>is</em> checkable in-process — that the server
    /// integrates within its own budget, accepts a plausible position, refuses an impossible one,
    /// and only lets a privileged <see cref="PlayerMover.Teleport"/> move the baseline — is asserted
    /// here; the attributes are asserted by reflection in <c>PlayerMoverTests</c>.
    /// </para>
    /// </summary>
    public sealed class PlayerMoverPlayModeTests
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
            _mover.ApplyInput(new Vector2(1f, 1f), 0.5);

            float travelled = Vector3.Distance(origin, _player.transform.position);
            Assert.AreEqual(_mover.MaxSpeed * 0.5f, travelled, 0.05f,
                "A diagonal push must be normalised so it cannot outrun a straight one.");
        }

        [UnityTest]
        public IEnumerator PlausibleReportedPosition_IsAccepted()
        {
            yield return StartHostWithPlayer();

            Vector3 origin = _player.transform.position;

            // Within the tick budget: 6 m/s for half a second is three metres.
            Vector3 target = origin + Vector3.right * (_mover.MaxSpeed * 0.5f);

            bool accepted = _mover.ApplyReportedPosition(target, 0.5);

            Assert.IsTrue(accepted, "A position reachable within the budget must be accepted.");
            Assert.AreEqual(target, _player.transform.position);
            Assert.AreEqual(target, _mover.LastAuthoritativePosition);
        }

        [UnityTest]
        public IEnumerator ImpossibleReportedPosition_SnapsThePlayerBack()
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
        public IEnumerator PlayerPrefab_CarriesTheMover()
        {
            yield return StartHostWithPlayer();

            Assert.IsNotNull(_mover, "PlayerPrefabFactory must put PlayerMover on the player.");
            Assert.IsTrue(_player.IsServer, "The host owns the server half of the player object.");
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

            // OnStartServer caches the authoritative baseline; the host client initialises a tick
            // behind the spawn. Wait for both so the mover is fully online before the test pokes it.
            yield return WaitUntil(() => _player.IsClientInitialized && _player.IsServer, 300);
        }

        private static IEnumerator WaitUntil(Func<bool> condition, int maxFrames)
        {
            for (int i = 0; i < maxFrames && !condition(); i++)
                yield return null;
        }
    }
}
