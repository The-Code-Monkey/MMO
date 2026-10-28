using System;
using System.Collections.Generic;
using MMO.Economy;
using MMO.Inventory;

namespace MMO.Social
{
    /// <summary>
    /// One tab of a guild bank: a display name and the items stored in it. Each item keeps its
    /// grid cell and footprint (the same <see cref="ItemInstance"/> shape the player bag uses), so
    /// the bank is laid out and rendered by the very same grid rules the inventory already uses.
    /// </summary>
    [Serializable]
    public sealed class GuildBankTab
    {
        /// <summary>Stable tab ordinal; the tab's position in <see cref="GuildBankRecord.Tabs"/>.</summary>
        public int Index;

        public string Name;

        public List<ItemInstance> Items = new List<ItemInstance>();
    }

    /// <summary>
    /// One rank's bank permissions. <see cref="AllowDeposit"/> and <see cref="AllowWithdraw"/> gate
    /// the action; <see cref="WithdrawLimit"/> caps how many items the rank may withdraw per quota
    /// period (<see cref="GuildBankRules.QuotaPeriod"/>), with a negative value meaning unlimited.
    /// The counter and period start are server-maintained and lazily reset by the rules, so a
    /// leader can hand a rank a generous allowance without it ever accumulating across periods.
    /// </summary>
    [Serializable]
    public struct GuildBankPermission
    {
        /// <summary>A <see cref="GuildRank"/> stored as its int value, like <see cref="GuildMemberRecord.Rank"/>.</summary>
        public int Rank;

        public bool AllowDeposit;

        public bool AllowWithdraw;

        /// <summary>Max items withdrawn per period; negative means unlimited.</summary>
        public int WithdrawLimit;

        /// <summary>Items withdrawn in the current period; server-maintained.</summary>
        public int WithdrawnThisPeriod;

        /// <summary>UTC ticks the current quota period began; <c>0</c> means "not started".</summary>
        public long PeriodStartTicks;

        /// <summary>Max total value withdrawn per period; negative means unlimited.</summary>
        public long WithdrawValueLimit;

        /// <summary>Total value withdrawn in the current period; server-maintained.</summary>
        public long WithdrawnValueThisPeriod;

        /// <summary>Max total value deposited per period; negative means unlimited.</summary>
        public long DepositValueLimit;

        /// <summary>Total value deposited in the current period; server-maintained.</summary>
        public long DepositedValueThisPeriod;
    }

    /// <summary>
    /// The outcome of a bank mutation that moves an item: the rule that decided it and, on success,
    /// the item that moved. A struct rather than an out parameter because the write is asynchronous
    /// and may have been retried under a version conflict.
    /// </summary>
    public readonly struct GuildBankResult
    {
        public GuildBankResult(GuildBankRule rule, ItemInstance item = default)
        {
            Rule = rule;
            Item = item;
        }

        /// <summary><see cref="GuildBankRule.Ok"/> on success; the refusal reason otherwise.</summary>
        public GuildBankRule Rule { get; }

        /// <summary>The moved item on success; empty on failure.</summary>
        public ItemInstance Item { get; }
    }

    /// <summary>
    /// The authoritative guild bank object: collection "guild_banks", key = <see cref="GuildId"/>,
    /// owner = the shared service account. It holds the persistent tab layout (each tab a grid of
    /// <see cref="GuildBankTab.Items"/>) and the per-rank <see cref="Permissions"/>, in the exact
    /// owner-scoped CAS pattern parties and guilds use. Only <see cref="SocialHubService"/>'s CAS
    /// retry loop writes it.
    ///
    /// <para>
    /// <see cref="StorageVersion"/> is the backend's revision hash, carried on the record so the IO
    /// layer can <em>if-match</em> the version it read; a losing write surfaces as
    /// <c>Persistence.StorageVersionConflictException</c> and the caller retries.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class GuildBankRecord
    {
        public string GuildId;

        public List<GuildBankTab> Tabs = new List<GuildBankTab>();

        /// <summary>
        /// Currency held by the guild, deposited and withdrawn under the same rank permissions and
        /// quota as the item tabs. A guild vault is a balance list, not a second wallet record.
        /// </summary>
        public List<CurrencyBalance> Vault = new List<CurrencyBalance>();

        public List<GuildBankPermission> Permissions = new List<GuildBankPermission>();

        /// <summary>The backend's revision hash for optimistic concurrency; not bank data.</summary>
        public string StorageVersion;
    }
}
