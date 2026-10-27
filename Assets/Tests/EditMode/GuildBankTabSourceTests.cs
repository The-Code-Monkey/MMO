using System;
using System.Collections.Generic;
using MMO.Inventory;
using MMO.Social;
using MMO.UI;
using NUnit.Framework;

namespace MMO.Tests
{
    /// <summary>
    /// The guild bank window's pure parts, isolated: the tab source a grid binds to, the bag
    /// source that turns a right-click into a deposit, and the permission editor's control-value
    /// mapping. No MonoBehaviour, no server, no socket.
    /// </summary>
    public sealed class GuildBankTabSourceTests
    {
        private static ItemInstance Item(string name = "Sword", int x = 0, int y = 0)
            => new ItemInstance { ItemName = name, GridX = x, GridY = y, Width = 1, Height = 1 };

        private static GuildBankRecord BankWith(params (int tab, ItemInstance item)[] items)
        {
            GuildBankRecord bank = GuildBankRules.CreateDefault("g1");
            foreach ((int tab, ItemInstance item) entry in items)
                bank.Tabs[entry.tab].Items.Add(entry.item);

            return bank;
        }

        [Test]
        public void Geometry_IsTheBankTabGrid()
        {
            var source = new GuildBankTabSource();
            Assert.AreEqual(GuildBankRules.TabWidth, source.GridWidth);
            Assert.AreEqual(GuildBankRules.TabHeight, source.GridHeight);
        }

        [Test]
        public void Tabs_AreEmptyBeforeABankIsBound()
        {
            var source = new GuildBankTabSource();
            Assert.AreEqual(0, source.Tabs.Count);
            Assert.IsTrue(source.GetItemAt(0, 0).IsEmpty);
        }

        [Test]
        public void GetItemAt_ReturnsTheItemCoveringTheCell()
        {
            var source = new GuildBankTabSource(BankWith((0, Item("Sword", 2, 1))));
            Assert.AreEqual("Sword", source.GetItemAt(2, 1).ItemName);
            Assert.IsTrue(source.GetItemAt(0, 0).IsEmpty);
        }

        [Test]
        public void SetBank_RaisesContainersChanged_AndReadsTheNewBank()
        {
            var source = new GuildBankTabSource();
            int raised = 0;
            source.ContainersChanged += () => raised++;

            source.SetBank(BankWith((0, Item("Axe"))));

            Assert.AreEqual(1, raised);
            Assert.AreEqual("Axe", source.GetItemAt(0, 0).ItemName);
        }

        [Test]
        public void SelectTab_RaisesContainersChanged_AndReadsTheNewTab()
        {
            GuildBankRecord bank = BankWith((0, Item("Sword")), (1, Item("Shield")));
            var source = new GuildBankTabSource(bank);

            int raised = 0;
            source.ContainersChanged += () => raised++;

            source.SelectTab(1);

            Assert.AreEqual(1, raised);
            Assert.AreEqual(1, source.TabIndex);
            Assert.AreEqual("Shield", source.GetItemAt(0, 0).ItemName);
        }

        [Test]
        public void SelectTab_SameTab_DoesNotRaise()
        {
            var source = new GuildBankTabSource(BankWith((0, Item())));
            int raised = 0;
            source.ContainersChanged += () => raised++;

            source.SelectTab(0);

            Assert.AreEqual(0, raised);
        }

        [Test]
        public void SelectTab_UnknownOrdinal_ClampsToTheFirstTab()
        {
            var source = new GuildBankTabSource(BankWith((0, Item())));
            source.SelectTab(99);
            Assert.AreEqual(0, source.TabIndex);
        }

        [Test]
        public void SetBank_WhenTabVanishes_ClampsToAnAvailableTab()
        {
            var source = new GuildBankTabSource(BankWith((0, Item()), (1, Item())));
            source.SelectTab(1);

            GuildBankRecord singleTab = BankWith((0, Item("Sword")));
            singleTab.Tabs.RemoveRange(1, singleTab.Tabs.Count - 1);
            source.SetBank(singleTab);

            Assert.AreEqual(0, source.TabIndex);
        }

