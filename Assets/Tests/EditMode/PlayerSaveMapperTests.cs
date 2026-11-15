using System.Collections.Generic;
using MMO.Content;
using MMO.Gameplay;
using MMO.Inventory;
using MMO.Persistence;
using NUnit.Framework;
using UnityEngine;

namespace MMO.Tests
{
    /// <summary>
    /// The pure half of persistence: capture a live state into a <see cref="PlayerSaveRecord"/>
    /// and rebuild the two containers from one. Driven entirely through the mapper's public API
    /// (no networked components), so the pruning rules — unresolvable definitions and
    /// out-of-range equipment slots are dropped, never resurrected — are pinned here in EditMode.
    /// </summary>
    public sealed class PlayerSaveMapperTests
    {
        private const string SwordName = "MMOTest.Sword";
        private const string HelmName = "MMOTest.Helm";

        [TearDown]
        public void TearDown()
        {
            ItemDefinitionRegistry.Clear();
        }

        [Test]
        public void Capture_StoresVitalsPositionAndIdentity()
        {
            var record = Capture(level: 7, health: 60, mana: 40);

            Assert.AreEqual("u-1", record.UserId);
            Assert.AreEqual(7, record.Level);
            Assert.AreEqual(60, record.Health);
            Assert.AreEqual(40, record.Mana);
            Assert.AreEqual(new Vector3(1f, 2f, 3f), record.Position);
        }

        [Test]
        public void Capture_RoundTripsABagItem()
        {
            ItemInstance sword = ItemInstance.Create(TestItemDefinitions.Create(SwordName, 2, 1), 2, 3);

            PlayerSaveRecord record = Capture(bag: new[] { sword });

            Assert.AreEqual(1, record.Items.Count);
            Assert.AreEqual(SwordName, record.Items[0].ItemName);
            Assert.AreEqual(-1, record.Items[0].EquipmentSlot, "A bag item wears no slot.");
            Assert.AreEqual(2, record.Items[0].GridX);
            Assert.AreEqual(3, record.Items[0].GridY);
            Assert.AreEqual(2, record.Items[0].Width);
            Assert.AreEqual(1, record.Items[0].Height);

            PlayerSaveMapper.Apply(record, out List<ItemInstance> bag, out List<ItemInstance> equipment, out int skipped, out long gold);

            Assert.AreEqual(0, skipped);
            Assert.AreEqual(1, bag.Count);
            Assert.AreEqual(SwordName, bag[0].ItemName);
            Assert.AreEqual(2, bag[0].GridX);
            Assert.AreEqual(3, bag[0].GridY);
            Assert.IsEmpty(equipment);
        }

        [Test]
        public void Capture_RoundTripsAnEquippedItemIntoItsSlot()
        {
            TestItemDefinitions.Create(HelmName, equipSlot: EquipmentSlot.Head);

            var equipment = EquipmentList((int)EquipmentSlot.Head,
                ItemInstance.Create(ItemDefinitionRegistry.Resolve(HelmName), 0, 0));

            PlayerSaveRecord record = Capture(bag: null, equipment: equipment);

            Assert.AreEqual(1, record.Items.Count);
            Assert.AreEqual((int)EquipmentSlot.Head, record.Items[0].EquipmentSlot);

            PlayerSaveMapper.Apply(record, out List<ItemInstance> bag, out List<ItemInstance> equipmentOut, out int skipped, out _);

            Assert.AreEqual(0, skipped);
            Assert.IsEmpty(bag);
            Assert.AreEqual(HelmName, equipmentOut[(int)EquipmentSlot.Head].ItemName);
        }

        [Test]
        public void Capture_RoundTripsTheInstanceId()
        {
            ItemInstance sword = ItemInstance.Create(TestItemDefinitions.Create(SwordName), 0, 0);

            PlayerSaveRecord record = Capture(bag: new[] { sword });

            Assert.AreEqual(sword.InstanceId, record.Items[0].InstanceId);

            PlayerSaveMapper.Apply(record, out List<ItemInstance> bag, out _, out int skipped, out _);

            Assert.AreEqual(0, skipped);
            Assert.AreEqual(sword.InstanceId, bag[0].InstanceId,
                "The same physical item must keep its identity across a save/load.");
        }

