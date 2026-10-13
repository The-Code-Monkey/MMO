using System;
using System.Collections.Generic;

namespace MMO.Social
{
    public enum GuildBankLedgerAction
    {
        Deposit,
        Withdraw,
    }

    [Serializable]
    public sealed class GuildBankLedgerEntry
    {
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
    }
}
