using System;
using System.Collections;
using System.Collections.Generic;
using FishNet.Managing;
using FishNet.Managing.Object;
using FishNet.Object;
using MMO.Crafting;
using MMO.Inventory;
using MMO.Net;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace MMO.Tests
{
    /// <summary>
    /// The server-driven craft flow over a live host: opening a station crosses a real
    /// <c>[ServerRpc]</c>, the server answers that client with a <c>[TargetRpc]</c> carrying the
    /// filtered <see cref="RecipeOffer"/> payload, and the craft itself stays server-authoritative.
    ///
    /// <para>
    /// Host-mode limits, as elsewhere: one NetworkManager per process means a dishonest client
    /// cannot be simulated in process. What is proven is that the payload the panel renders is
    /// built server-side from the station the player actually stands near, and that showing an
    /// unaffordable recipe is a display courtesy — a craft request for it is still refused.
    /// </para>
    /// </summary>
    public sealed class PlayerCraftingPlayModeTests
    {
        private NetworkManager _manager;
        private PlayerCrafting _crafting;
        private PlayerInventory _inventory;
        private readonly List<GameObject> _spawned = new();
        private readonly List<Object> _assets = new();

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _manager = NetworkManagerTestHelper.GetOrCreate();
            NetworkManagerTestHelper.StopIfListening(_manager);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            NetworkManagerTestHelper.StopIfListening(_manager);
            yield return null;

            foreach (GameObject go in _spawned)
            {
                if (go != null)
                    Object.Destroy(go);
            }

            _spawned.Clear();

            RecipeRegistry.Clear();
            ItemDefinitionRegistry.Clear();

            foreach (Object asset in _assets)
            {
                if (asset != null)
                    Object.Destroy(asset);
            }

            _assets.Clear();
            yield return null;
        }

        [UnityTest]
        public IEnumerator RequestOpenStation_SendsOnlyThatStationsRecipes()
        {
            yield return StartHostAndPlayer();

            CraftingStation anvil = SpawnStation(CraftingStationType.Anvil, NearPlayer(new Vector3(0f, 0f, 1f)));
            SpawnStation(CraftingStationType.Alchemy, NearPlayer(new Vector3(1f, 0f, 0f)));

            RegisterRecipe("Iron Sword", CraftingStationType.Anvil, requiredLevel: 1);
            RegisterRecipe("Iron Plate", CraftingStationType.Anvil, requiredLevel: 5);
            RegisterRecipe("Healing Potion", CraftingStationType.Alchemy, requiredLevel: 1);

            CraftingStation opened = null;
            List<RecipeOffer> offers = null;
            _crafting.StationOpened += (station, payload) =>
            {
                opened = station;
                offers = new List<RecipeOffer>(payload);
            };

            _crafting.RequestOpenStation(anvil);

            yield return WaitUntil(() => offers != null, 300);

            Assert.AreSame(anvil, opened, "The payload must name the station that was clicked.");
            Assert.AreEqual(1, offers.Count, "Only the anvil's own, in-level recipes travel.");
            Assert.AreEqual("Iron Sword", offers[0].RecipeName);
            Assert.IsFalse(offers[0].IngredientsMet, "An empty bag affords nothing — shown, not hidden.");
            Assert.AreEqual(1, _crafting.AvailableRecipes.Count,
                "What the source exposes must be the payload that arrived.");
            Assert.AreEqual("Iron Sword", _crafting.AvailableRecipes[0].RecipeName);
        }

        [UnityTest]
        public IEnumerator RequestOpenStation_ForAlchemy_OpensTheSamePanelWithDifferentRecipes()
        {
            yield return StartHostAndPlayer();

            SpawnStation(CraftingStationType.Anvil, NearPlayer(new Vector3(0f, 0f, 1f)));
            CraftingStation alchemy = SpawnStation(CraftingStationType.Alchemy, NearPlayer(new Vector3(1f, 0f, 0f)));

            RegisterRecipe("Iron Sword", CraftingStationType.Anvil, requiredLevel: 1);
            RegisterRecipe("Healing Potion", CraftingStationType.Alchemy, requiredLevel: 1);

            List<RecipeOffer> offers = null;
            _crafting.StationOpened += (_, payload) => offers = new List<RecipeOffer>(payload);

            _crafting.RequestOpenStation(alchemy);

            yield return WaitUntil(() => offers != null, 300);

            Assert.AreEqual(1, offers.Count);
            Assert.AreEqual("Healing Potion", offers[0].RecipeName,
                "The same PlayerCrafting, the same payload shape — a different station's list.");
        }

        [UnityTest]
        public IEnumerator RequestOpenStation_WhenTheStationIsOutOfReach_SendsNoPayload()
        {
            yield return StartHostAndPlayer();

            CraftingStation faraway = SpawnStation(CraftingStationType.Anvil, NearPlayer(new Vector3(0f, 0f, 50f)));
            RegisterRecipe("Iron Sword", CraftingStationType.Anvil, requiredLevel: 1);

            bool raised = false;
            _crafting.StationOpened += (_, _) => raised = true;

            _crafting.RequestOpenStation(faraway);

            // Give a wrongly-sent payload every chance to arrive before judging.
            for (int i = 0; i < 30; i++)
                yield return null;

            Assert.IsFalse(raised, "Proximity is checked on the server, not by the client's click.");
            Assert.IsEmpty(_crafting.AvailableRecipes);
        }

        [UnityTest]
        public IEnumerator CraftRequest_ForAnOfferThePayloadCalledUnaffordable_IsRefused()
        {
            yield return StartHostAndPlayer();

            CraftingStation anvil = SpawnStation(CraftingStationType.Anvil, NearPlayer(new Vector3(0f, 0f, 1f)));
            ItemDefinition ore = CreateItem("Iron Ore");
            ItemDefinition sword = CreateItem("Iron Sword");
            RegisterRecipe("Iron Sword", CraftingStationType.Anvil, requiredLevel: 1,
                output: sword, ingredient: ore, count: 3);

            List<RecipeOffer> offers = null;
            _crafting.StationOpened += (_, payload) => offers = new List<RecipeOffer>(payload);

            _crafting.RequestOpenStation(anvil);
            yield return WaitUntil(() => offers != null, 300);

            Assert.AreEqual(1, offers.Count);
            Assert.IsFalse(offers[0].IngredientsMet);

            // The panel rendered the row; the request it would send is refused by the server
            // because the bag is still empty. The payload is a hint, never authorization.
            _crafting.RequestCraft(anvil, "Iron Sword");

            for (int i = 0; i < 30; i++)
                yield return null;

            Assert.AreEqual(0, CraftingValidator.Count(_inventory.Items, sword.ItemName),
                "The output never appears.");
            Assert.AreEqual(0, CraftingValidator.Count(_inventory.Items, ore.ItemName),
                "Nothing is produced from an unaffordable offer.");
        }

        [UnityTest]
        public IEnumerator CraftRequest_ForAnAffordableOffer_CraftsAndRefreshesThePayload()
        {
            yield return StartHostAndPlayer();

            CraftingStation anvil = SpawnStation(CraftingStationType.Anvil, NearPlayer(new Vector3(0f, 0f, 1f)));
            ItemDefinition ore = CreateItem("Iron Ore");
            ItemDefinition sword = CreateItem("Iron Sword");
            RegisterRecipe("Iron Sword", CraftingStationType.Anvil, requiredLevel: 1,
                output: sword, ingredient: ore, count: 3);

            for (int i = 0; i < 3; i++)
                Assert.IsTrue(_inventory.TryAddItem(ore), $"The bag should accept ingot {i}.");

            List<RecipeOffer> offers = null;
            _crafting.StationOpened += (_, payload) => offers = new List<RecipeOffer>(payload);

            _crafting.RequestOpenStation(anvil);
            yield return WaitUntil(() => offers != null, 300);

            Assert.IsTrue(offers[0].IngredientsMet,
                "With three ingots in the bag the payload must say so.");

            _crafting.RequestCraft(anvil, "Iron Sword");

            yield return WaitUntil(() => CraftingValidator.Count(_inventory.Items, sword.ItemName) == 1, 300);

            Assert.AreEqual(0, CraftingValidator.Count(_inventory.Items, ore.ItemName),
                "The craft consumed the ingredients.");

            yield return WaitUntil(() => offers.Count == 1 && !offers[0].IngredientsMet, 300);
            Assert.IsFalse(offers[0].IngredientsMet,
                "A successful craft hands the panel a fresh payload instead of a stale one.");
        }

        private IEnumerator StartHostAndPlayer()
        {
            NetworkManagerTestHelper.StartHost(_manager);
            yield return NetworkManagerTestHelper.WaitForHostAuthenticated(_manager);
            yield return NetworkManagerTestHelper.WaitForPlayerObject(_manager);

            NetworkObject player = NetworkManagerTestHelper.FindPlayerObject(_manager);
            Assert.IsNotNull(player, "A connecting client must receive a player object.");

            _crafting = player.GetComponent<PlayerCrafting>();
            Assert.IsNotNull(_crafting, "The prefab's crafting component has to travel with it.");
            _inventory = player.GetComponent<PlayerInventory>();
            Assert.IsNotNull(_inventory, "The prefab's inventory has to travel with it.");
        }

        private Vector3 NearPlayer(Vector3 offset) => _crafting.transform.position + offset;

        private CraftingStation SpawnStation(CraftingStationType type, Vector3 position)
        {
            NetworkObject prefab = ResolveStationPrefab();
            Assert.IsNotNull(prefab, "The test manager must register a crafting station prefab.");

            GameObject instance = Object.Instantiate(prefab.gameObject);
            instance.name = prefab.name;
            instance.transform.position = position;
            instance.SetActive(true);
            _spawned.Add(instance);

            _manager.ServerManager.Spawn(instance);

            CraftingStation station = instance.GetComponent<CraftingStation>();
            station.Configure(type);
            return station;
        }

        private NetworkObject ResolveStationPrefab()
        {
            if (_manager.SpawnablePrefabs is not SinglePrefabObjects single)
                return null;

            foreach (NetworkObject prefab in single.Prefabs)
            {
                if (prefab != null && prefab.name == CraftingStationPrefabFactory.PrefabName)
                    return prefab;
            }

            return null;
        }

        private RecipeData RegisterRecipe(
            string name,
            CraftingStationType stationType,
            int requiredLevel,
            ItemDefinition output = null,
            ItemDefinition ingredient = null,
            int count = 1)
        {
            RecipeData recipe = ScriptableObject.CreateInstance<RecipeData>();
            _assets.Add(recipe);

            var inspector = new SerializedObject(recipe);
            inspector.FindProperty("_recipeName").stringValue = name;
            inspector.FindProperty("_stationType").intValue = (int)stationType;
            inspector.FindProperty("_requiredCraftingLevel").intValue = requiredLevel;
            inspector.FindProperty("_output").objectReferenceValue = output;

            if (ingredient != null)
            {
                SerializedProperty ingredients = inspector.FindProperty("_ingredients");
                ingredients.arraySize = 1;
                SerializedProperty element = ingredients.GetArrayElementAtIndex(0);
                element.FindPropertyRelative("Item").objectReferenceValue = ingredient;
                element.FindPropertyRelative("Count").intValue = count;
            }

            inspector.ApplyModifiedPropertiesWithoutUndo();

            // OnEnable ran before the name existed, so the test registers it explicitly.
            RecipeRegistry.Register(recipe);
            return recipe;
        }

        private ItemDefinition CreateItem(string name)
        {
            ItemDefinition definition = ScriptableObject.CreateInstance<ItemDefinition>();
            _assets.Add(definition);

            var inspector = new SerializedObject(definition);
            inspector.FindProperty("_itemName").stringValue = name;
            inspector.ApplyModifiedPropertiesWithoutUndo();

            ItemDefinitionRegistry.Register(definition);
            return definition;
        }

        private static IEnumerator WaitUntil(Func<bool> condition, int maxFrames)
        {
            for (int i = 0; i < maxFrames && !condition(); i++)
                yield return null;
        }
    }
}
