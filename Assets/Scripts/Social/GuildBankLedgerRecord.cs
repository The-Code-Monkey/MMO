using System;
using System.Collections.Generic;

namespace MMO.Social
{
    [Serializable]
    public sealed class GuildBankLedgerRecord
    {
        public string GuildId;
        public List<GuildBankLedgerEntry> Entries = new List<GuildBankLedgerEntry>();
        public string StorageVersion;
    }
}
