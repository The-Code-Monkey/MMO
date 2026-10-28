using System;
using MMO.Economy;
using MMO.Inventory;
using MMO.Social;
using NUnit.Framework;

namespace MMO.Tests
{
    /// <summary>
    /// The guild-bank rules, isolated: the persistent tab grid, the per-rank deposit/withdraw
    /// permission model and the withdrawal quota. No server, no storage, no socket — the same
    /// "test the server logic with mock state first" rule the rest of the suite follows.
    /// </summary>
    public sealed class GuildBankRulesTests
    {
        private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        private static GuildRecord Guild(params (string id, GuildRank rank)[] members)
        {
            var guild = new GuildRecord { GuildId = "g1", Name = "Raiders", LeaderId = members[0].id };
            long tick = 1;
            foreach ((string id, GuildRank rank) member in members)
            {
                guild.Members.Add(new GuildMemberRecord
                {
                    AccountId = member.id,
                    Rank = (int)member.rank,
                    JoinedAtTicks = tick++,
                });
            }

            return guild;
        }

        private static ItemInstance Item(string name = "Sword", int width = 1, int height = 1)
            => new ItemInstance { ItemName = name, Width = width, Height = height };

        [Test]
        public void CreateDefault_HasTheDefaultTabsAndARankedPermissionSet()
        {
            GuildBankRecord bank = GuildBankRules.CreateDefault("g1");

            Assert.AreEqual("g1", bank.GuildId);
            Assert.AreEqual(GuildBankRules.DefaultTabCount, bank.Tabs.Count);
            Assert.AreEqual(3, bank.Permissions.Count);
            Assert.AreEqual(GuildRank.Member, (GuildRank)bank.Permissions[0].Rank);
            Assert.AreEqual(GuildRank.Leader, (GuildRank)bank.Permissions[2].Rank);
        }

        [Test]
        public void EnsureDefaults_RepairsATablessBank()
        {
            var bank = new GuildBankRecord { GuildId = "g1" };

            GuildBankRules.EnsureDefaults(bank);

            Assert.AreEqual(GuildBankRules.DefaultTabCount, bank.Tabs.Count);
            Assert.AreEqual(3, bank.Permissions.Count);
        }

        [Test]
        public void CanView_NonMember_IsRefused()
        {
            GuildRecord guild = Guild(("a", GuildRank.Leader));

            Assert.AreEqual(GuildBankRule.NotInGuild, GuildBankRules.CanView(guild, "b"));
            Assert.AreEqual(GuildBankRule.Ok, GuildBankRules.CanView(guild, "a"));
            Assert.AreEqual(GuildBankRule.NoGuild, GuildBankRules.CanView(null, "a"));
        }

        [Test]
        public void CanDeposit_MemberWithRoom_IsAllowed_AndDepositPlacesTheItem()
        {
            GuildRecord guild = Guild(("a", GuildRank.Leader), ("b", GuildRank.Member));
            GuildBankRecord bank = GuildBankRules.CreateDefault("g1");
            ItemInstance item = Item(width: 2, height: 2);

            Assert.AreEqual(GuildBankRule.Ok, GuildBankRules.CanDeposit(guild, bank, "b", 0, item));

            Assert.IsTrue(GuildBankRules.Deposit(bank, 0, item));
            GuildBankTab tab = GuildBankRules.FindTab(bank, 0);
            Assert.AreEqual(1, tab.Items.Count);
            Assert.AreEqual("Sword", tab.Items[0].ItemName);
        }

        [Test]
        public void CanDeposit_WhenTheRankMayNotDeposit_IsRefused()
        {
            GuildRecord guild = Guild(("a", GuildRank.Leader), ("b", GuildRank.Member));
            GuildBankRecord bank = GuildBankRules.CreateDefault("g1");
            GuildBankRules.SetPermission(bank, GuildRank.Member, allowDeposit: false, allowWithdraw: true, withdrawLimit: 1);

            Assert.AreEqual(GuildBankRule.NotPermitted, GuildBankRules.CanDeposit(guild, bank, "b", 0, Item()));
        }

