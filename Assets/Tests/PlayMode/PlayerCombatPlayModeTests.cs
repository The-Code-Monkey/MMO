using System;
using System.Collections;
using System.Collections.Generic;
using FishNet.Managing;
using FishNet.Object;
using MMO.Gameplay;
using MMO.Net;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace MMO.Tests
{
    /// <summary>
    /// Proves the attack is server-validated over a live FishNet connection: a real host spawns its
    /// player and a dummy, and a click travels the real path — dummy click entry point →
    /// <c>PlayerCombat.RequestAttack</c> → <c>[ServerRpc]</c> → owner check → distance check → the
    /// dummy's SyncVar <c>Health</c>. The health bar neighbourhood of the issue, end to end.
    ///
    /// <para>
    /// As with <see cref="PlayerStatsPlayModeTests"/>, there is no attempt to catch a cheating
    /// client here: that needs two processes, because FishNet permits exactly one
    /// <c>NetworkManager</c> per process and a host is server and client simultaneously. The
    /// half of the guarantee that <em>is</em> checkable in-process — the ServerRpc's
    /// <c>RequireOwnership</c>, the <c>[Server]</c> attributes, and the server's distance check —
    /// is asserted here and by reflection in <c>PlayerCombatTests</c>.
    /// </para>
    ///
    /// <para>
    /// The dummy prefab is registered once per test session into the shared manager's heavily
    /// shared spawnable collection; that collection has no removal API, so the registration has to
    /// be session-scoped (a static field) and is never torn down. The manager itself is also
    /// session-scoped, so the id that registration assigns stays valid for the whole run.
    /// </para>
    /// </summary>
    public sealed class PlayerCombatPlayModeTests
    {
        private const int ArmedDamage = 40;
        private const float ArmedRange = 10f;
        private const float InRangeDistance = 5f;
        private const float OutOfRangeDistance = 50f;

        private static NetworkObject _dummyPrefab;

        private NetworkManager _manager;
        private NetworkObject _player;
        private PlayerCombat _combat;
        private TargetDummy _dummy;
        private readonly List<NetworkObject> _spawnedDummies = new();
        private WeaponData _weapon;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _manager = NetworkManagerTestHelper.GetOrCreate();

            _player = null;
            _combat = null;
            _dummy = null;

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

            foreach (NetworkObject spawned in _spawnedDummies)
            {
                if (spawned != null)
                    Object.Destroy(spawned.gameObject);
            }

            _spawnedDummies.Clear();

            if (_weapon != null)
                Object.Destroy(_weapon);

            yield return null;
        }

        [UnityTest]
        public IEnumerator ClickingTheDummyInRange_AppliesServerValidatedDamage()
        {
            yield return StartHostWithDummyAt(InRangeDistance, equipWeapon: true);

            // Exactly what OnMouseDown() invokes when a player presses the dummy's collider.
            _dummy.RequestAttackFromLocalPlayer();

            // The intent travels the real RPC path and the server applies it a tick or two later;
            // poll rather than assume a frame count.
            yield return WaitUntil(() => _dummy.Health == TargetDummy.MaxHealth - ArmedDamage, 300);

            Assert.AreEqual(TargetDummy.MaxHealth - ArmedDamage, _dummy.Health,
                "A click in range, with a weapon in hand, must land exactly the weapon's damage.");
        }

        [UnityTest]
        public IEnumerator ClickingTheDummyOutOfRange_IsRefusedByTheServer()
        {
            yield return StartHostWithDummyAt(OutOfRangeDistance, equipWeapon: true);

            _dummy.RequestAttackFromLocalPlayer();

            // Give the RPC plenty of time to travel; the server must refuse it on range alone.
            yield return WaitUntil(() => _dummy.Health != TargetDummy.MaxHealth, 60);

            Assert.AreEqual(TargetDummy.MaxHealth, _dummy.Health,
                "The server must refuse a hit that is out of the weapon's range.");

            // Prove the refusal was about range and not a dropped click: drag the dummy into range
            // and the same attack lands. A silently dead pipeline would leave this at full health.
            _dummy.transform.position = _player.transform.position + Vector3.forward * InRangeDistance;
            _dummy.RequestAttackFromLocalPlayer();

            yield return WaitUntil(() => _dummy.Health == TargetDummy.MaxHealth - ArmedDamage, 300);

            Assert.AreEqual(TargetDummy.MaxHealth - ArmedDamage, _dummy.Health,
                "The same dummy, once in range, must be reachable.");
        }

        [UnityTest]
        public IEnumerator ClickingWithNoWeapon_IsRefusedByTheServer()
        {
            yield return StartHostWithDummyAt(InRangeDistance, equipWeapon: false);

            _dummy.RequestAttackFromLocalPlayer();

            yield return WaitUntil(() => _dummy.Health != TargetDummy.MaxHealth, 60);

            Assert.AreEqual(TargetDummy.MaxHealth, _dummy.Health,
                "An unarmed player cannot apply damage, however close the target.");
        }

        [UnityTest]
        public IEnumerator SpawnedDummy_StartsAtFullHealth()
        {
            yield return StartHostWithDummyAt(InRangeDistance, equipWeapon: false);

            // The server fills health on spawn in OnStartNetwork; zero would mean the reset never ran.
            Assert.AreEqual(TargetDummy.MaxHealth, _dummy.Health);
        }

        [UnityTest]
        public IEnumerator RegisteringTheDummyAtRuntime_AssignsItsPrefabIdImmediately()
        {
            NetworkManagerTestHelper.StartHost(_manager);
            yield return WaitUntil(() => _manager.IsServerStarted, 300);

            // The shared manager has already run Awake, so the runtime register path
            // (initializeAdded: true) has to hand the prefab its id here and now rather than wait
            // for a second Awake — otherwise the client could never resolve the spawn.
            NetworkObject prefab = EnsureDummyPrefab();

            Assert.AreNotEqual(NetworkObject.UNSET_PREFABID_VALUE, prefab.PrefabId,
                "A prefab id is required before anything can be spawned from this prefab.");
        }

        private IEnumerator StartHostWithDummyAt(float distance, bool equipWeapon)
        {
            NetworkManagerTestHelper.StartHost(_manager);
            yield return NetworkManagerTestHelper.WaitForHostAuthenticated(_manager);
            yield return NetworkManagerTestHelper.WaitForPlayerObject(_manager);

            _player = NetworkManagerTestHelper.FindPlayerObject(_manager);
            Assert.IsNotNull(_player, "A connecting client must receive a player object.");

            _combat = _player.GetComponent<PlayerCombat>();
            Assert.IsNotNull(_combat, "PlayerPrefabFactory must put PlayerCombat on the player.");

            // A host's local client initialises a tick behind the spawn; until then IsOwner reads
            // false and RequestAttack would drop the click as not-our-object.
            yield return WaitUntil(() => _player.IsClientInitialized, 300);
            Assert.IsTrue(_player.IsClientInitialized,
                "The host client never finished initialising its player object.");

            _dummy = SpawnDummy(_player.transform.position + Vector3.forward * distance);
            Assert.IsNotNull(_dummy, "A spawned dummy must carry the TargetDummy behaviour.");

            if (equipWeapon)
            {
                _weapon = CreateWeapon(ArmedDamage, ArmedRange);
                _combat.Equip(_weapon);
            }

            // OnStartNetwork fills health either during spawn or a tick later; wait for the full bar.
            yield return WaitUntil(() => _dummy.Health == TargetDummy.MaxHealth, 300);
        }

        private NetworkObject EnsureDummyPrefab()
        {
            // The shared spawnable collection has no removal API, so the dummy is registered once
            // for the whole test session and every test reuses the same prefab.
            if (_dummyPrefab != null)
                return _dummyPrefab;

            NetworkObject prefab = TargetDummyFactory.Create();

            // Out of sight, like the player prefab; FishNet's pool activates the clone it spawns.
            prefab.gameObject.SetActive(false);

            Assert.IsTrue(TargetDummyFactory.Register(_manager.SpawnablePrefabs, prefab),
                "The dummy prefab has to be a spawnable for the server to spawn it.");

            _dummyPrefab = prefab;
            return _dummyPrefab;
        }

        private TargetDummy SpawnDummy(Vector3 position)
        {
            NetworkObject prefab = EnsureDummyPrefab();

            // Spawned code-side with the server's authority, as a level designer or a combat
            // director would; FishNet elides a full client-side round-trip.
            NetworkObject instance = Object.Instantiate(prefab, position, Quaternion.identity);
            _spawnedDummies.Add(instance);
            _manager.ServerManager.Spawn(instance);
            return instance.GetComponent<TargetDummy>();
        }

        private static WeaponData CreateWeapon(int damage, float range)
        {
            var weapon = ScriptableObject.CreateInstance<WeaponData>();
            weapon.name = "PlayMode Test Weapon";

            // The weapon's fields are private [SerializeField], so the only honest way to arm a
            // test weapon is the same SerializedObject path the Inspector uses.
            var inspector = new SerializedObject(weapon);
            inspector.FindProperty("_autoAttackDamage").intValue = damage;
            inspector.FindProperty("_attackRange").floatValue = range;
            inspector.ApplyModifiedPropertiesWithoutUndo();
            return weapon;
        }

        private static IEnumerator WaitUntil(Func<bool> condition, int maxFrames)
        {
            for (int i = 0; i < maxFrames && !condition(); i++)
                yield return null;
        }
    }
}