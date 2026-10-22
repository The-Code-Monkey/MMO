using System.Collections.Generic;
using MMO.Gameplay;
using MMO.Inventory;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MMO.Tests
{
    /// <summary>
    /// The resource data card: the numbers a node reads when it answers "what am I, what do I
    /// give, and what does it take to take it". Every value is clamped in the property rather
    /// than in the Inspector, because the Inspector's <c>[Min]</c> is a hint an author can
    /// override and a server must not be defeatable by a mis-typed number.
    /// </summary>
    public sealed class ResourceDefinitionTests
    {
        private readonly List<Object> _scratch = new();

        [TearDown]
        public void TearDown()
        {
            // The item fixture registers itself in a global registry; leaving it behind would
            // hand every later test a bagful of ore it never made.
            foreach (Object asset in _scratch)
            {
                if (asset != null)
                    Object.DestroyImmediate(asset);
            }

            _scratch.Clear();
            ItemDefinitionRegistry.Clear();
        }

        [Test]
        public void Defaults_AreTheDocumentedOnes()
        {
            // A raw instance, not the fixture: this test is about what a designer gets before
            // touching anything in the Inspector.
            ResourceDefinition definition = ScriptableObject.CreateInstance<ResourceDefinition>();
            _scratch.Add(definition);

            Assert.AreEqual(ResourceType.None, definition.Type);
            Assert.IsNull(definition.Drop);
            Assert.AreEqual(ResourceDefinition.DefaultDropAmount, definition.DropAmount);
            Assert.AreEqual(0, definition.GatheringTier);
            Assert.AreEqual(ToolKind.None, definition.RequiredTool);
            Assert.AreEqual(ResourceDefinition.DefaultResetTime, definition.ResetTime, 0.0001f);
            Assert.AreEqual(ResourceDefinition.DefaultGatherIntervalSeconds, definition.GatherIntervalSeconds, 0.0001f);
            Assert.AreEqual(ResourceDefinition.DefaultInteractRange, definition.InteractRange, 0.0001f);
        }

        [Test]
        public void DropAmount_FloorsAtOne()
        {
            ResourceDefinition definition = Definition(dropAmount: 0);

            Assert.AreEqual(1, definition.DropAmount,
                "A node that holds nothing could never be depleted, because there is nothing to take.");
        }

        [Test]
        public void GatheringTier_FloorsAtZero()
        {
            ResourceDefinition definition = Definition(gatheringTier: -5);

            Assert.AreEqual(0, definition.GatheringTier,
                "A negative tier is not a tier above bare hands; it is a tier below nothing.");
        }

        [Test]
        public void ResetTimeAndIntervalAndReach_FloorAtZero()
        {
            ResourceDefinition definition = Definition(
                resetTime: -5f,
                gatherIntervalSeconds: -1f,
                interactRange: -2f);

            Assert.AreEqual(0f, definition.ResetTime);
            Assert.AreEqual(0f, definition.GatherIntervalSeconds);
            Assert.AreEqual(0f, definition.InteractRange);
        }

        [Test]
        public void RequiredTool_DefaultsToNone()
        {
            ResourceDefinition definition = ScriptableObject.CreateInstance<ResourceDefinition>();
            _scratch.Add(definition);

            Assert.AreEqual(ToolKind.None, definition.RequiredTool,
                "A resource names no tool by default, so bare hands work until a designer says otherwise.");
        }

        [Test]
        public void Drop_KeepsTheReferenceItWasGiven()
        {
            ItemDefinition ore = TestItemDefinitions.Create("DefinitionDropOre");
            _scratch.Add(ore);
            ResourceDefinition definition = Definition(drop: ore);

            Assert.AreSame(ore, definition.Drop);
        }

        private ResourceDefinition Definition(
            ItemDefinition drop = null,
            int dropAmount = ResourceDefinition.DefaultDropAmount,
            int gatheringTier = 0,
            float resetTime = ResourceDefinition.DefaultResetTime,
            float gatherIntervalSeconds = ResourceDefinition.DefaultGatherIntervalSeconds,
            float interactRange = ResourceDefinition.DefaultInteractRange)
        {
            ResourceDefinition definition = TestResourceDefinitions.Create(
                drop: drop,
                dropAmount: dropAmount,
                gatheringTier: gatheringTier,
                resetTime: resetTime,
                gatherIntervalSeconds: gatherIntervalSeconds,
                interactRange: interactRange);
            _scratch.Add(definition);
            return definition;
        }
    }
}