        [Test]
        public void CanDeposit_NonMember_IsRefused()
        {
            GuildRecord guild = Guild(("a", GuildRank.Leader));
            GuildBankRecord bank = GuildBankRules.CreateDefault("g1");

            Assert.AreEqual(GuildBankRule.NotInGuild, GuildBankRules.CanDeposit(guild, bank, "ghost", 0, Item()));
        }

        [Test]
        public void CanDeposit_InvalidItem_IsRefused()
        {
            GuildRecord guild = Guild(("a", GuildRank.Leader));
            GuildBankRecord bank = GuildBankRules.CreateDefault("g1");

            Assert.AreEqual(GuildBankRule.InvalidItem, GuildBankRules.CanDeposit(guild, bank, "a", 0, ItemInstance.Empty));
        }

        [Test]
        public void CanDeposit_ToAMissingTab_IsRefused()
        {
            GuildRecord guild = Guild(("a", GuildRank.Leader));
            GuildBankRecord bank = GuildBankRules.CreateDefault("g1");

            Assert.AreEqual(GuildBankRule.InvalidTab, GuildBankRules.CanDeposit(guild, bank, "a", 99, Item()));
        }

        [Test]
        public void CanDeposit_WhenTheTabIsFull_ReportsTabFull()
        {
            GuildRecord guild = Guild(("a", GuildRank.Leader));
            GuildBankRecord bank = GuildBankRules.CreateDefault("g1");
            GuildBankTab tab = GuildBankRules.FindTab(bank, 0);

            // Fill every cell of the tab with one-cell items.
            for (int y = 0; y < GuildBankRules.TabHeight; y++)
                for (int x = 0; x < GuildBankRules.TabWidth; x++)
                    tab.Items.Add(new ItemInstance { ItemName = "Rock", GridX = x, GridY = y, Width = 1, Height = 1 });

            Assert.AreEqual(GuildBankRule.TabFull, GuildBankRules.CanDeposit(guild, bank, "a", 0, Item()));
        }

        [Test]
        public void CanWithdraw_WhenTheRankMayNotWithdraw_IsRefused()
        {
            GuildRecord guild = Guild(("a", GuildRank.Leader), ("b", GuildRank.Member));
            GuildBankRecord bank = GuildBankRules.CreateDefault("g1");
            GuildBankRules.SetPermission(bank, GuildRank.Member, allowDeposit: true, allowWithdraw: false, withdrawLimit: 0);
            GuildBankRules.Deposit(bank, 0, Item());

            Assert.AreEqual(GuildBankRule.NotPermitted, GuildBankRules.CanWithdraw(guild, bank, "b", 0, 0, 0, Now));
        }

        [Test]
        public void CanWithdraw_FromAnEmptyCell_ReportsNothingToWithdraw()
        {
            GuildRecord guild = Guild(("a", GuildRank.Leader));
            GuildBankRecord bank = GuildBankRules.CreateDefault("g1");

            Assert.AreEqual(GuildBankRule.NothingToWithdraw, GuildBankRules.CanWithdraw(guild, bank, "a", 0, 0, 0, Now));
        }

        [Test]
        public void CanWithdraw_WhenTheQuotaIsSpent_ReportsLimitReached()
        {
            GuildRecord guild = Guild(("a", GuildRank.Leader), ("b", GuildRank.Member));
            GuildBankRecord bank = GuildBankRules.CreateDefault("g1");   // member limit = 1
            GuildBankRules.Deposit(bank, 0, Item("A"));
            GuildBankRules.Deposit(bank, 0, Item("B"));

            Assert.AreEqual(GuildBankRule.Ok, GuildBankRules.CanWithdraw(guild, bank, "b", 0, 0, 0, Now));
            Assert.IsTrue(GuildBankRules.Withdraw(guild, bank, "b", 0, 0, 0, Now, out _));

            // The second withdrawal in the same period is refused.
            Assert.AreEqual(GuildBankRule.WithdrawLimitReached, GuildBankRules.CanWithdraw(guild, bank, "b", 0, 1, 0, Now));
        }

