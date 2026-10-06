namespace MMO.Inventory
{
    /// <summary>
    /// The explicit equipment slots a player wears. These are the only places gear may occupy;
    /// the numeric values are part of the wire contract because <c>PlayerInventory</c> stores
    /// equipped items in a <c>SyncList</c> indexed by <c>(int)slot</c>, so re-ordering the enum
    /// would silently move every player's helmet onto their boots on both ends of the connection.
    /// </summary>
    public enum EquipmentSlot
    {
        MainHand = 0,
        OffHand = 1,
        Head = 2,
        Chest = 3,
        Feet = 4,
    }
}
