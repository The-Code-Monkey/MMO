using System;
using System.Collections.Generic;

namespace MMO.Persistence
{
    /// <summary>
    /// Lightweight, serializable DTO representing inventory state for saving.
    /// Contains item IDs, stack counts, and durability values.
    /// </summary>
    [Serializable]
    public struct InventorySaveData
    {
        public List<int> ItemIds;
        public List<int> StackCounts;
        public List<int> Durability;
    }
}
