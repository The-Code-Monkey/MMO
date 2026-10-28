using System.Collections.Generic;
using MMO.Banking;
using MMO.Inventory;
using NUnit.Framework;

namespace MMO.Tests
{
    /// <summary>
    /// The personal bank's grid rules, isolated: cell lookup and free-space checks for a deposit
    /// or a withdrawal. No server, no storage.
    /// </summary>
    public sealed class PersonalBankRulesTests
    {
        private static ItemInstance Item(string name, int width = 1, int height = 1, int x = 0, int y = 0)
            => new ItemInstance { ItemName = name, Width = width, Height = height, GridX = x, GridY = y };

        [Test]
        public void ItemAt_FreeCell_IsEmpty()
            => Assert.IsTrue(PersonalBankRules.ItemAt(new List<ItemInstance>(), 0, 0).IsEmpty);

        [Test]
        public void ItemAt_FindsTheItemCoveringTheCell()
        {
            var bank = new List<ItemInstance> { Item("Sword", 2, 2, 1, 1) };

            Assert.AreEqual("Sword", PersonalBankRules.ItemAt(bank, 1, 1).ItemName);
            Assert.AreEqual("Sword", PersonalBankRules.ItemAt(bank, 2, 2).ItemName);
            Assert.IsTrue(PersonalBankRules.ItemAt(bank, 0, 0).IsEmpty);
        }

        [Test]
        public void BankHasRoom_EmptyBank_IsTrue()
            => Assert.IsTrue(PersonalBankRules.BankHasRoom(new List<ItemInstance>(), Item("Ore")));

        [Test]
        public void TryFindBankCell_ReturnsTopLeftFreeCell()
        {
            var bank = new List<ItemInstance> { Item("Ore", 1, 1, 0, 0) };

            Assert.IsTrue(PersonalBankRules.TryFindBankCell(bank, Item("Sword", 2, 2), out int x, out int y));
            Assert.AreEqual(1, x);
            Assert.AreEqual(0, y);
        }

        [Test]
        public void BankHasRoom_WhenFull_IsFalse()
        {
            var bank = new List<ItemInstance>();
            for (int y = 0; y < PersonalBankRules.BankHeight; y++)
                for (int x = 0; x < PersonalBankRules.BankWidth; x++)
                    bank.Add(Item("Ore", 1, 1, x, y));

            Assert.IsFalse(PersonalBankRules.BankHasRoom(bank, Item("Sword")));
        }

        [Test]
        public void BagHasRoom_EmptyBag_IsTrue()
            => Assert.IsTrue(PersonalBankRules.BagHasRoom(new List<ItemInstance>(), Item("Ore")));
    }
}
