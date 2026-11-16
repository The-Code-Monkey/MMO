using System;
using System.Collections.Generic;

namespace MMO.Auction
{
    /// <summary>
    /// One account's saved market queries. Kept as a list of <see cref="AuctionQuery"/> because both
    /// types are <c>JsonUtility</c>-serializable and a saved search is exactly the query object.
    /// </summary>
    [Serializable]
    public sealed class AuctionSavedSearchRecord
    {
        public const string Collection = "auction_saved_searches";

        public string AccountId;
        public List<AuctionQuery> Queries = new List<AuctionQuery>();
        public string StorageVersion;
    }
}
