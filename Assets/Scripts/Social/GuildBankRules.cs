using System;
using System.Collections.Generic;
using MMO.Inventory;

namespace MMO.Social
{
    /// <summary>The outcome of a guild-bank decision. <see cref="Ok"/> is the only success.</summary>
    public enum GuildBankRule
    {
        Ok,
        NoGuild,
        NotInGuild,
        NoBank,
        InvalidTab,
        InvalidItem,
        InvalidPermission,
        NotPermitted,
        TabFull,
        NothingToWithdraw,
        BagFull,
        WithdrawLimitReached,
        BackendUnavailable,
        Conflict,
    }

    /// <summary>
    /// Every guild-bank decision as a pure function, the bank half of <see cref="GuildRules"/>:
    /// the zone server validates here before it writes, and <see cref="SocialHubService"/> reuses
    /// the same checks under its CAS retry loop. No network, storage, or Unity dependency, so
    /// every branch is reachable from an EditMode test.
    ///
    /// <para>
    /// The tab grid is laid out with <see cref="MMO.Inventory.InventoryGrid"/>, the same pure
    /// geometry the player bag uses, so a bank cell and a bag cell mean the same thing.
    /// </para>
    /// </summary>
    public static class GuildBankRules
    {
        public const int TabWidth = 8;
        public const int TabHeight = 6;
        public const int DefaultTabCount = 4;
        public const int MaxTabs = 6;
        public const int MaxTabNameLength = 24;

        /// <summary>A negative <see cref="GuildBankPermission.WithdrawLimit"/> means no cap.</summary>
        public const int UnlimitedWithdraw = -1;

        /// <summary>How long a rank's withdrawal allowance lasts before it refills.</summary>
        public static readonly TimeSpan QuotaPeriod = TimeSpan.FromHours(1);

        private static readonly InventoryGrid Grid = new InventoryGrid(TabWidth, TabHeight);

        /// <summary>Builds the bank a fresh guild starts with: default tabs and default rank rules.</summary>
        public static GuildBankRecord CreateDefault(string guildId)
        {
            var bank = new GuildBankRecord { GuildId = guildId };

            for (int i = 0; i < DefaultTabCount; i++)
                bank.Tabs.Add(new GuildBankTab { Index = i, Name = $"Tab {i + 1}" });

            bank.Permissions.Add(DefaultPermission(GuildRank.Member));
            bank.Permissions.Add(DefaultPermission(GuildRank.Officer));
            bank.Permissions.Add(DefaultPermission(GuildRank.Leader));
            return bank;
        }

        /// <summary>
        /// Repairs a loaded bank: fills in any missing default tabs and any missing rank
        /// permission, so a bank written before a rule existed still opens. Idempotent.
        /// </summary>
        public static void EnsureDefaults(GuildBankRecord bank)
        {
            if (bank == null)
                return;

            bank.Tabs ??= new List<GuildBankTab>();
            bank.Permissions ??= new List<GuildBankPermission>();

            for (int i = 0; i < DefaultTabCount; i++)
            {
                if (FindTab(bank, i) == null)
                    bank.Tabs.Add(new GuildBankTab { Index = i, Name = $"Tab {i + 1}" });
            }

            foreach (GuildRank rank in new[] { GuildRank.Member, GuildRank.Officer, GuildRank.Leader })
            {
                if (!HasPermission(bank, rank))
                    bank.Permissions.Add(DefaultPermission(rank));
            }
        }

        /// <summary>The tab with this ordinal, or null when the bank has no such tab.</summary>
        public static GuildBankTab FindTab(GuildBankRecord bank, int index)
        {
            if (bank?.Tabs == null)
                return null;

            for (int i = 0; i < bank.Tabs.Count; i++)
            {
                if (bank.Tabs[i].Index == index)
                    return bank.Tabs[i];
            }

            return null;
        }

        /// <summary>The permission entry for a rank, falling back to the defaults when absent.</summary>
        public static GuildBankPermission PermissionFor(GuildBankRecord bank, GuildRank rank)
        {
            if (bank?.Permissions != null)
            {
                for (int i = 0; i < bank.Permissions.Count; i++)
                {
                    if (GuildRules.RankOf(new GuildMemberRecord { Rank = bank.Permissions[i].Rank }) == rank)
                        return bank.Permissions[i];
                }
            }

            return DefaultPermission(rank);
        }

        /// <summary>Whether the bank has an explicit permission entry for this rank.</summary>
        public static bool HasPermission(GuildBankRecord bank, GuildRank rank)
        {
            if (bank?.Permissions == null)
                return false;

            for (int i = 0; i < bank.Permissions.Count; i++)
            {
                if (bank.Permissions[i].Rank == (int)rank)
                    return true;
            }

            return false;
        }

