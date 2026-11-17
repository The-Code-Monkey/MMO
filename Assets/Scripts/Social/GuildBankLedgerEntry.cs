using System;
using System.Collections.Generic;

namespace MMO.Social
{
    public enum GuildBankLedgerAction
    {
        Deposit,
        Withdraw,

        /// <summary>
        /// Territory tax withheld from a seller's auction proceeds. Appended when the sale settles
        /// and paid into the owning guild's vault by the revenue settlement pass.
        /// </summary>
        Tax,
    }

    [Serializable]
    public sealed class GuildBankLedgerEntry
    {
        /// <summary>
        /// Stable identity of this movement. Settlement records it on the bank so the same entry can
        /// never be paid twice, even if the ledger write that marks it settled is retried or lost.
        /// </summary>
        public string EntryId;

        public string GuildId;
        public string ActorAccountId;
        public GuildBankLedgerAction Action;
        public int TabIndex;
        public string ItemName;
        public int Width;
        public int Height;
        public int GridX;
        public int GridY;
        public int StackCount;
        public long ValuePerUnit;
        public long TotalValue;
        public long TimestampTicks;
        public List<int> AffectedCells = new List<int>();

        /// <summary>True once the revenue settlement pass has paid this entry into the vault.</summary>
        public bool Settled;
    }
}
