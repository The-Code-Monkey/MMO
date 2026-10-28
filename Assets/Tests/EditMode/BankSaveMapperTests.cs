using System.Collections.Generic;
using MMO.Inventory;
using MMO.Persistence;
using NUnit.Framework;

namespace MMO.Tests
{
    /// <summary>
    /// The personal bank's save mapping, isolated: empty cells are dropped, and a load rebuilds
    /// instances while dropping entries whose definition no longer resolves.
    /// </summary>
    public sealed class BankSaveMapperTests
    {
        [Test]
        public void CaptureBank_SkipsEmptyCells()
        {
            var bank = new List<ItemInstance>
            {
                new ItemInstance { ItemName = "Ore", InstanceId = "a", Width = 1, Height = 1, GridX = 1, GridY = 2 },
                ItemInstance.Empty,
            };

            List<ItemSaveData> saved = PlayerSaveMapper.CaptureBank(bank);

            Assert.AreEqual(1, saved.Count);
            Assert.AreEqual("Ore", saved[0].ItemName);
            Assert.AreEqual(1, saved[0].GridX);
            Assert.AreEqual(2, saved[0].GridY);
            Assert.AreEqual(-1, saved[0].EquipmentSlot);
        }

        [Test]
        public void ApplyBank_RebuildsItemsAndSkipsUnknownDefinitions()
        {
            TestItemDefinitions.Create("Ore", width: 1, height: 1);

            var saved = new List<ItemSaveData>
            {
                new ItemSaveData { ItemName = "Ore", InstanceId = "a", Width = 1, Height = 1, GridX = 3, GridY = 4 },
                new ItemSaveData { ItemName = "Missing", InstanceId = "b", Width = 1, Height = 1 },
                new ItemSaveData { ItemName = null },
            };

            PlayerSaveMapper.ApplyBank(saved, out List<ItemInstance> bank, out int skipped);

            Assert.AreEqual(1, bank.Count);
            Assert.AreEqual(2, skipped, "Both the unknown definition and the empty name are skipped.");
            Assert.AreEqual("Ore", bank[0].ItemName);
            Assert.AreEqual(3, bank[0].GridX);
            Assert.AreEqual(4, bank[0].GridY);
        }

        [Test]
        public void ApplyBank_Null_IsEmpty()
        {
            PlayerSaveMapper.ApplyBank(null, out List<ItemInstance> bank, out int skipped);

            Assert.AreEqual(0, bank.Count);
            Assert.AreEqual(0, skipped);
        }
    }
}