        [Test]
        public void RequestEquip_FiresWithdrawRequestedWithTabAndCell()
        {
            var source = new GuildBankTabSource(BankWith((0, Item()), (1, Item())));
            source.SelectTab(1);

            int firedTab = -1, firedX = -1, firedY = -1;
            source.WithdrawRequested += (tab, x, y) => { firedTab = tab; firedX = x; firedY = y; };

            source.RequestEquip(3, 4);

            Assert.AreEqual(1, firedTab);
            Assert.AreEqual(3, firedX);
            Assert.AreEqual(4, firedY);
        }

        // ---- DepositBagSource (the frame's bag half) ----

        [Test]
        public void DepositBagSource_GeometryAndItemsComeFromTheBag()
        {
            var bag = new FakeBag { GridWidth = 6, GridHeight = 5 };
            bag.Set(1, 1, Item("Potion", 1, 1));

            var source = new GuildBankFrameView.DepositBagSource(bag, () => 0);

            Assert.AreEqual(6, source.GridWidth);
            Assert.AreEqual(5, source.GridHeight);
            Assert.AreEqual("Potion", source.GetItemAt(1, 1).ItemName);
        }

        [Test]
        public void DepositBagSource_RequestEquip_FiresDepositWithTheOpenTabAndCell()
        {
            var source = new GuildBankFrameView.DepositBagSource(new FakeBag(), () => 2);

            int firedTab = -1, firedX = -1, firedY = -1;
            source.DepositRequested += (tab, x, y) => { firedTab = tab; firedX = x; firedY = y; };

            source.RequestEquip(3, 4);

            Assert.AreEqual(2, firedTab);
            Assert.AreEqual(3, firedX);
            Assert.AreEqual(4, firedY);
        }

        [Test]
        public void DepositBagSource_ForwardsBagChanges_UntilUnsubscribed()
        {
            var bag = new FakeBag();
            var source = new GuildBankFrameView.DepositBagSource(bag, () => 0);

            int raised = 0;
            source.ContainersChanged += () => raised++;

            bag.RaiseChanged();
            Assert.AreEqual(1, raised);

            source.Unsubscribe();
            bag.RaiseChanged();
            Assert.AreEqual(1, raised);
        }

        // ---- GuildRankPermissionEditor.Model ----

        [Test]
        public void LimitText_UnlimitedIsBlank_OthersAreNumbers()
        {
            Assert.AreEqual(string.Empty, GuildRankPermissionEditor.Model.LimitText(
                new GuildBankPermission { WithdrawLimit = GuildBankRules.UnlimitedWithdraw }));
            Assert.AreEqual("7", GuildRankPermissionEditor.Model.LimitText(
                new GuildBankPermission { WithdrawLimit = 7 }));
        }

        [Test]
        public void TryParseLimit_BlankMeansUnlimited()
        {
            Assert.IsTrue(GuildRankPermissionEditor.Model.TryParseLimit("  ", out int limit));
            Assert.AreEqual(GuildBankRules.UnlimitedWithdraw, limit);
        }

        [Test]
        public void TryParseLimit_ParsesANumber()
        {
            Assert.IsTrue(GuildRankPermissionEditor.Model.TryParseLimit("12", out int limit));
            Assert.AreEqual(12, limit);
        }

        [Test]
        public void TryParseLimit_RejectsGarbageAndValuesBelowTheSentinel()
        {
            Assert.IsFalse(GuildRankPermissionEditor.Model.TryParseLimit("lots", out _));
            Assert.IsFalse(GuildRankPermissionEditor.Model.TryParseLimit("-5", out _));
        }

        [Test]
        public void IsValid_RequiresAtLeastOneAction()
        {
            Assert.IsFalse(GuildRankPermissionEditor.Model.IsValid(false, false, 0));
            Assert.IsTrue(GuildRankPermissionEditor.Model.IsValid(true, false, 0));
        }

        private sealed class FakeBag : IInventorySource
        {
            private readonly Dictionary<(int, int), ItemInstance> _cells = new Dictionary<(int, int), ItemInstance>();

            public int GridWidth { get; set; } = 1;
            public int GridHeight { get; set; } = 1;

            public event Action ContainersChanged;

            public void Set(int x, int y, ItemInstance item) => _cells[(x, y)] = item;

            public void RaiseChanged() => ContainersChanged?.Invoke();

            public ItemInstance GetItemAt(int gridX, int gridY)
                => _cells.TryGetValue((gridX, gridY), out ItemInstance item) ? item : ItemInstance.Empty;

            public void RequestEquip(int gridX, int gridY)
            {
            }
        }
    }
}