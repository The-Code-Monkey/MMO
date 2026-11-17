using System;
using System.Collections.Generic;
using MMO.Economy;
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
        InvalidAmount,
        InsufficientFunds,
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
        public const long UnlimitedValue = -1;

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
            bank.Vault ??= new List<CurrencyBalance>();
            bank.Permissions ??= new List<GuildBankPermission>();
            bank.SettledTerritoryEntries ??= new List<string>();

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
                    return NewPermission(rank, allowDeposit: true, allowWithdraw: true, withdrawLimit: UnlimitedWithdraw, withdrawValueLimit: UnlimitedValue, depositValueLimit: UnlimitedValue);
                case GuildRank.Officer:
                    return NewPermission(rank, allowDeposit: true, allowWithdraw: true, withdrawLimit: 10, withdrawValueLimit: UnlimitedValue, depositValueLimit: UnlimitedValue);
                default:
                    return NewPermission(rank, allowDeposit: true, allowWithdraw: true, withdrawLimit: 1, withdrawValueLimit: UnlimitedValue, depositValueLimit: UnlimitedValue);
            }
        }

        public static bool RankAllowsDeposit(GuildBankRecord bank, GuildRank rank)
            => PermissionFor(bank, rank).AllowDeposit;

        public static bool RankAllowsWithdraw(GuildBankRecord bank, GuildRank rank)
            => PermissionFor(bank, rank).AllowWithdraw;

        public static long GetWithdrawValueLimit(GuildBankPermission permission)
            => permission.WithdrawValueLimit;

        public static long GetDepositValueLimit(GuildBankPermission permission)
            => permission.DepositValueLimit;

        public static long GetEffectiveWithdrawnValue(GuildBankPermission permission, DateTimeOffset now)
            => EffectiveWithdrawnValue(permission, now);

        public static long GetEffectiveDepositedValue(GuildBankPermission permission, DateTimeOffset now)
            => EffectiveDepositedValue(permission, now);

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
            GuildRecord guild, GuildBankRecord bank, string accountId, int tabIndex, ItemInstance item, DateTimeOffset now)
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

            GuildRank rank = GuildRules.RankOf(member);
            if (!RankAllowsDeposit(bank, rank))
                return GuildBankRule.NotPermitted;

            GuildBankPermission permission = PermissionFor(bank, rank);
            long itemValue = item.GetTotalValue(ItemDefinitionRegistry.Resolve(item.ItemName));
            if (permission.DepositValueLimit >= 0 &&
                EffectiveDepositedValue(permission, now) + itemValue > permission.DepositValueLimit)
                return GuildBankRule.NotPermitted;

            GuildBankTab tab = FindTab(bank, tabIndex);
            if (tab == null)
                return GuildBankRule.InvalidTab;

            if (!HasFreeSpace(tab, item.Width, item.Height))
                return GuildBankRule.TabFull;

            return GuildBankRule.Ok;
        }

        public static GuildBankRule CanDeposit(
            GuildRecord guild, GuildBankRecord bank, string accountId, int tabIndex, ItemInstance item)
            => CanDeposit(guild, bank, accountId, tabIndex, item, DateTimeOffset.UtcNow);

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

            GuildRank rank = GuildRules.RankOf(member);
            GuildBankPermission permission = PermissionFor(bank, rank);
            if (!permission.AllowWithdraw)
                return GuildBankRule.NotPermitted;

            if (permission.WithdrawLimit >= 0 &&
                EffectiveWithdrawn(permission, now) >= permission.WithdrawLimit)
                return GuildBankRule.WithdrawLimitReached;

            GuildBankTab tab = FindTab(bank, tabIndex);
            if (tab == null)
                return GuildBankRule.InvalidTab;

            int idx = Grid.FindAt(tab.Items, gridX, gridY);
            if (idx < 0)
                return GuildBankRule.NothingToWithdraw;

            if (permission.WithdrawValueLimit >= 0)
            {
                ItemInstance candidate = tab.Items[idx];
                long candidateValue = candidate.GetTotalValue(ItemDefinitionRegistry.Resolve(candidate.ItemName));
                if (EffectiveWithdrawnValue(permission, now) + candidateValue > permission.WithdrawValueLimit)
                    return GuildBankRule.WithdrawLimitReached;
            }

            return GuildBankRule.Ok;
        }

        /// <summary>
        /// Whether the requester may deposit currency into the guild vault. Order: guild/member,
        /// bank, amount, rank permission, deposit-value quota. The caller checks the member's
        /// wallet for the funds; this rule only gates the guild side.
        /// </summary>
        public static GuildBankRule CanDepositCurrency(
            GuildRecord guild, GuildBankRecord bank, string accountId, string currencyId, long amount, DateTimeOffset now)
        {
            if (guild == null)
                return GuildBankRule.NoGuild;

            GuildMemberRecord member = GuildRules.FindMember(guild, accountId);
            if (member.AccountId == null)
                return GuildBankRule.NotInGuild;

            if (bank == null)
                return GuildBankRule.NoBank;

            if (string.IsNullOrEmpty(currencyId) || amount <= 0)
                return GuildBankRule.InvalidAmount;

            GuildRank rank = GuildRules.RankOf(member);
            GuildBankPermission permission = PermissionFor(bank, rank);
            if (!permission.AllowDeposit)
                return GuildBankRule.NotPermitted;

            if (permission.DepositValueLimit >= 0 &&
                EffectiveDepositedValue(permission, now) + amount > permission.DepositValueLimit)
                return GuildBankRule.NotPermitted;

            return GuildBankRule.Ok;
        }

        /// <summary>
        /// Whether the requester may withdraw currency from the guild vault. Order: guild/member,
        /// bank, amount, rank permission, withdrawal quota, vault balance.
        /// </summary>
        public static GuildBankRule CanWithdrawCurrency(
            GuildRecord guild, GuildBankRecord bank, string accountId, string currencyId, long amount, DateTimeOffset now)
        {
            if (guild == null)
                return GuildBankRule.NoGuild;

            GuildMemberRecord member = GuildRules.FindMember(guild, accountId);
            if (member.AccountId == null)
                return GuildBankRule.NotInGuild;

            if (bank == null)
                return GuildBankRule.NoBank;

            if (string.IsNullOrEmpty(currencyId) || amount <= 0)
                return GuildBankRule.InvalidAmount;

            GuildRank rank = GuildRules.RankOf(member);
            GuildBankPermission permission = PermissionFor(bank, rank);
            if (!permission.AllowWithdraw)
                return GuildBankRule.NotPermitted;

            if (permission.WithdrawLimit >= 0 &&
                EffectiveWithdrawn(permission, now) >= permission.WithdrawLimit)
                return GuildBankRule.WithdrawLimitReached;

            if (permission.WithdrawValueLimit >= 0 &&
                EffectiveWithdrawnValue(permission, now) + amount > permission.WithdrawValueLimit)
                return GuildBankRule.WithdrawLimitReached;

            if (WalletRules.BalanceOf(bank.Vault, currencyId) < amount)
                return GuildBankRule.InsufficientFunds;

            return GuildBankRule.Ok;
        }

        /// <summary>
        /// Applies a currency deposit: credits the vault and spends the rank's deposit quota.
        /// Assumes <see cref="CanDepositCurrency"/> passed.
        /// </summary>
        public static bool DepositCurrency(GuildBankRecord bank, string currencyId, long amount, GuildRank rank, DateTimeOffset now)
        {
            if (bank == null)
                return false;

            bank.Vault ??= new List<CurrencyBalance>();
            if (WalletRules.Credit(bank.Vault, currencyId, amount) != WalletError.Ok)
                return false;

            if (rank != GuildRank.None)
                SpendDepositQuota(bank, rank, now, amount);
            return true;
        }

        /// <summary>
        /// Applies a currency withdrawal: debits the vault and spends the rank's quota. Assumes
        /// <see cref="CanWithdrawCurrency"/> passed with the same <paramref name="now"/>.
        /// </summary>
        public static bool WithdrawCurrency(GuildBankRecord bank, string currencyId, long amount, GuildRank rank, DateTimeOffset now)
        {
            if (bank == null)
                return false;

            bank.Vault ??= new List<CurrencyBalance>();
            if (WalletRules.Debit(bank.Vault, currencyId, amount) != WalletError.Ok)
                return false;

            if (rank != GuildRank.None)
                SpendQuota(bank, rank, now, amount);
            return true;
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
        public static bool Deposit(GuildBankRecord bank, int tabIndex, ItemInstance item, GuildRank rank, DateTimeOffset now)
        {
            GuildBankTab tab = FindTab(bank, tabIndex);
            if (tab == null || !Grid.TryFindFreeSpace(tab.Items, item.Width, item.Height, out int x, out int y))
                return false;

            item.GridX = x;
            item.GridY = y;
            tab.Items.Add(item);
            if (bank != null && rank != GuildRank.None)
            {
                long value = item.GetTotalValue(ItemDefinitionRegistry.Resolve(item.ItemName));
                SpendDepositQuota(bank, rank, now, value);
            }
            return true;
        }

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
            long value = item.GetTotalValue(ItemDefinitionRegistry.Resolve(item.ItemName));
            SpendQuota(bank, rank, now, value);
            return true;
        }

        /// <summary>
        /// Replaces (or adds) the permission entry for a rank. Called only after
        /// <see cref="CanEditPermissions"/> passed; the counter is left in place so an edit is not
        /// a free quota reset.
        /// </summary>
        public static void SetPermission(
            GuildBankRecord bank, GuildRank rank, bool allowDeposit, bool allowWithdraw, int withdrawLimit,
            long withdrawValueLimit = UnlimitedValue, long depositValueLimit = UnlimitedValue)
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
            permission.WithdrawValueLimit = withdrawValueLimit < UnlimitedValue ? UnlimitedValue : withdrawValueLimit;
            permission.DepositValueLimit = depositValueLimit < UnlimitedValue ? UnlimitedValue : depositValueLimit;

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

        public static bool IsValidPermission(bool allowDeposit, bool allowWithdraw, int withdrawLimit, long withdrawValueLimit, long depositValueLimit)
        {
            if (!allowDeposit && !allowWithdraw)
                return false;

            if (withdrawLimit < UnlimitedWithdraw)
                return false;

            if (withdrawValueLimit < UnlimitedValue)
                return false;

            if (depositValueLimit < UnlimitedValue)
                return false;

            return true;
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

        private static long EffectiveWithdrawnValue(GuildBankPermission permission, DateTimeOffset now)
            => IsQuotaStale(permission, now) ? 0 : permission.WithdrawnValueThisPeriod;

        private static long EffectiveDepositedValue(GuildBankPermission permission, DateTimeOffset now)
            => IsQuotaStale(permission, now) ? 0 : permission.DepositedValueThisPeriod;



        public static void SpendQuota(GuildBankRecord bank, GuildRank rank, DateTimeOffset now, long withdrawValue)
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
            if (IsQuotaStale(permission, now))
            {
                permission.WithdrawnThisPeriod = 0;
                permission.WithdrawnValueThisPeriod = 0;
                permission.DepositedValueThisPeriod = 0;
                permission.PeriodStartTicks = now.UtcDateTime.Ticks;
            }

            permission.WithdrawnThisPeriod++;
            permission.WithdrawnValueThisPeriod += withdrawValue;

            if (index >= 0)
                bank.Permissions[index] = permission;
            else
                bank.Permissions.Add(permission);
        }

        public static void SpendDepositQuota(GuildBankRecord bank, GuildRank rank, DateTimeOffset now, long depositValue)
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
            if (IsQuotaStale(permission, now))
            {
                permission.WithdrawnThisPeriod = 0;
                permission.WithdrawnValueThisPeriod = 0;
                permission.DepositedValueThisPeriod = 0;
                permission.PeriodStartTicks = now.UtcDateTime.Ticks;
            }

            permission.DepositedValueThisPeriod += depositValue;

            if (index >= 0)
                bank.Permissions[index] = permission;
            else
                bank.Permissions.Add(permission);
        }

        private static GuildBankPermission NewPermission(
            GuildRank rank, bool allowDeposit, bool allowWithdraw, int withdrawLimit,
            long withdrawValueLimit = UnlimitedValue, long depositValueLimit = UnlimitedValue)
            => new GuildBankPermission
            {
                Rank = (int)rank,
                AllowDeposit = allowDeposit,
                AllowWithdraw = allowWithdraw,
                WithdrawLimit = withdrawLimit,
                WithdrawValueLimit = withdrawValueLimit,
                DepositValueLimit = depositValueLimit,
            };
    }
}
