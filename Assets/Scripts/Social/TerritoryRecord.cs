using System;

namespace MMO.Social
{
    /// <summary>The durable owner of one scene claim flag.</summary>
    [Serializable]
    public sealed class TerritoryRecord
    {
        public string TerritoryId;
        public string OwnerGuildId;
        public long CapturedAtTicks;

        /// <summary>Tax charged inside this territory, in basis points (100 = 1%). Zero = untaxed.</summary>
        public int TaxRateBp;

        /// <summary>UTC ticks the tax rate was last set.</summary>
        public long SetAtTicks;

        /// <summary>UTC ticks the treasury last settled this territory's collected fees.</summary>
        public long LastSettledAtTicks;

        /// <summary>Backend revision for the optimistic write; empty on create.</summary>
        public string StorageVersion;
    }
}
