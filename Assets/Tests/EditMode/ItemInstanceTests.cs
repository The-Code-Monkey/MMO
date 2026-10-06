using System;
using System.Reflection;
using MMO.Inventory;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MMO.Tests
{
    /// <summary>
    /// The wire payload of the equipment/inventory <c>SyncList</c>. FishNet serializes public
    /// fields only, so this test pins that contract down: five public fields, no FishNet types,
    /// and the empty/footprint behaviour every grid calculation leans on.
    /// </summary>
    public sealed class ItemInstanceTests
    {
        private ItemDefinition _created;

        [TearDown]
        public void TearDown()
        {
            if (_created != null)
                Object.DestroyImmediate(_created);

            ItemDefinitionRegistry.Clear();
        }

        [Test]
        public void ItemInstance_HoldsPublicFieldsForTheWire()
        {
            foreach (string name in new[] { "ItemName", "GridX", "GridY", "Width", "Height" })
            {
                FieldInfo field = typeof(ItemInstance).GetField(name, BindingFlags.Instance | BindingFlags.Public);

                Assert.IsNotNull(field, $"{name} must exist as a public field.");
                Assert.IsTrue(field.IsPublic && !field.IsInitOnly,
                    $"{name} must be a writable public field; FishNet's generated serializer skips anything else.");
            }
        }

        [Test]
        public void ItemInstance_CarriesNoFishNetTypesOfItsOwn()
        {
            // The instance travels *through* a serializer; it is not a networked object itself.
            foreach (FieldInfo field in typeof(ItemInstance).GetFields(
                         BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic |
                         BindingFlags.DeclaredOnly))
            {
                Assert.IsFalse(field.FieldType.FullName != null && field.FieldType.FullName.StartsWith("FishNet."),
                    $"{field.Name} is a FishNet type; ItemInstance is plain data.");
            }
        }

        [Test]
        public void ItemInstance_EmptyAndDefault_ReadAsEmpty()
        {
            Assert.IsTrue(ItemInstance.Empty.IsEmpty);
            Assert.IsTrue(default(ItemInstance).IsEmpty,
                "A SyncList element that was never written must read as an empty cell.");
        }

        [Test]
        public void ItemInstance_Create_CopiesNameAndFootprint()
        {
            ItemDefinition definition = CreateDefinition("Wire Payload", width: 2, height: 3);

            ItemInstance instance = ItemInstance.Create(definition, 4, 5);

            Assert.AreEqual("Wire Payload", instance.ItemName);
            Assert.AreEqual(2, instance.Width, "The footprint must be copied so grid math never resolves an asset.");
            Assert.AreEqual(3, instance.Height);
            Assert.IsFalse(instance.IsEmpty);
        }

        [Test]
        public void ItemInstance_Create_PlacesAtTheRequestedCell()
        {
            ItemDefinition definition = CreateDefinition("Placed Item");

            ItemInstance instance = ItemInstance.Create(definition, 7, 2);

            Assert.AreEqual(7, instance.GridX);
            Assert.AreEqual(2, instance.GridY);
        }

        [Test]
        public void ItemInstance_Create_NullDefinition_Throws()
        {
            // A null here would fabricate a nameless instance that reads as "empty" while still
            // sitting in a cell — corruption the grid would happily stack a second item on top of.
            Assert.Throws<ArgumentNullException>(() => ItemInstance.Create(null, 0, 0));
        }

        private ItemDefinition CreateDefinition(string name, int width = 1, int height = 1)
        {
            _created = ScriptableObject.CreateInstance<ItemDefinition>();
            TestItemDefinitions.Write(_created, name, width: width, height: height);
            return _created;
        }
    }
}