        /// <summary>The permission defaults: members deposit freely but withdraw sparingly.</summary>
        public static GuildBankPermission DefaultPermission(GuildRank rank)
        {
            switch (rank)
            {
                case GuildRank.Leader:
                    return NewPermission(rank, allowDeposit: true, allowWithdraw: true, withdrawLimit: UnlimitedWithdraw);
                case GuildRank.Officer:
                    return NewPermission(rank, allowDeposit: true, allowWithdraw: true, withdrawLimit: 10);
                default:
                    return NewPermission(rank, allowDeposit: true, allowWithdraw: true, withdrawLimit: 1);
            }
        }

        public static bool RankAllowsDeposit(GuildBankRecord bank, GuildRank rank)
            => PermissionFor(bank, rank).AllowDeposit;

        public static bool RankAllowsWithdraw(GuildBankRecord bank, GuildRank rank)
            => PermissionFor(bank, rank).AllowWithdraw;

        /// <summary>Whether the requester may look at the bank at all: any guild member may.</summary>
        public static GuildBankRule CanView(GuildRecord guild, string accountId)
        {
            if (guild == null)
                return GuildBankRule.NoGuild;

            return GuildRules.IsMember(guild, accountId) ? GuildBankRule.Ok : GuildBankRule.NotInGuild;
        }

        /// <summary>
        /// Whether the requester may deposit <paramref name="item"/> into the given tab. Order:
        /// guild/member, bank, item, rank permission, tab exists, tab has room.
        /// </summary>
        public static GuildBankRule CanDeposit(
            GuildRecord guild, GuildBankRecord bank, string accountId, int tabIndex, ItemInstance item)
        {
            if (guild == null)
                return GuildBankRule.NoGuild;

            GuildMemberRecord member = GuildRules.FindMember(guild, accountId);
            if (member.AccountId == null)
                return GuildBankRule.NotInGuild;

            if (bank == null)
                return GuildBankRule.NoBank;

            if (item.IsEmpty || item.Width < 1 || item.Height < 1)
                return GuildBankRule.InvalidItem;

            if (!RankAllowsDeposit(bank, GuildRules.RankOf(member)))
                return GuildBankRule.NotPermitted;

            GuildBankTab tab = FindTab(bank, tabIndex);
            if (tab == null)
                return GuildBankRule.InvalidTab;

            if (!HasFreeSpace(tab, item.Width, item.Height))
                return GuildBankRule.TabFull;

            return GuildBankRule.Ok;
        }

        /// <summary>
        /// Whether the requester may withdraw the item at a tab cell. Order: guild/member, bank,
        /// rank permission, quota, tab/cell. <paramref name="now"/> drives the lazy quota reset.
        /// </summary>
        public static GuildBankRule CanWithdraw(
            GuildRecord guild, GuildBankRecord bank, string accountId, int tabIndex, int gridX, int gridY,
            DateTimeOffset now)
        {
            if (guild == null)
                return GuildBankRule.NoGuild;

            GuildMemberRecord member = GuildRules.FindMember(guild, accountId);
            if (member.AccountId == null)
                return GuildBankRule.NotInGuild;

            if (bank == null)
                return GuildBankRule.NoBank;

            GuildBankPermission permission = PermissionFor(bank, GuildRules.RankOf(member));
            if (!permission.AllowWithdraw)
                return GuildBankRule.NotPermitted;

            if (permission.WithdrawLimit >= 0 &&
                EffectiveWithdrawn(permission, now) >= permission.WithdrawLimit)
                return GuildBankRule.WithdrawLimitReached;

            GuildBankTab tab = FindTab(bank, tabIndex);
            if (tab == null)
                return GuildBankRule.InvalidTab;

            if (Grid.FindAt(tab.Items, gridX, gridY) < 0)
                return GuildBankRule.NothingToWithdraw;

            return GuildBankRule.Ok;
        }

        /// <summary>Whether the requester may edit rank permissions: only the guild leader.</summary>
        public static GuildBankRule CanEditPermissions(GuildRecord guild, string accountId)
        {
            if (guild == null)
                return GuildBankRule.NoGuild;

            GuildMemberRecord member = GuildRules.FindMember(guild, accountId);
            if (member.AccountId == null)
                return GuildBankRule.NotInGuild;

            return GuildRules.RankOf(member) == GuildRank.Leader ? GuildBankRule.Ok : GuildBankRule.NotPermitted;
        }

        /// <summary>
        /// Applies a deposit: places the item in the first free cell of the tab. Assumes
        /// <see cref="CanDeposit"/> passed. Returns false only when the caller lied about space.
        /// </summary>
        public static bool Deposit(GuildBankRecord bank, int tabIndex, ItemInstance item)
        {
            GuildBankTab tab = FindTab(bank, tabIndex);
            if (tab == null || !Grid.TryFindFreeSpace(tab.Items, item.Width, item.Height, out int x, out int y))
                return false;

            item.GridX = x;
            item.GridY = y;
            tab.Items.Add(item);
            return true;
        }