        [Test]
        public void CanWithdraw_AfterTheQuotaPeriodRollsOver_IsAllowedAgain()
        {
            GuildRecord guild = Guild(("a", GuildRank.Leader), ("b", GuildRank.Member));
            GuildBankRecord bank = GuildBankRules.CreateDefault("g1");
            GuildBankRules.Deposit(bank, 0, Item("A"));
            GuildBankRules.Deposit(bank, 0, Item("B"));

            Assert.IsTrue(GuildBankRules.Withdraw(guild, bank, "b", 0, 0, 0, Now, out _));

            DateTimeOffset later = Now + GuildBankRules.QuotaPeriod + TimeSpan.FromMinutes(1);
            Assert.AreEqual(GuildBankRule.Ok, GuildBankRules.CanWithdraw(guild, bank, "b", 0, 1, 0, later));
        }

        [Test]
        public void Withdraw_RemovesTheItemAndSpendsOneQuotaUnit()
        {
            GuildRecord guild = Guild(("a", GuildRank.Leader));
            GuildBankRecord bank = GuildBankRules.CreateDefault("g1");
            GuildBankRules.Deposit(bank, 0, Item("Axe"));

            bool removed = GuildBankRules.Withdraw(guild, bank, "a", 0, 0, 0, Now, out ItemInstance item);

            Assert.IsTrue(removed);
            Assert.AreEqual("Axe", item.ItemName);
            Assert.AreEqual(0, GuildBankRules.FindTab(bank, 0).Items.Count);
        }

        [Test]
        public void CanEditPermissions_NonLeader_IsRefused()
        {
            GuildRecord guild = Guild(("a", GuildRank.Leader), ("b", GuildRank.Officer));

            Assert.AreEqual(GuildBankRule.NotPermitted, GuildBankRules.CanEditPermissions(guild, "b"));
            Assert.AreEqual(GuildBankRule.Ok, GuildBankRules.CanEditPermissions(guild, "a"));
        }

        [Test]
        public void SetPermission_ReplacesTheRankEntry_AndClampsTheLimit()
        {
            GuildBankRecord bank = GuildBankRules.CreateDefault("g1");

            GuildBankRules.SetPermission(bank, GuildRank.Officer, allowDeposit: false, allowWithdraw: true, withdrawLimit: -5);

            GuildBankPermission permission = GuildBankRules.PermissionFor(bank, GuildRank.Officer);
            Assert.IsFalse(permission.AllowDeposit);
            Assert.AreEqual(GuildBankRules.UnlimitedWithdraw, permission.WithdrawLimit);
            Assert.AreEqual(3, bank.Permissions.Count, "Editing must not add a duplicate rank entry.");
        }

        [Test]
        public void IsValidPermission_RejectsARankThatCanDoNothing()
        {
            Assert.IsFalse(GuildBankRules.IsValidPermission(false, false, 1));
            Assert.IsFalse(GuildBankRules.IsValidPermission(true, false, -2));
            Assert.IsTrue(GuildBankRules.IsValidPermission(true, false, 0));
            Assert.IsTrue(GuildBankRules.IsValidPermission(true, true, GuildBankRules.UnlimitedWithdraw));
        }

        [Test]
        public void ItemAt_ReturnsTheItemCoveringTheCell()
        {
            GuildBankTab tab = new GuildBankTab { Index = 0 };
            tab.Items.Add(new ItemInstance { ItemName = "Shield", GridX = 2, GridY = 1, Width = 2, Height = 2 });

            Assert.AreEqual("Shield", GuildBankRules.ItemAt(tab, 2, 1).ItemName);
            Assert.AreEqual("Shield", GuildBankRules.ItemAt(tab, 3, 2).ItemName);
            Assert.IsTrue(GuildBankRules.ItemAt(tab, 0, 0).IsEmpty);
        }

        [Test]
        public void EnsureDefaults_InitialisesTheCurrencyVault()
        {
            var bank = new GuildBankRecord { GuildId = "g1" };

            GuildBankRules.EnsureDefaults(bank);

            Assert.IsNotNull(bank.Vault);
            Assert.AreEqual(0, bank.Vault.Count);
        }

        [Test]
        public void CanDepositCurrency_Member_IsAllowed()
        {
            GuildRecord guild = Guild(("a", GuildRank.Member));
            GuildBankRecord bank = GuildBankRules.CreateDefault("g1");

            Assert.AreEqual(
                GuildBankRule.Ok,
                GuildBankRules.CanDepositCurrency(guild, bank, "a", CurrencyIds.Gold, 50, Now));
        }

