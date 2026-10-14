using System.Collections.Generic;
using UnityEngine;

namespace MMO.Auction
{
    /// <summary>
    /// Configuration for which zones have auction houses.
    /// The entire zone acts as an auction house (central trading hub).
    /// </summary>
    [CreateAssetMenu(fileName = "AuctionHouseZoneConfig", menuName = "MMO/Auction House Zone Config")]
    public sealed class AuctionHouseZoneConfig : ScriptableObject
    {
        [SerializeField] private List<string> _auctionHouseZoneIds = new List<string>();

        /// <summary>Zone IDs (matching SocialHubService.ZoneId) that have auction houses.</summary>
        public IReadOnlyList<string> AuctionHouseZoneIds => _auctionHouseZoneIds;

        /// <summary>Checks if a zone has an auction house.</summary>
        public bool HasAuctionHouse(string zoneId)
        {
            if (string.IsNullOrEmpty(zoneId))
                return false;
            return _auctionHouseZoneIds.Contains(zoneId);
        }
    }
}