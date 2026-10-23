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

        /// <summary>
        /// The slot a mount item is worn in. Appended after the original five so their wire
        /// values are unchanged; a mount only works while equipped here.
        /// </summary>
        Mount = 5,

        /// <summary>
        /// Not wearable: a bag or bank item that is neither gear nor a mount. Appended so no
        /// existing slot moves; <see cref="EquipmentSlots.IsValid"/> rejects it.
        /// </summary>
        None = 6,
    }
}