        [Test]
        public void CanDepositCurrency_NonMember_IsRefused()
        {
            GuildRecord guild = Guild(("a", GuildRank.Member));
            GuildBankRecord bank = GuildBankRules.CreateDefault("g1");

            Assert.AreEqual(
                GuildBankRule.NotInGuild,
                GuildBankRules.CanDepositCurrency(guild, bank, "b", CurrencyIds.Gold, 50, Now));
        }

        [Test]
        public void CanDepositCurrency_NonPositiveAmount_IsRefused()
        {
            GuildRecord guild = Guild(("a", GuildRank.Member));
            GuildBankRecord bank = GuildBankRules.CreateDefault("g1");

            Assert.AreEqual(
                GuildBankRule.InvalidAmount,
                GuildBankRules.CanDepositCurrency(guild, bank, "a", CurrencyIds.Gold, 0, Now));
        }

        [Test]
        public void CanDepositCurrency_RankWithoutDeposit_IsRefused()
        {
            GuildRecord guild = Guild(("a", GuildRank.Member));
            GuildBankRecord bank = GuildBankRules.CreateDefault("g1");
            GuildBankRules.SetPermission(bank, GuildRank.Member, allowDeposit: false, allowWithdraw: true, withdrawLimit: 1);

            Assert.AreEqual(
                GuildBankRule.NotPermitted,
                GuildBankRules.CanDepositCurrency(guild, bank, "a", CurrencyIds.Gold, 5, Now));
        }

        [Test]
        public void DepositCurrency_CreditsTheVault()
        {
            GuildBankRecord bank = GuildBankRules.CreateDefault("g1");

            bool deposited = GuildBankRules.DepositCurrency(bank, CurrencyIds.Gold, 25, GuildRank.Member, Now);

            Assert.IsTrue(deposited);
            Assert.AreEqual(25, WalletRules.BalanceOf(bank.Vault, CurrencyIds.Gold));
        }

        [Test]
        public void CanWithdrawCurrency_MoreThanTheVaultHolds_IsRefused()
        {
            GuildRecord guild = Guild(("a", GuildRank.Leader));
            GuildBankRecord bank = GuildBankRules.CreateDefault("g1");
            GuildBankRules.DepositCurrency(bank, CurrencyIds.Gold, 10, GuildRank.None, Now);

            Assert.AreEqual(
                GuildBankRule.InsufficientFunds,
                GuildBankRules.CanWithdrawCurrency(guild, bank, "a", CurrencyIds.Gold, 11, Now));
        }

        [Test]
        public void WithdrawCurrency_DebitsTheVault()
        {
            GuildRecord guild = Guild(("a", GuildRank.Leader));
            GuildBankRecord bank = GuildBankRules.CreateDefault("g1");
            GuildBankRules.DepositCurrency(bank, CurrencyIds.Gold, 40, GuildRank.None, Now);

            Assert.AreEqual(
                GuildBankRule.Ok,
                GuildBankRules.CanWithdrawCurrency(guild, bank, "a", CurrencyIds.Gold, 15, Now));
            Assert.IsTrue(GuildBankRules.WithdrawCurrency(bank, CurrencyIds.Gold, 15, GuildRank.Leader, Now));

            Assert.AreEqual(25, WalletRules.BalanceOf(bank.Vault, CurrencyIds.Gold));
        }

        [Test]
        public void CanWithdrawCurrency_RankWithoutWithdraw_IsRefused()
        {
            GuildRecord guild = Guild(("a", GuildRank.Member));
            GuildBankRecord bank = GuildBankRules.CreateDefault("g1");
            GuildBankRules.DepositCurrency(bank, CurrencyIds.Gold, 10, GuildRank.None, Now);
            GuildBankRules.SetPermission(bank, GuildRank.Member, allowDeposit: true, allowWithdraw: false, withdrawLimit: 1);

            Assert.AreEqual(
                GuildBankRule.NotPermitted,
                GuildBankRules.CanWithdrawCurrency(guild, bank, "a", CurrencyIds.Gold, 5, Now));
        }
    }
}
