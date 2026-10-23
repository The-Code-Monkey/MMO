using System;

namespace MMO.Inventory
{
    /// <summary>
    /// The worn-gear side a paper-doll view binds to: what is equipped in each slot, the
    /// owner-side unequip intent, and the same change event the bag uses.
    ///
    /// <see cref="PlayerInventory"/> is the networked implementation. The interface exists for the
    /// same reason <see cref="IInventorySource"/> does: an equipment screen can be driven by a plain
    /// in-memory source in an EditMode test, with no server and no socket.
    /// </summary>
    public interface IEquipmentSource
    {
        /// <summary>The item worn in <paramref name="slot"/>, or an empty instance.</summary>
        ItemInstance GetEquipped(EquipmentSlot slot);

        /// <summary>Owner-side intent: ask the server to take the item out of this slot.</summary>
        void RequestUnequip(EquipmentSlot slot);

        /// <summary>Raised whenever the bag or the worn equipment changes.</summary>
        event Action ContainersChanged;
    }
}
