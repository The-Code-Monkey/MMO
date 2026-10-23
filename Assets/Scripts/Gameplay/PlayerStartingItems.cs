using FishNet.Object;
using MMO.Inventory;
using UnityEngine;

namespace MMO.Gameplay
{
    /// <summary>
    /// Grants the shipped starter mount to every player on the server. Runs after
    /// <c>PlayerSaveTrigger</c> in component order so a restored save is in the bag first and an
    /// existing donkey is not duplicated.
    ///
    /// The item, not a flag, is the authority for having the donkey: if a player has lost theirs,
    /// the next spawn grants another, so nobody is permanently stranded without their starter
    /// mount. It is granted to the bag; the player then equips it into the mount slot, which is
    /// what <see cref="PlayerMount"/> checks before it will let them ride. The server does the
    /// granting; a client never adds items to itself.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerStartingItems : NetworkBehaviour
    {
        /// <summary>The shipped starter mount item every player receives.</summary>
        public const string DonkeyItemName = "Donkey";

        public override void OnStartNetwork()
        {
            if (!IsServer)
                return;

            if (!TryGetComponent(out PlayerInventory inventory))
            {
                Debug.LogError(
                    $"{nameof(PlayerStartingItems)}: the player prefab is missing {nameof(PlayerInventory)}.", this);
                return;
            }

            if (inventory.CountItem(DonkeyItemName) > 0 ||
                inventory.GetEquipped(EquipmentSlot.Mount).ItemName == DonkeyItemName)
                return;

            ItemDefinition donkey = ItemDefinitionRegistry.Resolve(DonkeyItemName);
            if (donkey == null)
            {
                Debug.LogError(
                    $"{nameof(PlayerStartingItems)}: no item definition registered for '{DonkeyItemName}'.", this);
                return;
            }

            if (!inventory.TryAddItem(donkey))
            {
                Debug.LogWarning(
                    $"{nameof(PlayerStartingItems)}: the bag is full; the starter donkey was not added.", this);
            }
        }
    }
}
