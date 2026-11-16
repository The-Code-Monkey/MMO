using System.Collections.Generic;
using FishNet.Object;
using MMO.Editor;
using MMO.Gameplay;
using MMO.Inventory;
using MMO.Net;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MMO.Tests
{
    /// <summary>
    /// The shipped resource content: the iron vein a brand-new character gathers their first
    /// ore from, and the prefab that carries it.
    ///
    /// <para>
    /// These are content locks rather than behaviour tests. The definition is a hand-authored
    /// asset, so the only thing that proves it still says what it is meant to say is a test that
    /// reads the shipped asset and says it out loud — otherwise a re-bake, a re-import or a
    /// renamed GUID would quietly turn the tutorial vein into an inert rock.
    /// </para>
    /// </summary>
    // These tests bake assets and re-import them per test, which costs seconds each; the
    // Unity Tests workflow shards them by category so they run in parallel with the suite.
    [Category("Bake")]
    [Category("BakeContent")]
    public sealed class ResourceDefinitionContentTests
    {
        private const string ShippedDefinitionPath = "Assets/Resources/ResourceDefinitions/Iron_Ore.asset";

        [TearDown]
        public void TearDown()
        {
            // Loading the shipped item assets registers them in a global registry. Leaving them
            // behind would make every later test count items it never registered.
            ItemDefinitionRegistry.Clear();
        }

        [Test]
        public void TheShippedDefinition_LoadsFromResources()
        {
            ResourceDefinition definition = ResourceDefinition.LoadDefault();

            Assert.IsNotNull(definition,
                "The shipped definition has to load by name, because the prefab factory finds it that way.");
            Assert.AreEqual(ShippedDefinitionPath, AssetDatabase.GetAssetPath(definition));
        }

        [Test]
        public void TheShippedDefinition_IsAnIronVeinThatDropsTheShippedOre()
        {
            ResourceDefinition definition = ResourceDefinition.LoadDefault();

            Assert.AreEqual(ResourceType.Ore, definition.Type);
            Assert.IsNotNull(definition.Drop, "A vein with no drop item awards nothing at all.");
            Assert.AreEqual("Iron Ore", definition.Drop.ItemName,
                "The shipped Resources item is what a default vein awards, so the loop works out of the box.");
            Assert.AreEqual(5, definition.DropAmount);
        }

        [Test]
        public void TheShippedDefinition_IsBareHandedSoTheTutorialKeepsWorking()
        {
            ResourceDefinition definition = ResourceDefinition.LoadDefault();

            Assert.AreEqual(ToolKind.None, definition.RequiredTool,
                "The tutorial vein is bare-handed; a designer tightens it to e.g. a pickaxe.");
            Assert.AreEqual(0, definition.GatheringTier);
        }

        [Test]
        public void TheShippedDefinition_TimesTheGatheringLoop()
        {
            ResourceDefinition definition = ResourceDefinition.LoadDefault();

            Assert.AreEqual(30f, definition.ResetTime, 0.0001f);
            Assert.AreEqual(1.2f, definition.GatherIntervalSeconds, 0.0001f);
            Assert.AreEqual(3f, definition.InteractRange, 0.0001f);
        }

        [Test]
        public void TheShippedPrefab_CarriesTheShippedDefinition()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ResourceNodeBaker.PrefabAssetPath);

            Assert.IsNotNull(prefab, $"The shipped resource node prefab is missing from '{ResourceNodeBaker.PrefabAssetPath}'.");

            ResourceNode node = prefab.GetComponent<ResourceNode>();
            Assert.IsNotNull(node, "The prefab has to carry the node component.");

            Assert.IsNotNull(node.Definition,
                "The prefab has to carry the shipped definition, or every vein in the world is inert.");
            Assert.AreSame(ResourceDefinition.LoadDefault(), node.Definition,
                "The prefab and the factory have to agree on one definition, or a runtime node differs from a baked one.");
        }

        [Test]
        public void TheLargeDefinition_IsAnIronVeinWithMoreOre()
        {
            var definition = AssetDatabase.LoadAssetAtPath<ResourceDefinition>(
                ResourceNodeAssetBaker.LargeDefinitionPath);

            Assert.IsNotNull(definition, "The large iron-ore definition is missing.");
            Assert.AreEqual(ResourceType.Ore, definition.Type);
            Assert.IsNotNull(definition.Drop);
            Assert.AreEqual("Iron Ore", definition.Drop.ItemName);
            Assert.AreEqual(15, definition.DropAmount,
                "The large vein must hold more ore than the normal five-harvest vein.");
            Assert.Greater(definition.DropAmount, ResourceDefinition.LoadDefault().DropAmount);
        }

        [Test]
        public void TheLargePrefab_CarriesTheLargeDefinitionAndBothModels()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ResourceNodeAssetBaker.LargePrefabPath);
            var definition = AssetDatabase.LoadAssetAtPath<ResourceDefinition>(
                ResourceNodeAssetBaker.LargeDefinitionPath);

            Assert.IsNotNull(prefab, "The large iron-ore prefab is missing.");
            Transform full = prefab.transform.Find("Visuals/IronOreMesh");
            Transform empty = prefab.transform.Find("Visuals/StoneOreEmptyMesh");
            Assert.IsNotNull(full);
            Assert.IsNotNull(empty);
            Assert.AreEqual("IronOreLarge", full.GetComponent<MeshRenderer>().sharedMaterial.name);
            Assert.AreEqual("StoneOreLargeEmpty", empty.GetComponent<MeshRenderer>().sharedMaterial.name);

            ResourceNode node = prefab.GetComponent<ResourceNode>();
            Assert.IsNotNull(node);
            Assert.AreSame(definition, node.Definition,
                "The large prefab must carry the large definition, or it yields the normal amount.");

            var collider = prefab.GetComponent<BoxCollider>();
            Assert.IsNotNull(collider);
            Assert.AreEqual(new Vector3(2.8f, 2.4f, 2.2f),
                Vector3.Scale(collider.size, prefab.transform.lossyScale),
                "The large model needs a collider big enough to click.");
        }

        [Test]
        public void TheNewOreItems_AreShippedWithTheExpectedNames()
        {
            var variants = new[]
            {
                (ResourceNodeAssetBaker.CopperItemPath, "Copper Ore"),
                (ResourceNodeAssetBaker.TinItemPath, "Tin Ore"),
                (ResourceNodeAssetBaker.CobaltItemPath, "Cobalt Ore"),
                (ResourceNodeAssetBaker.VeridiumItemPath, "Veridium Ore"),
            };

            foreach ((string path, string itemName) in variants)
            {
                var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
                Assert.IsNotNull(item, $"Missing item at '{path}'.");
                Assert.AreEqual(itemName, item.ItemName);
            }
        }

        [Test]
        public void TheNewDefinitions_AreOreVeinsWithTheSameTimingAsIron()
        {
            var variants = new[]
            {
                (ResourceNodeAssetBaker.CopperDefinitionPath, "Copper Ore", 5),
                (ResourceNodeAssetBaker.CopperLargeDefinitionPath, "Copper Ore", 15),
                (ResourceNodeAssetBaker.TinDefinitionPath, "Tin Ore", 5),
                (ResourceNodeAssetBaker.TinLargeDefinitionPath, "Tin Ore", 15),
                (ResourceNodeAssetBaker.CobaltDefinitionPath, "Cobalt Ore", 5),
                (ResourceNodeAssetBaker.VeridiumDefinitionPath, "Veridium Ore", 5),
            };

            foreach ((string path, string itemName, int amount) in variants)
            {
                var definition = AssetDatabase.LoadAssetAtPath<ResourceDefinition>(path);
                Assert.IsNotNull(definition, $"Missing definition at '{path}'.");
                Assert.AreEqual(ResourceType.Ore, definition.Type);
                Assert.AreEqual(itemName, definition.Drop.ItemName);
                Assert.AreEqual(amount, definition.DropAmount);
                Assert.AreEqual(ToolKind.None, definition.RequiredTool);
                Assert.AreEqual(0, definition.GatheringTier);
                Assert.AreEqual(30f, definition.ResetTime, 0.0001f);
                Assert.AreEqual(1.2f, definition.GatherIntervalSeconds, 0.0001f);
                Assert.AreEqual(3f, definition.InteractRange, 0.0001f);
            }
        }

        [Test]
        public void TheNewPrefabs_CarryTheirDefinitionAndBothModels()
        {
            var variants = new[]
            {
                (ResourceNodeAssetBaker.CopperPrefabPath, ResourceNodeAssetBaker.CopperDefinitionPath,
                    "CopperOreMesh", "StoneOreEmptyMesh", "CopperOre", "StoneOreEmpty"),
                (ResourceNodeAssetBaker.CopperLargePrefabPath, ResourceNodeAssetBaker.CopperLargeDefinitionPath,
                    "CopperOreMesh", "StoneOreLargeEmptyMesh", "CopperOreLarge", "StoneOreLargeEmpty"),
                (ResourceNodeAssetBaker.TinPrefabPath, ResourceNodeAssetBaker.TinDefinitionPath,
                    "TinOreMesh", "StoneOreEmptyMesh", "TinOre", "StoneOreEmpty"),
                (ResourceNodeAssetBaker.TinLargePrefabPath, ResourceNodeAssetBaker.TinLargeDefinitionPath,
                    "TinOreMesh", "StoneOreLargeEmptyMesh", "TinOreLarge", "StoneOreLargeEmpty"),
                (ResourceNodeAssetBaker.CobaltPrefabPath, ResourceNodeAssetBaker.CobaltDefinitionPath,
                    "CobaltOreMesh", "CobaltOreEmptyMesh", "CobaltOre", "CobaltOreEmpty"),
                (ResourceNodeAssetBaker.VeridiumPrefabPath, ResourceNodeAssetBaker.VeridiumDefinitionPath,
                    "VeridiumOreMesh", "VeridiumOreEmptyMesh", "VeridiumOre", "VeridiumOreEmpty"),
            };

            foreach ((string prefabPath, string definitionPath, string fullName, string emptyName,
                      string fullMaterial, string emptyMaterial) in variants)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                var definition = AssetDatabase.LoadAssetAtPath<ResourceDefinition>(definitionPath);
                Assert.IsNotNull(prefab, $"Missing prefab at '{prefabPath}'.");
                Assert.IsNotNull(definition, $"Missing definition at '{definitionPath}'.");

                Transform full = prefab.transform.Find($"Visuals/{fullName}");
                Transform empty = prefab.transform.Find($"Visuals/{emptyName}");
                Assert.IsNotNull(full, $"'{prefabPath}' needs '{fullName}'.");
                Assert.IsNotNull(empty, $"'{prefabPath}' needs '{emptyName}'.");
                Assert.AreEqual(fullMaterial, full.GetComponent<MeshRenderer>().sharedMaterial.name);
                Assert.AreEqual(emptyMaterial, empty.GetComponent<MeshRenderer>().sharedMaterial.name);

                ResourceNode node = prefab.GetComponent<ResourceNode>();
                Assert.IsNotNull(node);
                Assert.AreSame(definition, node.Definition);
            }
        }

        [Test]
        public void TheNodeCatalog_ListsEveryNodePrefab()
        {
            Assert.AreEqual(8, ResourceNodeAssetBaker.AllPrefabPaths.Length);

            foreach (string path in ResourceNodeAssetBaker.AllPrefabPaths)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<NetworkObject>(path);
                Assert.IsNotNull(prefab, $"Node prefab missing at '{path}'.");
            }
        }

        [Test]
        public void EveryNodePrefab_HasAUniqueFishNetAssetPathHash()
        {
            var paths = new Dictionary<ulong, string>();

            foreach (string path in ResourceNodeAssetBaker.AllPrefabPaths)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<NetworkObject>(path);
                Assert.IsNotNull(prefab, $"Node prefab missing at '{path}'.");
                Assert.AreNotEqual(0ul, prefab.AssetPathHash,
                    $"'{path}' has no FishNet AssetPathHash; a build will log a hash collision.");

                Assert.IsFalse(paths.TryGetValue(prefab.AssetPathHash, out string other),
                    $"'{path}' and '{other}' share FishNet AssetPathHash {prefab.AssetPathHash}.");
                paths.Add(prefab.AssetPathHash, path);
            }
        }

        [Test]
        public void RebuildingNodePrefabs_PreservesTheirFishNetAssetPathHash()
        {
            var before = new Dictionary<string, ulong>();
            foreach (string path in ResourceNodeAssetBaker.AllPrefabPaths)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<NetworkObject>(path);
                Assert.IsNotNull(prefab, $"Node prefab missing at '{path}'.");
                before.Add(path, prefab.AssetPathHash);
            }

            ResourceNodeAssetBaker.Rebuild();

            foreach (string path in ResourceNodeAssetBaker.AllPrefabPaths)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<NetworkObject>(path);
                Assert.IsNotNull(prefab, $"Node prefab missing at '{path}'.");
                Assert.AreNotEqual(0ul, prefab.AssetPathHash,
                    $"'{path}' lost its FishNet AssetPathHash during a rebuild.");
                Assert.AreEqual(before[path], prefab.AssetPathHash,
                    $"'{path}' changed FishNet AssetPathHash during a rebuild.");
            }
        }

        [Test]
        public void ThePrefabFactory_BuildsANodeThatCarriesTheShippedDefinition()
        {
            NetworkObject prefab = ResourceNodePrefabFactory.Create();

            try
            {
                ResourceNode node = prefab.GetComponent<ResourceNode>();

                Assert.IsNotNull(node.Definition,
                    "A node built at runtime has to carry the same definition the baked prefab does.");
                Assert.AreSame(ResourceDefinition.LoadDefault(), node.Definition);
            }
            finally
            {
                Object.DestroyImmediate(prefab.gameObject);
            }
        }
    }
}
