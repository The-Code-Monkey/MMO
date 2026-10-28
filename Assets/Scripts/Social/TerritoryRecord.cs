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

        /// <summary>Backend revision for the optimistic write; empty on create.</summary>
        public string StorageVersion;
    }
}
