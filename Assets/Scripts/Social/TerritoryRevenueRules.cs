using System.Collections.Generic;

namespace MMO.Social
{
    /// <summary>
    /// The pure half of territory revenue: which ledger rows are tax, which are still owed, and how
    /// much has been collected. Settlement itself is a version-checked bank write, but every
    /// decision about <em>what</em> to pay is here so a test can exercise it without a backend.
    /// </summary>
    public static class TerritoryRevenueRules
    {
        /// <summary>True when the entry is a positive tax movement in a currency.</summary>
        public static bool IsTaxEntry(GuildBankLedgerEntry entry)
            => entry != null
               && entry.Action == GuildBankLedgerAction.Tax
               && entry.TotalValue > 0
               && !string.IsNullOrEmpty(entry.ItemName);

        /// <summary>
        /// True when this entry has already been paid. The bank record is authoritative: the ledger's
        /// own <see cref="GuildBankLedgerEntry.Settled"/> flag is a convenience that a later pass can
        /// rebuild from the ids.
        /// </summary>
        public static bool IsSettled(GuildBankRecord bank, GuildBankLedgerEntry entry)
        {
            if (entry == null || string.IsNullOrEmpty(entry.EntryId))
                return false;

            List<string> settled = bank?.SettledTerritoryEntries;
            if (settled == null)
                return false;

            for (int i = 0; i < settled.Count; i++)
            {
                if (settled[i] == entry.EntryId)
                    return true;
            }

            return false;
        }

        /// <summary>Every tax entry that is due and not already recorded as paid on the bank.</summary>
        public static List<GuildBankLedgerEntry> DueEntries(
            IReadOnlyList<GuildBankLedgerEntry> entries,
            GuildBankRecord bank)
        {
            var due = new List<GuildBankLedgerEntry>();
            if (entries == null)
                return due;

            for (int i = 0; i < entries.Count; i++)
            {
                GuildBankLedgerEntry entry = entries[i];
                if (IsTaxEntry(entry) && !IsSettled(bank, entry))
                    due.Add(entry);
            }

            return due;
        }

        /// <summary>Total tax recorded in the ledger, settled or not, for display/diagnostics.</summary>
        public static long TotalCollected(IReadOnlyList<GuildBankLedgerEntry> entries)
        {
            if (entries == null)
                return 0;

            long total = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                if (IsTaxEntry(entries[i]))
                    total += entries[i].TotalValue;
            }

            return total;
        }
    }
}
