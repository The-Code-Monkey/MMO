using System.Collections.Generic;
using MMO.Social;
using NUnit.Framework;
using UnityEngine;

namespace MMO.Tests
{
    /// <summary>
    /// Territory revenue is decided purely: which ledger rows are tax, which ids the bank has
    /// already paid, and what is still due. The idempotence test is the important one — a retried
    /// settlement must never pay the same row twice.
    /// </summary>
    public sealed class TerritoryRevenueRulesTests
    {
        [Test]
        public void IsTaxEntry_OnlyAcceptsPositiveCurrencyTaxRows()
        {
            Assert.IsTrue(TerritoryRevenueRules.IsTaxEntry(Tax("e1", 100)));

            Assert.IsFalse(TerritoryRevenueRules.IsTaxEntry(null));
            Assert.IsFalse(TerritoryRevenueRules.IsTaxEntry(Tax("e2", 0)));
            Assert.IsFalse(TerritoryRevenueRules.IsTaxEntry(new GuildBankLedgerEntry
            {
                EntryId = "e3",
                Action = GuildBankLedgerAction.Deposit,
                ItemName = "gold",
                TotalValue = 100,
            }));
            Assert.IsFalse(TerritoryRevenueRules.IsTaxEntry(new GuildBankLedgerEntry
            {
                EntryId = "e4",
                Action = GuildBankLedgerAction.Tax,
                TotalValue = 100,
            }));
        }

        [Test]
        public void IsSettled_MatchesTheBankEntryId()
        {
            var bank = new GuildBankRecord { SettledTerritoryEntries = new List<string> { "paid" } };

            Assert.IsTrue(TerritoryRevenueRules.IsSettled(bank, Tax("paid", 10)));
            Assert.IsFalse(TerritoryRevenueRules.IsSettled(bank, Tax("unpaid", 10)));
            Assert.IsFalse(TerritoryRevenueRules.IsSettled(null, Tax("paid", 10)));
            Assert.IsFalse(TerritoryRevenueRules.IsSettled(bank, new GuildBankLedgerEntry
            {
                Action = GuildBankLedgerAction.Tax,
                TotalValue = 10,
            }));
        }

        [Test]
        public void DueEntries_SkipsPaidAndNonTaxRows()
        {
            var entries = new List<GuildBankLedgerEntry>
            {
                Tax("paid", 100),
                Tax("due", 250),
                new GuildBankLedgerEntry
                {
                    EntryId = "withdraw",
                    Action = GuildBankLedgerAction.Withdraw,
                    ItemName = "gold",
                    TotalValue = 500,
                },
            };
            var bank = new GuildBankRecord { SettledTerritoryEntries = new List<string> { "paid" } };

            List<GuildBankLedgerEntry> due = TerritoryRevenueRules.DueEntries(entries, bank);

            Assert.AreEqual(1, due.Count);
            Assert.AreEqual("due", due[0].EntryId);
        }

        [Test]
        public void DueEntries_IsEmptyAfterTheBankRecordsPayment()
        {
            var entries = new List<GuildBankLedgerEntry> { Tax("e1", 100) };
            var bank = new GuildBankRecord();

            Assert.AreEqual(1, TerritoryRevenueRules.DueEntries(entries, bank).Count);

            bank.SettledTerritoryEntries.Add("e1");

            Assert.IsEmpty(TerritoryRevenueRules.DueEntries(entries, bank),
                "A settled entry must never be paid a second time.");
        }

        [Test]
        public void TotalCollected_SumsTaxRowsOnly()
        {
            var entries = new List<GuildBankLedgerEntry>
            {
                Tax("a", 100),
                Tax("b", 250),
                new GuildBankLedgerEntry { Action = GuildBankLedgerAction.Deposit, TotalValue = 999 },
            };

            Assert.AreEqual(350L, TerritoryRevenueRules.TotalCollected(entries));
            Assert.AreEqual(0L, TerritoryRevenueRules.TotalCollected(null));
        }

        [Test]
        public void BankRecord_RoundTripsRevenueFieldsThroughJson()
        {
            var bank = new GuildBankRecord
            {
                GuildId = "g1",
                SettledTerritoryEntries = new List<string> { "e1", "e2" },
                TerritoryRevenue = 750,
            };

            GuildBankRecord restored = JsonUtility.FromJson<GuildBankRecord>(JsonUtility.ToJson(bank));

            Assert.AreEqual(750L, restored.TerritoryRevenue);
            CollectionAssert.AreEqual(bank.SettledTerritoryEntries, restored.SettledTerritoryEntries);
        }

        private static GuildBankLedgerEntry Tax(string entryId, long amount)
        {
            return new GuildBankLedgerEntry
            {
                EntryId = entryId,
                Action = GuildBankLedgerAction.Tax,
                ItemName = "gold",
                TotalValue = amount,
                TimestampTicks = 1,
            };
        }
    }
}
