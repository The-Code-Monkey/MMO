using System.Collections.Generic;
using System.Linq;
using MMO.Auction;
using MMO.Persistence;
using NUnit.Framework;

namespace MMO.Tests.EditMode
{
    public sealed class AuctionSearchTests
    {
        private const long Now = 1_000_000;

        private static SearchRow Row(
            string id, string itemName, string category = "Weapon", int tier = 1,
            long price = 100, string seller = "s1", long listedAt = 1000, long expiresAt = Now + 100)
            => new SearchRow
            {
                ListingId = id,
                SellerAccountId = seller,
                SellerPlayerName = seller,
                ItemName = itemName,
                Category = category,
                Tier = tier,
                AskingPrice = price,
                StackCount = 1,
                ListedAtTicks = listedAt,
                ExpiresAtTicks = expiresAt,
                SellerZoneId = "zone-a",
            };

        private static AuctionQuery Query() => new AuctionQuery();

        [Test]
        public void Filter_Text_MatchesNameOnly()
        {
            var rows = new List<SearchRow> { Row("1", "Iron Sword"), Row("2", "Health Potion") };
            var q = Query();
            q.Text = "swo";

            IReadOnlyList<SearchRow> result = AuctionSearch.Apply(rows, q, Now);

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("Iron Sword", result[0].ItemName);
        }

        [Test]
        public void Filter_Category_IsCaseInsensitive()
        {
            var rows = new List<SearchRow> { Row("1", "Iron Sword", category: "Weapon"), Row("2", "Belt", category: "Armor") };
            var q = Query();
            q.Category = "weapon";

            Assert.AreEqual(1, AuctionSearch.Apply(rows, q, Now).Count);
        }

        [Test]
        public void Filter_TierBand_IsInclusive()
        {
            var rows = new List<SearchRow> { Row("1", "T1", tier: 1), Row("2", "T5", tier: 5), Row("3", "T10", tier: 10) };
            var q = Query();
            q.MinTier = 2;
            q.MaxTier = 5;

            IReadOnlyList<SearchRow> result = AuctionSearch.Apply(rows, q, Now);
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("T5", result[0].ItemName);
        }

        [Test]
        public void Filter_PriceBand_IsInclusive()
        {
            var rows = new List<SearchRow> { Row("1", "cheap", price: 50), Row("2", "mid", price: 100), Row("3", "dear", price: 150) };
            var q = Query();
            q.MinPrice = 100;
            q.MaxPrice = 100;

            Assert.AreEqual(1, AuctionSearch.Apply(rows, q, Now).Count);
        }

        [Test]
        public void Filter_Seller_ReturnsOnlyTheirListings()
        {
            var rows = new List<SearchRow> { Row("1", "a", seller: "s1"), Row("2", "b", seller: "s2") };
            var q = Query();
            q.SellerId = "s2";

            IReadOnlyList<SearchRow> result = AuctionSearch.Apply(rows, q, Now);
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("s2", result[0].SellerAccountId);
        }

        [Test]
        public void Filter_ExcludesExpired()
        {
            var rows = new List<SearchRow> { Row("1", "live"), Row("2", "dead", expiresAt: Now) };
            Assert.AreEqual(1, AuctionSearch.Apply(rows, Query(), Now).Count);
        }

        [Test]
        public void Sort_PriceAsc_IsOrderedTiesById()
        {
            var rows = new List<SearchRow>
            {
                Row("b", "mid", price: 100),
                Row("a", "cheap", price: 50),
                Row("c", "dear", price: 150),
            };
            var q = Query();
            q.SortBy = AuctionSort.PriceAsc;

            IReadOnlyList<SearchRow> result = AuctionSearch.Apply(rows, q, Now);
            Assert.AreEqual(new[] { "a", "b", "c" }, result.Select(r => r.ListingId).ToArray());
        }

        [Test]
        public void Sort_PriceDesc_HighestFirst()
        {
            var rows = new List<SearchRow> { Row("b", "mid", price: 100), Row("a", "cheap", price: 50), Row("c", "dear", price: 150) };
            var q = Query();
            q.SortBy = AuctionSort.PriceDesc;

            Assert.AreEqual(new[] { "c", "b", "a" }, AuctionSearch.Apply(rows, q, Now).Select(r => r.ListingId).ToArray());
        }

        [Test]
        public void Sort_TieBreaksByIdForStablePaging()
        {
            var rows = new List<SearchRow>
            {
                Row("b", "x", price: 100),
                Row("a", "y", price: 100),
            };
            var q = Query();
            q.SortBy = AuctionSort.PriceAsc;

            Assert.AreEqual(new[] { "a", "b" }, AuctionSearch.Apply(rows, q, Now).Select(r => r.ListingId).ToArray());
        }

        [Test]
        public void Apply_StablePaging_NoDuplicatesAcrossPages()
        {
            var rows = new List<SearchRow>();
            for (int i = 0; i < 10; i++)
                rows.Add(Row(i.ToString("D2"), $"Item {i}", price: 100 + i));

            var q = Query();
            q.SortBy = AuctionSort.PriceAsc;
            q.PageSize = 4;

            var seen = new HashSet<string>();
            for (int page = 0; page < 3; page++)
            {
                q.Page = page;
                IReadOnlyList<SearchRow> result = AuctionSearch.Apply(rows, q, Now);
                foreach (SearchRow row in result)
                    Assert.IsTrue(seen.Add(row.ListingId), $"duplicate {row.ListingId} on page {page}");
            }

            Assert.AreEqual(10, seen.Count);
        }

        [Test]
        public void Apply_NeverThrowsOnEmpty()
        {
            Assert.IsEmpty(AuctionSearch.Apply(null, Query(), Now));
            Assert.IsEmpty(AuctionSearch.Apply(new List<SearchRow>(), Query(), Now));
        }

        [Test]
        public void Apply_EmptyPagePastEnd()
        {
            var q = Query();
            q.Page = 5;
            Assert.IsEmpty(AuctionSearch.Apply(new List<SearchRow> { Row("1", "a") }, q, Now));
        }
    }
}
