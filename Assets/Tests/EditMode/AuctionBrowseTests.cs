using System.Collections.Generic;
using MMO.Auction;
using MMO.Persistence;
using NUnit.Framework;
using UnityEngine;

namespace MMO.Tests
{
    /// <summary>
    /// The browse bar's pure half: text-to-query defaults, search-row presentation, the saved-search
    /// cap/dedupe, and the JSON codec the server and client share.
    /// </summary>
    public sealed class AuctionBrowseTests
    {
        [Test]
        public void Build_BlankFieldsMeanNoBounds()
        {
            AuctionQuery query = AuctionQueryMapper.Build(
                "  ", " ", "", "", "", "", "", AuctionSort.Relevance, 0);

            Assert.AreEqual(string.Empty, query.Text);
            Assert.AreEqual(string.Empty, query.Category);
            Assert.AreEqual(0, query.MinTier);
            Assert.AreEqual(int.MaxValue, query.MaxTier);
            Assert.AreEqual(0, query.MinPrice);
            Assert.AreEqual(long.MaxValue, query.MaxPrice);
            Assert.AreEqual(AuctionSort.Relevance, query.SortBy);
            Assert.AreEqual(0, query.Page);
            Assert.AreEqual(AuctionSavedSearchRules.DefaultPageSize, query.PageSize);
        }

        [Test]
        public void Build_ParsesFiltersAndClampsThePage()
        {
            AuctionQuery query = AuctionQueryMapper.Build(
                " iron ", "Chest", "3", "7", " seller-1 ", "10", "250",
                AuctionSort.PriceDesc, page: -4, pageSize: 0);

            Assert.AreEqual("iron", query.Text);
            Assert.AreEqual("Chest", query.Category);
            Assert.AreEqual(3, query.MinTier);
            Assert.AreEqual(7, query.MaxTier);
            Assert.AreEqual("seller-1", query.SellerId);
            Assert.AreEqual(10, query.MinPrice);
            Assert.AreEqual(250, query.MaxPrice);
            Assert.AreEqual(AuctionSort.PriceDesc, query.SortBy);
            Assert.AreEqual(0, query.Page);
            Assert.AreEqual(AuctionSavedSearchRules.DefaultPageSize, query.PageSize);
        }

        [Test]
        public void Build_InvalidNumbersFallBackToNoBound()
        {
            AuctionQuery query = AuctionQueryMapper.Build(
                null, null, "abc", "-1", null, "nope", "-5", AuctionSort.Tier, 2);

            Assert.AreEqual(0, query.MinTier);
            Assert.AreEqual(int.MaxValue, query.MaxTier);
            Assert.AreEqual(0, query.MinPrice);
            Assert.AreEqual(long.MaxValue, query.MaxPrice);
            Assert.AreEqual(2, query.Page);
        }

        [Test]
        public void Categories_OffersEquipmentToolsAndOther()
        {
            IReadOnlyList<string> categories = AuctionQueryMapper.Categories();

            CollectionAssert.Contains(categories, "Chest");
            CollectionAssert.Contains(categories, "Pickaxe");
            CollectionAssert.Contains(categories, "Other");
            CollectionAssert.DoesNotContain(categories, "None");
        }

        [Test]
        public void ToListingRecord_PresentsTheSearchRowForTheExistingBinder()
        {
            var row = new SearchRow
            {
                ListingId = "l1",
                SellerAccountId = "seller",
                SellerPlayerName = "Seller Name",
                ItemName = "Iron Ore",
                StackCount = 4,
                AskingPrice = 25,
                ListedAtTicks = 11,
                ExpiresAtTicks = 22,
                SellerZoneId = "server-b",
            };

            AuctionListingRecord listing = AuctionQueryMapper.ToListingRecord(row);

            Assert.AreEqual("l1", listing.ListingId);
            Assert.AreEqual(25, listing.PricePerUnit);
            Assert.AreEqual(100, listing.TotalPrice);
            Assert.AreEqual(AuctionListingStatus.Active, listing.Status);
            Assert.AreEqual("server-b", listing.SellerZoneId);
        }

        [Test]
        public void SavedSearch_AddRejectsDuplicatesIgnoringPageAndWhitespace()
        {
            var saved = new List<AuctionQuery>();
            var query = new AuctionQuery { Text = "iron", Category = "Chest", SortBy = AuctionSort.PriceAsc };

            Assert.AreEqual(SavedSearchRule.Ok, AuctionSavedSearchRules.Add(saved, query));

            var same = new AuctionQuery
            {
                Text = "  iron  ",
                Category = " Chest ",
                SortBy = AuctionSort.PriceAsc,
                Page = 9,
            };
            Assert.AreEqual(SavedSearchRule.Duplicate, AuctionSavedSearchRules.Add(saved, same));
            Assert.AreEqual(1, saved.Count);
        }

        [Test]
        public void SavedSearch_IsCappedAtTen()
        {
            var saved = new List<AuctionQuery>();
            for (int i = 0; i < AuctionSavedSearchRules.MaxSavedSearches; i++)
            {
                Assert.AreEqual(SavedSearchRule.Ok,
                    AuctionSavedSearchRules.Add(saved, new AuctionQuery { Text = "item-" + i }));
            }

            Assert.AreEqual(SavedSearchRule.Full,
                AuctionSavedSearchRules.Add(saved, new AuctionQuery { Text = "one-more" }));
        }

        [Test]
        public void SavedSearch_NormalizeDoesNotMutateTheCallersQuery()
        {
            var query = new AuctionQuery { Text = " x ", Page = 4, PageSize = 0 };

            AuctionQuery normalized = AuctionSavedSearchRules.Normalize(query);

            Assert.AreEqual("x", normalized.Text);
            Assert.AreEqual(0, normalized.Page);
            Assert.AreEqual(" x ", query.Text, "Normalising a saved query must not rewrite the live bar.");
            Assert.AreEqual(4, query.Page);
        }

        [Test]
        public void SavedSearch_RemoveAtRejectsOutOfRange()
        {
            var saved = new List<AuctionQuery> { new AuctionQuery() };

            Assert.AreEqual(SavedSearchRule.NotFound, AuctionSavedSearchRules.RemoveAt(saved, 5));
            Assert.AreEqual(SavedSearchRule.Ok, AuctionSavedSearchRules.RemoveAt(saved, 0));
            Assert.IsEmpty(saved);
        }

        [Test]
        public void Protocol_RoundTripsSearchRowsAndSavedQueries()
        {
            var rows = new List<SearchRow>
            {
                new SearchRow { ListingId = "l1", ItemName = "Iron Ore", AskingPrice = 10 },
            };
            var queries = new List<AuctionQuery>
            {
                new AuctionQuery { Text = "ore", Category = "Other" },
            };

            List<SearchRow> decodedRows = AuctionClientProtocol.DecodeSearchRows(
                AuctionClientProtocol.EncodeSearchRows(rows));
            List<AuctionQuery> decodedQueries = AuctionClientProtocol.DecodeSavedSearches(
                AuctionClientProtocol.EncodeSavedSearches(queries));

            Assert.AreEqual(1, decodedRows.Count);
            Assert.AreEqual("l1", decodedRows[0].ListingId);
            Assert.AreEqual(1, decodedQueries.Count);
            Assert.AreEqual("ore", decodedQueries[0].Text);
        }

        [Test]
        public void Protocol_DecodeQuery_ToleratesEmptyOrMalformedJson()
        {
            Assert.IsNotNull(AuctionClientProtocol.DecodeQuery(null));
            Assert.IsNotNull(AuctionClientProtocol.DecodeQuery("{ not json"));
        }
    }
}
