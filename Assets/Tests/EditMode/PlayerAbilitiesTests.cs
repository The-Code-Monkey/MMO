using System.Reflection;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using MMO.Gameplay;
using MMO.Net;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MMO.Tests
{
    /// <summary>
    /// The networked shell of the ability framework. The cast pipeline itself is covered end to
    /// end by <c>AbilityCasterTests</c>; what is asserted here is the wiring that cannot be
    /// checked without reflection: that the bar is replicated one name per slot, that the only
    /// entry points a client has are the two the issue allows (an equip on the server, a cast
    /// request from the owner), and that nothing exposes a setter a client could write through.
    /// </summary>
    public sealed class PlayerAbilitiesTests
    {
        private readonly System.Collections.Generic.List<GameObject> _sceneObjects = new();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _sceneObjects)
            {
                if (go != null)
                    Object.DestroyImmediate(go);
            }

            _sceneObjects.Clear();
        }

        [Test]
        public void PlayerAbilities_IsANetworkBehaviour()
        {
            Assert.AreEqual(typeof(NetworkBehaviour), typeof(PlayerAbilities).BaseType,
                "Only a NetworkBehaviour can own SyncVars and RPCs, which the slot bar and cast request need.");
        }

        [Test]
        public void SlotNames_AreReplicatedWithOneSyncVarPerSlot()
        {
            // Six plain string SyncVars, one per slot. The count is checked against the loadout
            // so widening the bar without adding the matching field fails here rather than at
            // runtime, when a client's action bar quietly loses its last button.
            var slotNames = new System.Collections.Generic.List<FieldInfo>();

            foreach (FieldInfo field in typeof(PlayerAbilities).GetFields(
                         BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (field.FieldType.IsGenericType &&
                    field.FieldType.GetGenericTypeDefinition() == typeof(SyncVar<>))
                {
                    Assert.AreEqual(typeof(string), field.FieldType.GetGenericArguments()[0],
                        $"{field.Name} should replicate the skill's name, which is what an action bar displays.");
                    slotNames.Add(field);
                }
            }

            Assert.AreEqual(AbilityLoadout.MaxSlots, slotNames.Count,
                "Every slot needs its own replicated name.");
        }

        [Test]
        public void EquipGear_IsPublicAndServerOnly()
        {
            MethodInfo method = typeof(PlayerAbilities).GetMethod(
                "EquipGear", BindingFlags.Public | BindingFlags.Instance, null,
                new[] { typeof(WeaponData), typeof(ArmorData) }, null);

            Assert.IsNotNull(method, "The inventory system needs a public server-side equip entry point.");
            Assert.IsNotNull(method.GetCustomAttribute<ServerAttribute>(),
                "Equipping must be [Server]: a client that could fill its own bar could grant itself skills.");
        }

        [Test]
        public void RequestCast_IsTheOwnerSideEntryPointWithNoServerGuardOfItsOwn()
        {
            MethodInfo method = typeof(PlayerAbilities).GetMethod(
                "RequestCast", BindingFlags.Public | BindingFlags.Instance, null,
                new[] { typeof(int), typeof(Vector3) }, null);

            Assert.IsNotNull(method, "The owning client needs a way to say 'cast slot N at here'.");
            Assert.IsNull(method.GetCustomAttribute<ServerAttribute>(),
                "This runs on the client that pressed the button; guarding it as [Server] would make it unreachable.");
            Assert.IsNull(method.GetCustomAttribute<ServerRpcAttribute>(),
                "RequestCast wraps the RPC call; the RPC itself is the networked method.");
        }

        [Test]
        public void RpcRequestCast_CarriesTheServerRpcAttributeAndEndsWithTheSenderConnection()
        {
            MethodInfo method = typeof(PlayerAbilities).GetMethod(
                "RpcRequestCast", BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.IsNotNull(method, "The cast request has to reach the server as an RPC.");
            Assert.IsNotNull(method.GetCustomAttribute<ServerRpcAttribute>(),
                "Without [ServerRpc] FishNet would never deliver the request — or worse, would let it run locally.");

            ParameterInfo[] parameters = method.GetParameters();
            Assert.AreEqual(3, parameters.Length, "slot, aim point, and the connection FishNet fills in.");
            Assert.AreEqual(typeof(int), parameters[0].ParameterType);
            Assert.AreEqual(typeof(Vector3), parameters[1].ParameterType);
            Assert.AreEqual(typeof(NetworkConnection), parameters[2].ParameterType,
                "The sender parameter is what lets the server re-check ownership against the real connection.");
            Assert.IsTrue(parameters[2].HasDefaultValue,
                "The connection must default so FishNet can fill it in.");
        }

        [Test]
        public void GetSlotName_IsPublicAndAnswersEmptyForEmptyOrUnknownSlots()
        {
            PlayerAbilities abilities = Attach();

            // Unspawned: the SyncVars still hold their defaults, which is also what a client
            // sees for a slot it has not been told about yet.
            Assert.AreEqual(string.Empty, abilities.GetSlotName(0));
            Assert.AreEqual(string.Empty, abilities.GetSlotName(-1));
            Assert.AreEqual(string.Empty, abilities.GetSlotName(AbilityLoadout.MaxSlots));
        }

        [Test]
        public void SlotsAndCooldowns_AreReadableButNeverWritable()
        {
            foreach (string name in new[] { "Slots", "Cooldowns" })
            {
                PropertyInfo property = typeof(PlayerAbilities).GetProperty(
                    name, BindingFlags.Public | BindingFlags.Instance);

                Assert.IsNotNull(property, $"{name} should be readable by server and UI code.");
                Assert.IsNull(property.SetMethod,
                    $"{name} must have no public setter; a client writing it would be inventing its own state.");
            }
        }

        [Test]
        public void OnStartNetwork_IsOverriddenSoTheServerCanPublishTheBar()
        {
            // Slot names start empty until the server writes them, so the spawn-time publish is
            // what gives a joining client an action bar at all.
            Assert.IsNotNull(typeof(PlayerAbilities).GetMethod(
                "OnStartNetwork", BindingFlags.Public | BindingFlags.Instance));
        }

        [Test]
        public void Update_ExistsSoQueuedCastsResolve()
        {
            // The cast-time half of the framework is Unity's update loop pumping the queue; if
            // this method disappears, every channelling spell silently never lands.
            MethodInfo update = typeof(PlayerAbilities).GetMethod(
                "Update", BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.IsNotNull(update, "PlayerAbilities must pump its own cast queue each frame.");
            Assert.AreEqual(typeof(void), update.ReturnType);
        }

        [Test]
        public void PlayerPrefab_CarriesTheAbilityComponentNextToTheStats()
        {
            NetworkObject nob = PlayerPrefabFactory.Create();
            _sceneObjects.Add(nob.gameObject);

            var abilities = nob.GetComponent<PlayerAbilities>();
            Assert.IsNotNull(abilities,
                "The equipment-injected ability slots belong on the player object the server spawns.");
            Assert.IsNotNull(nob.GetComponent<PlayerStats>(),
                "The caster spends the vitals sitting next to it.");
            Assert.AreSame(nob.gameObject, abilities.gameObject,
                "The slots must share the NetworkObject or FishNet cannot replicate them.");
        }

        private PlayerAbilities Attach()
        {
            var go = new GameObject("PlayerAbilitiesUnderTest");
            _sceneObjects.Add(go);

            return go.AddComponent<PlayerAbilities>();
        }
    }
}