        /// <summary>
        /// Applies a withdrawal: removes the item at the tab cell and spends one unit of the
        /// rank's quota. Assumes <see cref="CanWithdraw"/> passed with the same <paramref name="now"/>.
        /// </summary>
        public static bool Withdraw(
            GuildRecord guild, GuildBankRecord bank, string accountId, int tabIndex, int gridX, int gridY,
            DateTimeOffset now, out ItemInstance item)
        {
            item = ItemInstance.Empty;
            if (guild == null || bank == null)
                return false;

            GuildBankTab tab = FindTab(bank, tabIndex);
            if (tab == null)
                return false;

            int index = Grid.FindAt(tab.Items, gridX, gridY);
            if (index < 0)
                return false;

            item = tab.Items[index];
            tab.Items.RemoveAt(index);

            GuildRank rank = GuildRules.RankOf(GuildRules.FindMember(guild, accountId));
            SpendQuota(bank, rank, now);
            return true;
        }

        /// <summary>
        /// Replaces (or adds) the permission entry for a rank. Called only after
        /// <see cref="CanEditPermissions"/> passed; the counter is left in place so an edit is not
        /// a free quota reset.
        /// </summary>
        public static void SetPermission(
            GuildBankRecord bank, GuildRank rank, bool allowDeposit, bool allowWithdraw, int withdrawLimit)
        {
            if (bank == null)
                return;

            bank.Permissions ??= new List<GuildBankPermission>();

            int index = -1;
            for (int i = 0; i < bank.Permissions.Count; i++)
            {
                if (bank.Permissions[i].Rank == (int)rank)
                {
                    index = i;
                    break;
                }
            }

            GuildBankPermission permission = index >= 0 ? bank.Permissions[index] : DefaultPermission(rank);
            permission.Rank = (int)rank;
            permission.AllowDeposit = allowDeposit;
            permission.AllowWithdraw = allowWithdraw;
            permission.WithdrawLimit = withdrawLimit < UnlimitedWithdraw ? UnlimitedWithdraw : withdrawLimit;

            if (index >= 0)
                bank.Permissions[index] = permission;
            else
                bank.Permissions.Add(permission);
        }

        /// <summary>Whether a valid permission value was supplied by a client/editor.</summary>
        public static bool IsValidPermission(bool allowDeposit, bool allowWithdraw, int withdrawLimit)
        {
            // A rank must be allowed to do at least one thing; a limit below -1 (other than the
            // exact unlimited sentinel) is nonsense and rejected rather than silently clamped.
            if (!allowDeposit && !allowWithdraw)
                return false;

            return withdrawLimit >= UnlimitedWithdraw;
        }

        /// <summary>True when the tab's grid can hold a rectangle of the given size somewhere.</summary>
        public static bool HasFreeSpace(GuildBankTab tab, int width, int height)
            => tab != null && Grid.TryFindFreeSpace(tab.Items, width, height, out _, out _);

        /// <summary>True when a rectangle sits in bounds and clear of every item in the tab.</summary>
        public static bool CanPlace(GuildBankTab tab, int x, int y, int width, int height)
            => tab != null && Grid.CanPlace(tab.Items, x, y, width, height);

        /// <summary>The item covering a tab cell, or an empty instance when the cell is free.</summary>
        public static ItemInstance ItemAt(GuildBankTab tab, int gridX, int gridY)
        {
            if (tab?.Items == null)
                return ItemInstance.Empty;

            int index = Grid.FindAt(tab.Items, gridX, gridY);
            return index >= 0 ? tab.Items[index] : ItemInstance.Empty;
        }

        private static bool IsQuotaStale(GuildBankPermission permission, DateTimeOffset now)
            => permission.PeriodStartTicks <= 0 ||
               now.UtcDateTime.Ticks - permission.PeriodStartTicks >= QuotaPeriod.Ticks;

        private static int EffectiveWithdrawn(GuildBankPermission permission, DateTimeOffset now)
            => IsQuotaStale(permission, now) ? 0 : permission.WithdrawnThisPeriod;

        private static void SpendQuota(GuildBankRecord bank, GuildRank rank, DateTimeOffset now)
        {
            bank.Permissions ??= new List<GuildBankPermission>();

            int index = -1;
            for (int i = 0; i < bank.Permissions.Count; i++)
            {
                if (bank.Permissions[i].Rank == (int)rank)
                {
                    index = i;
                    break;
                }
            }

            GuildBankPermission permission = index >= 0 ? bank.Permissions[index] : DefaultPermission(rank);
            if (IsQuotaStale(permission, now))
            {
                permission.WithdrawnThisPeriod = 0;
                permission.PeriodStartTicks = now.UtcDateTime.Ticks;
            }

            permission.WithdrawnThisPeriod++;

            if (index >= 0)
                bank.Permissions[index] = permission;
            else
                bank.Permissions.Add(permission);
        }

        private static GuildBankPermission NewPermission(
            GuildRank rank, bool allowDeposit, bool allowWithdraw, int withdrawLimit)
            => new GuildBankPermission
            {
                Rank = (int)rank,
                AllowDeposit = allowDeposit,
                AllowWithdraw = allowWithdraw,
                WithdrawLimit = withdrawLimit,
            };
    }
}