        [Test]
        public void Apply_MintsAnInstanceIdForALegacySaveWithNone()
        {
            TestItemDefinitions.Create(SwordName);
            var legacy = Saved(SwordName);
            legacy.InstanceId = null;
            var record = new PlayerSaveRecord { Items = { legacy } };

            PlayerSaveMapper.Apply(record, out List<ItemInstance> bag, out _, out int skipped, out _);

            Assert.AreEqual(0, skipped);
            Assert.AreEqual(1, bag.Count);
            Assert.IsNotEmpty(bag[0].InstanceId,
                "An item from a save written before instance ids existed enters the world with a fresh one.");
        }

        [Test]
        public void Capture_SkipsEmptyCellsAndVacantSlots()
        {
            ItemInstance sword = ItemInstance.Create(TestItemDefinitions.Create(SwordName), 0, 0);

            PlayerSaveRecord record = Capture(
                bag: new[] { ItemInstance.Empty, sword },
                equipment: EquipmentList((int)EquipmentSlot.Chest, ItemInstance.Empty));

            Assert.AreEqual(1, record.Items.Count, "Empty bag cells and unused slots are not stored.");
            Assert.AreEqual(SwordName, record.Items[0].ItemName);
        }

        [Test]
        public void Apply_NullRecord_ProducesTwoEmptyContainers()
        {
            PlayerSaveMapper.Apply(null, out List<ItemInstance> bag, out List<ItemInstance> equipment, out int skipped, out long gold);

            Assert.AreEqual(0, skipped);
            Assert.AreEqual(0, gold);
            Assert.IsEmpty(bag);
            Assert.IsEmpty(equipment);
        }

[Test]
        public void Apply_DropsAnItemWhoseDefinitionNoLongerResolves()
        {
            var record = new PlayerSaveRecord { Items = { Saved(SwordName) } };

            PlayerSaveMapper.Apply(record, out List<ItemInstance> bag, out List<ItemInstance> equipment, out int skipped, out long gold);

            Assert.AreEqual(1, skipped, "A droppable definition is data to discard, not a ghost to spawn.");
            Assert.AreEqual(0, gold);
            Assert.IsEmpty(bag);
            Assert.IsEmpty(equipment);
        }

        [Test]
        public void Apply_DropsAnItemWithNoName()
        {
            var record = new PlayerSaveRecord { Items = { Saved(null) } };

            PlayerSaveMapper.Apply(record, out List<ItemInstance> bag, out _, out int skipped, out long gold);

            Assert.AreEqual(1, skipped);
            Assert.AreEqual(0, gold);
            Assert.IsEmpty(bag);
        }

[Test]
        public void Apply_DropsEquipmentFromASlotPastTheSlotCount()
        {
            TestItemDefinitions.Create(HelmName, equipSlot: EquipmentSlot.Head);
            var record = new PlayerSaveRecord
            {
                Items = { Saved(HelmName, EquipmentSlots.Count) },
            };

            PlayerSaveMapper.Apply(record, out List<ItemInstance> bag, out List<ItemInstance> equipment, out int skipped, out long gold);

            Assert.AreEqual(1, skipped, "A slot past the container must be pruned, not grown into.");
            Assert.AreEqual(0, gold);
            Assert.IsEmpty(bag);
            Assert.IsEmpty(equipment);
        }

[Test]
        public void Apply_PreservesBothContainersInOneRecord()
        {
            TestItemDefinitions.Create(SwordName);
            ItemInstance helm = ItemInstance.Create(
                TestItemDefinitions.Create(HelmName, equipSlot: EquipmentSlot.Head), 4, 5);
            ItemInstance sword = ItemInstance.Create(ItemDefinitionRegistry.Resolve(SwordName), 1, 2);

            PlayerSaveRecord record = Capture(
                bag: new[] { sword },
                equipment: EquipmentList((int)EquipmentSlot.Head, helm));

            PlayerSaveMapper.Apply(record, out List<ItemInstance> bag, out List<ItemInstance> equipment, out int skipped, out long gold);

            Assert.AreEqual(0, skipped);
            Assert.AreEqual(0, gold);
            Assert.AreEqual(1, bag.Count);
            Assert.AreEqual(SwordName, bag[0].ItemName);
            Assert.AreEqual(1, bag[0].GridX);
            Assert.AreEqual(HelmName, equipment[(int)EquipmentSlot.Head].ItemName);
            Assert.AreEqual(4, equipment[(int)EquipmentSlot.Head].GridX);
        }


