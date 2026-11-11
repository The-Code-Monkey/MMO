using System.Collections.Generic;
using MMO.Auction;
using MMO.Persistence;
using NUnit.Framework;

namespace MMO.Tests.EditMode
{
    /// <summary>
    /// The pure market-index kernel: rows are a cache of the authoritative listings, so every rule
    /// here must converge on the truth without ever needing the backend.
    /// </summary>
    public sealed class MarketIndexRulesTests
    {
        private const long Now = 10_000;

        private static SearchRow Row(
            string id, string itemName = "Iron Sword", long price = 100,
            long expiresAt = Now + 100, long indexedAt = 0)
            => new SearchRow
            {
                ListingId = id,
                SellerAccountId = "seller",
                SellerPlayerName = "Seller",
                ItemName = itemName,
                Category = "Weapon",
                Tier = 1,
                AskingPrice = price,
                StackCount = 1,
                ListedAtTicks = 1_000,
                ExpiresAtTicks = expiresAt,
                SellerZoneId = "zone-a",
                IndexedAtTicks = indexedAt,
            };

        [Test]
        public void Upsert_NewRow_AddsAndStampsIt()
        {
            var rows = new List<SearchRow>();

            MarketIndexRules.Upsert(rows, Row("1"), Now);

            Assert.AreEqual(1, rows.Count);
            Assert.AreEqual("1", rows[0].ListingId);
            Assert.AreEqual(Now, rows[0].IndexedAtTicks);
        }

        [Test]
        public void Upsert_ExistingId_ReplacesWithoutDuplicating()
        {
            var rows = new List<SearchRow> { Row("1", price: 10), Row("2", price: 20) };

            MarketIndexRules.Upsert(rows, Row("1", price: 99), Now);

            Assert.AreEqual(2, rows.Count);
            Assert.AreEqual(99, MarketIndexRules.Find(rows, "1").AskingPrice);
            Assert.AreEqual(Now, MarketIndexRules.Find(rows, "1").IndexedAtTicks);
        }

        [Test]
        public void Remove_ExistingId_DropsTheRow()
        {
            var rows = new List<SearchRow> { Row("1"), Row("2") };

            bool removed = MarketIndexRules.Remove(rows, "1");

            Assert.IsTrue(removed);
            Assert.AreEqual(1, rows.Count);
            Assert.IsNull(MarketIndexRules.Find(rows, "1"));
        }

        [Test]
        public void Remove_MissingId_ReturnsFalse()
        {
            var rows = new List<SearchRow> { Row("1") };

            Assert.IsFalse(MarketIndexRules.Remove(rows, "2"));
        }

        [Test]
        public void Reconcile_AddsTruthRowsMissingFromIndex()
        {
            var index = new List<SearchRow> { Row("1") };
            var truth = new List<SearchRow> { Row("1"), Row("2") };

            List<SearchRow> result = MarketIndexRules.Reconcile(index, truth, Now);

            Assert.AreEqual(2, result.Count);
            Assert.AreEqual(Now, MarketIndexRules.Find(result, "2").IndexedAtTicks);
        }

        [Test]
        public void Reconcile_RemovesRowsAbsentFromTruth()
        {
            var index = new List<SearchRow> { Row("1"), Row("2") };
            var truth = new List<SearchRow> { Row("1") };

            List<SearchRow> result = MarketIndexRules.Reconcile(index, truth, Now);

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("1", result[0].ListingId);
        }

        [Test]
        public void Reconcile_PreservesUnchangedRowStamp()
        {
            var index = new List<SearchRow> { Row("1", indexedAt: 500) };
            var truth = new List<SearchRow> { Row("1") };

            List<SearchRow> result = MarketIndexRules.Reconcile(index, truth, Now);

            Assert.AreEqual(500, result[0].IndexedAtTicks);
        }

        [Test]
        public void Reconcile_RestampsChangedRow()
        {
            var index = new List<SearchRow> { Row("1", price: 10, indexedAt: 500) };
            var truth = new List<SearchRow> { Row("1", price: 20) };

            List<SearchRow> result = MarketIndexRules.Reconcile(index, truth, Now);

            Assert.AreEqual(20, result[0].AskingPrice);
            Assert.AreEqual(Now, result[0].IndexedAtTicks);
        }

        [Test]
        public void Reconcile_DropsExpiredTruthRow()
        {
            var index = new List<SearchRow> { Row("1") };
            var truth = new List<SearchRow> { Row("1", expiresAt: Now) };

            List<SearchRow> result = MarketIndexRules.Reconcile(index, truth, Now);

            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public void Reconcile_NullLists_ReturnEmpty()
        {
            Assert.AreEqual(0, MarketIndexRules.Reconcile(null, null, Now).Count);
        }

        [Test]
        public void IsStale_NeverReconciled_IsStale()
        {
            Assert.IsTrue(MarketIndexRules.IsStale(0, Now, 1_000));
        }

        [Test]
        public void IsStale_WithinInterval_IsFresh()
        {
            Assert.IsFalse(MarketIndexRules.IsStale(Now - 500, Now, 1_000));
        }

        [Test]
        public void IsStale_PastInterval_IsStale()
        {
            Assert.IsTrue(MarketIndexRules.IsStale(Now - 1_001, Now, 1_000));
        }
    }
}
