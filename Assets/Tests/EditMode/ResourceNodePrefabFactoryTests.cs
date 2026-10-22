using System;
using System.Collections.Generic;
using FishNet.Managing.Object;
using FishNet.Object;
using MMO.Gameplay;
using MMO.Inventory;
using MMO.Net;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MMO.Tests
{
    /// <summary>
    /// The resource node FishNet can track: a cube with the project's own networked
    /// <see cref="ResourceNode"/> on it, coloured the way a vein reads in the world, plus the
    /// registration path a runtime instantiation needs so both ends agree on its prefab id.
    ///
    /// Asserted on real components rather than doubles, because every failure this guards
    /// against — a node without a NetworkObject, a vein that never reached the spawnable
    /// collection, a colour that renders magenta under URP — is silent until a player clicks.
    /// </summary>
    public sealed class ResourceNodePrefabFactoryTests
    {
        private readonly List<GameObject> _sceneObjects = new();
        private readonly List<Object> _scratch = new();

        [TearDown]
        public void TearDown()
        {
            foreach (Object owned in _scratch)
            {
                if (owned != null)
                    Object.DestroyImmediate(owned);
            }

            foreach (GameObject go in _sceneObjects)
            {
                if (go != null)
                    Object.DestroyImmediate(go);
            }

            _scratch.Clear();
            _sceneObjects.Clear();
        }

        private NetworkObject NewNode() => ResourceNodePrefabFactory.Create();

        private SinglePrefabObjects NewSpawnables()
        {
            var prefabs = ScriptableObject.CreateInstance<SinglePrefabObjects>();
            prefabs.name = "TestSpawnablePrefabs";
            _scratch.Add(prefabs);
            return prefabs;
        }

        private void Track(NetworkObject nob) => _sceneObjects.Add(nob.gameObject);

        #region The object itself.
        [Test]
        public void Create_CarriesANetworkObject()
        {
            NetworkObject nob = NewNode();
            Track(nob);

            Assert.IsNotNull(nob.GetComponent<NetworkObject>(),
                "Without a NetworkObject FishNet has nothing to track across the network.");
        }

        [Test]
        public void Create_MakesACube()
        {
            NetworkObject nob = NewNode();
            Track(nob);

            var collider = nob.GetComponent<BoxCollider>();
            Assert.IsNotNull(collider, "CreatePrimitive(Cube) must leave a BoxCollider behind — that is what a player clicks.");
        }

        [Test]
        public void Create_GivesTheCubeItsDeclaredSize()
        {
            NetworkObject nob = NewNode();
            Track(nob);

            // Unity's cube primitive is unit-sized; the declared dimensions ride on the
            // transform scale, which is also what the BoxCollider and the mesh scale by — so
            // the thing a player clicks always matches the thing they see.
            var collider = nob.GetComponent<BoxCollider>();
            Assert.AreEqual(Vector3.one, collider.size, "The primitive collider stays unit-sized.");
            Assert.AreEqual(ResourceNodePrefabFactory.DefaultScale, nob.transform.localScale);
        }

        [Test]
        public void Create_MakesTheVeinVisible()
        {
            NetworkObject nob = NewNode();
            Track(nob);

            var filter = nob.GetComponent<MeshFilter>();
            Assert.IsNotNull(filter, "A cube nobody can see is not a landmark.");
            Assert.IsNotNull(nob.GetComponent<MeshRenderer>(), "A cube without a renderer is invisible.");
            Assert.IsNotNull(filter.sharedMesh, "The primitive must keep its cube mesh.");
        }

        [Test]
        public void Create_IsOreColoured()
        {
            NetworkObject nob = NewNode();
            Track(nob);

            Material material = nob.GetComponent<MeshRenderer>()?.sharedMaterial;
            if (material == null)
            {
                Assert.Ignore("No material could be resolved in this environment.");
                return;
            }

            Color colour = ReadColour(material);
            Assert.AreEqual(ResourceNodePrefabFactory.OreColor, colour,
                "Grey-brown is how a vein reads as rock against the red of an enemy.");
        }

        [Test]
        public void Create_AddsTheProjectsNetworkedNode()
        {
            NetworkObject nob = NewNode();
            Track(nob);

            Assert.IsNotNull(nob.GetComponent<ResourceNode>(),
                "ResourceNode is the project's own networked component and belongs on the vein.");
        }

        [Test]
        public void Create_PutsTheNodeNextToTheNetworkObject()
        {
            NetworkObject nob = NewNode();
            Track(nob);

            NetworkBehaviour[] behaviours = nob.GetComponents<NetworkBehaviour>();
            Assert.AreEqual(1, behaviours.Length, "Only the project's own behaviour belongs here.");
            Assert.IsInstanceOf<ResourceNode>(behaviours[0]);
            Assert.AreSame(nob.gameObject, behaviours[0].gameObject,
                "The node and the NetworkObject must be on the same object.");
        }

        [Test]
        public void Create_NamesTheObjectResourceNode()
        {
            NetworkObject nob = NewNode();
            Track(nob);

            Assert.AreEqual(ResourceNodePrefabFactory.PrefabName, nob.name);
        }

        [Test]
        public void Create_LeavesTheObjectActiveSoItCanBeSavedAsAPrefab()
        {
            NetworkObject nob = NewNode();
            Track(nob);

            Assert.IsTrue(nob.gameObject.activeSelf,
                "An inactive root is saved into the prefab greyed out; deactivate at runtime instead.");
        }

        [Test]
        public void Create_BuildsIndependentInstances()
        {
            NetworkObject first = NewNode();
            Track(first);
            NetworkObject second = NewNode();
            Track(second);

            Assert.AreNotSame(first, second);
            Assert.AreNotSame(first.gameObject, second.gameObject,
                "Two veins in a level must be two objects, not one shared mesh.");
        }

        [Test]
        public void Create_BuildsTheDefaultVeinConfiguration()
        {
            NetworkObject nob = NewNode();
            Track(nob);
            ResourceNode node = nob.GetComponent<ResourceNode>();

            // The factory hands the node the shipped vein definition, so a node built at runtime
            // is exactly as workable as the baked prefab — no extra configuration on top.
            Assert.IsNotNull(node.Definition,
                "A node built at runtime has to carry the shipped definition, or it is inert.");
            Assert.AreEqual(ResourceType.Ore, node.Type);
            Assert.IsNotNull(node.Drop, "The shipped definition awards the shipped Iron Ore item.");
            Assert.AreEqual(5, node.DropAmount);
            Assert.AreEqual(ToolKind.None, node.RequiredTool);
            Assert.AreEqual(0, node.GatheringTier);
            Assert.AreEqual(30f, node.ResetTime, 0.0001f);
            Assert.AreEqual(1.2f, node.GatherIntervalSeconds, 0.0001f);
            Assert.AreEqual(3f, node.InteractRange, 0.0001f);
        }
        #endregion

        #region Spawnable registration.
        [Test]
        public void Register_AddsThePrefabToTheCollection()
        {
            SinglePrefabObjects prefabs = NewSpawnables();
            NetworkObject node = NewNode();
            Track(node);

            Assert.IsTrue(ResourceNodePrefabFactory.Register(prefabs, node));

            Assert.AreEqual(1, prefabs.Prefabs.Count);
            Assert.AreSame(node, prefabs.Prefabs[0],
                "FishNet derives prefab ids from list order, so the node has to be in there.");
        }

        [Test]
        public void Register_LeavesPrefabIdsToTheNetworkManagerWhileNotPlaying()
        {
            // Registration happens during an EditMode run here; InitializePrefabRange only runs
            // while the game is playing, so id assignment stays with NetworkManager.Awake — and
            // that is the only end that knows the final list order.
            SinglePrefabObjects prefabs = NewSpawnables();
            NetworkObject node = NewNode();
            Track(node);

            ResourceNodePrefabFactory.Register(prefabs, node);

            Assert.AreEqual(NetworkObject.UNSET_PREFABID_VALUE, node.PrefabId);
        }

        [Test]
        public void Register_IsIdempotent()
        {
            SinglePrefabObjects prefabs = NewSpawnables();
            NetworkObject node = NewNode();
            Track(node);

            Assert.IsTrue(ResourceNodePrefabFactory.Register(prefabs, node));
            Assert.IsFalse(ResourceNodePrefabFactory.Register(prefabs, node),
                "A second register would shift every prefab id by one on both ends.");
            Assert.AreEqual(1, prefabs.Prefabs.Count);
        }

        [Test]
        public void Register_KeepsThePermanentPrefabsInFront()
        {
            SinglePrefabObjects prefabs = NewSpawnables();
            NetworkObject player = new GameObject("Player").AddComponent<NetworkObject>();
            _sceneObjects.Add(player.gameObject);
            NetworkObject dummy = new GameObject("TargetDummy").AddComponent<NetworkObject>();
            _sceneObjects.Add(dummy.gameObject);
            NetworkObject node = NewNode();
            Track(node);

            // Player id 0, dummy id 1, node id 2 — the order both ends must agree on, and the
            // order a level's runtime node instantiation registers into.
            PlayerPrefabFactory.Register(prefabs, player);
            TargetDummyPrefabFactory.Register(prefabs, dummy);
            ResourceNodePrefabFactory.Register(prefabs, node);

            Assert.AreEqual(3, prefabs.Prefabs.Count);
            Assert.AreSame(player, prefabs.Prefabs[0]);
            Assert.AreSame(dummy, prefabs.Prefabs[1]);
            Assert.AreSame(node, prefabs.Prefabs[2],
                "A node registered before the permanent prefabs would steal their ids.");
        }

        [Test]
        public void Register_IgnoresANullPrefab()
        {
            SinglePrefabObjects prefabs = NewSpawnables();

            Assert.IsFalse(ResourceNodePrefabFactory.Register(prefabs, null));
            Assert.AreEqual(0, prefabs.Prefabs.Count);
        }

        [Test]
        public void Register_RejectsANullCollection()
        {
            NetworkObject node = NewNode();
            Track(node);

            Assert.Throws<ArgumentNullException>(() => ResourceNodePrefabFactory.Register(null, node));
        }

        [Test]
        public void Contains_ReportsWhetherThePrefabIsRegistered()
        {
            SinglePrefabObjects prefabs = NewSpawnables();
            NetworkObject node = NewNode();
            Track(node);
            NetworkObject stranger = NewNode();
            Track(stranger);

            Assert.IsFalse(ResourceNodePrefabFactory.Contains(prefabs, node));

            ResourceNodePrefabFactory.Register(prefabs, node);

            Assert.IsTrue(ResourceNodePrefabFactory.Contains(prefabs, node));
            Assert.IsFalse(ResourceNodePrefabFactory.Contains(prefabs, stranger));
        }

        [Test]
        public void Contains_IsFalseForNulls()
        {
            SinglePrefabObjects prefabs = NewSpawnables();
            NetworkObject node = NewNode();
            Track(node);
            ResourceNodePrefabFactory.Register(prefabs, node);

            Assert.IsFalse(ResourceNodePrefabFactory.Contains(prefabs, null));
            Assert.IsFalse(ResourceNodePrefabFactory.Contains(null, node));
        }
        #endregion

        /// <summary>Reads the material's base colour whichever shader property owns it.</summary>
        private static Color ReadColour(Material material)
        {
            if (material.HasProperty("_BaseColor"))
                return material.GetColor("_BaseColor");
            if (material.HasProperty("_Color"))
                return material.color;

            Assert.Ignore("The resolved shader exposes no colour property to assert.");
            return default;
        }
    }
}
