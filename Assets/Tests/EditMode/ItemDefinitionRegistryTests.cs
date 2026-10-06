using System;
using MMO.Inventory;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MMO.Tests
{
    /// <summary>
    /// The lookup that turns the wire's <see cref="ItemInstance.ItemName"/> key back into a
    /// local definition. Two paths are guaranteed: explicit registration (tests, bootstraps)
    /// and a Resources fallback (the shipped items) — and a miss must resolve to "refuse the
    /// action", never to a crash.
    /// </summary>
    public sealed class ItemDefinitionRegistryTests
    {
        private ItemDefinition _first;
        private ItemDefinition _second;

        [TearDown]
        public void TearDown()
        {
            if (_first != null)
                Object.DestroyImmediate(_first);

            if (_second != null)
                Object.DestroyImmediate(_second);

            ItemDefinitionRegistry.Clear();
        }

        [Test]
        public void Registry_ResolvesWhatItRegisters()
        {
            _first = TestItemDefinitions.Create("Registered Blade");

            Assert.AreEqual(_first, ItemDefinitionRegistry.Resolve("Registered Blade"));
            Assert.AreEqual(1, ItemDefinitionRegistry.Count);
        }

        [Test]
        public void Registry_UnregisterRemovesItsOwnDefinition()
        {
            _first = TestItemDefinitions.Create("Temporary Item");

            ItemDefinitionRegistry.Unregister(_first);

            Assert.IsNull(ItemDefinitionRegistry.Resolve("Temporary Item"));
        }

        [Test]
        public void Registry_NullAndEmptyKeys_ResolveToNull()
        {
            Assert.IsNull(ItemDefinitionRegistry.Resolve(null), "A null key is a miss, not an exception.");
            Assert.IsNull(ItemDefinitionRegistry.Resolve(string.Empty));
        }

        [Test]
        public void Registry_RegisterRejectsNullAndUnnamedDefinitions()
        {
            // An unnamed definition has no key; registering it would make it unreachable anyway.
            Assert.Throws<ArgumentNullException>(() => ItemDefinitionRegistry.Register(null));

            _first = ScriptableObject.CreateInstance<ItemDefinition>();
            Assert.Throws<ArgumentException>(() => ItemDefinitionRegistry.Register(_first));
        }

        [Test]
        public void Registry_ASecondDefinitionTakesOverASharedKey()
        {
            // Keys are the item's display name, so a duplicate is an authoring mistake — but the
            // last one registered wins rather than silently keeping the first. The warning that
            // Unity logs here is intentional; only errors fail a test.
            _first = TestItemDefinitions.Create("Contested Key");
            _second = TestItemDefinitions.Create("Contested Key");

            Assert.AreEqual(_second, ItemDefinitionRegistry.Resolve("Contested Key"));
        }

        [Test]
        public void Registry_StaleUnregisterCannotEvictTheReplacement()
        {
            _first = TestItemDefinitions.Create("Contested Key");
            _second = TestItemDefinitions.Create("Contested Key");

            // The old asset's OnDisable fires late (teardown, a reimport); it must not delete
            // the entry its replacement now owns.
            ItemDefinitionRegistry.Unregister(_first);

            Assert.AreEqual(_second, ItemDefinitionRegistry.Resolve("Contested Key"));
        }

        [Test]
        public void Registry_DestroyedRegistration_FallsBackToResources()
        {
            // A destroyed-but-still-dictionary entry is Unity-null: Resolve must notice and
            // retry through Resources instead of handing back a fake-null object.
            _first = TestItemDefinitions.Create("Destroyed Later");
            Object.DestroyImmediate(_first);
            _first = null;

            Assert.IsNull(ItemDefinitionRegistry.Resolve("Destroyed Later"),
                "An in-code item that dies with its asset resolves to nothing.");
            Assert.IsNotNull(ItemDefinitionRegistry.Resolve("Broadsword"),
                "The Resources fallback still serves shipped items in the same situation.");
        }

        [Test]
        public void Registry_ClearEmptiesEveryRegistration()
        {
            _first = TestItemDefinitions.Create("Cleared Item");
            ItemDefinitionRegistry.Clear();

            Assert.AreEqual(0, ItemDefinitionRegistry.Count);
        }
    }
}