        [Test]
        public void SchemaVersion_IsFive()
        {
            Assert.AreEqual(5, PlayerSaveRecord.CurrentSchemaVersion);
        }

        [Test]
        public void Capture_RoundTripsExperienceAndQuestContent()
        {
            var progress = new QuestProgressEntry
            {
                QuestId = "wolf_hunt",
                Kind = QuestObjectiveKind.Kill,
                TargetName = "Wolf",
                Count = 2,
            };

            PlayerSaveRecord record = Capture(
                experience: 42,
                completedQuestIds: new[] { "intro" },
                questProgress: new[] { progress });

            Assert.AreEqual(42, record.Experience);
            CollectionAssert.AreEqual(new[] { "intro" }, record.CompletedQuestIds);
            Assert.AreEqual(1, record.QuestProgress.Count);
            Assert.AreEqual("wolf_hunt", record.QuestProgress[0].QuestId);

            PlayerSaveMapper.Apply(
                record,
                out _,
                out _,
                out int skipped,
                out _,
                out int experience,
                out List<string> completed,
                out List<QuestProgressEntry> restored);

            Assert.AreEqual(0, skipped);
            Assert.AreEqual(42, experience);
            CollectionAssert.AreEqual(new[] { "intro" }, completed);
            Assert.AreEqual(1, restored.Count);
            Assert.AreEqual(QuestObjectiveKind.Kill, restored[0].Kind);
            Assert.AreEqual("Wolf", restored[0].TargetName);
            Assert.AreEqual(2, restored[0].Count);
        }

        [Test]
        public void Apply_LegacyRecord_DefaultsContent()
        {
            var legacy = new PlayerSaveRecord
            {
                UserId = "u-1",
                CompletedQuestIds = null,
                QuestProgress = null,
            };

            PlayerSaveMapper.Apply(
                legacy,
                out _,
                out _,
                out _,
                out _,
                out int experience,
                out List<string> completed,
                out List<QuestProgressEntry> progress);

            Assert.AreEqual(0, experience);
            Assert.IsEmpty(completed);
            Assert.IsEmpty(progress);
        }

        private static PlayerSaveRecord Capture(
            int level = 1,
            int health = PlayerStats.MaxHealth,
            int mana = PlayerStats.MaxMana,
            long gold = 0,
            IReadOnlyList<ItemInstance> bag = null,
            IReadOnlyList<ItemInstance> equipment = null,
            int experience = 0,
            IReadOnlyList<string> completedQuestIds = null,
            IReadOnlyList<QuestProgressEntry> questProgress = null)
        {
            return PlayerSaveMapper.Capture(
                "u-1", level, health, mana, gold, new Vector3(1f, 2f, 3f), bag, equipment,
                experience, completedQuestIds, questProgress);
        }

        private static ItemSaveData Saved(string name, int equipmentSlot = -1, int gridX = 0, int gridY = 0)
        {
            return new ItemSaveData
            {
                ItemName = name,
                EquipmentSlot = equipmentSlot,
                GridX = gridX,
                GridY = gridY,
                Width = 1,
                Height = 1,
            };
        }

        private static List<ItemInstance> EquipmentList(int slot, params ItemInstance[] entries)
        {
            var list = new List<ItemInstance> { ItemInstance.Empty, ItemInstance.Empty, ItemInstance.Empty, ItemInstance.Empty, ItemInstance.Empty };
            for (int i = 0; i < entries.Length; i++)
                list[slot] = entries[i];
            return list;
        }
    }
}