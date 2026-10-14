using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using MMO.Inventory;
using UnityEngine;

namespace MMO.Gameplay
{
    /// <summary>
    /// A pile of dropped items on the ground, spawned by the server when a player dies in a
    /// full-loot zone. The contents are a server-write <c>SyncList&lt;ItemInstance&gt;</c>, so a
    /// client sees exactly what the server put in and can never author it — the same
    /// authoritative-container shape <see cref="PlayerInventory"/> uses.
    ///
    /// The bag is <em>unowned</em>: nobody owns it, so a looter cannot send this object a
    /// <c>[ServerRpc]</c> (FishNet requires ownership for that). The pickup intent therefore
    /// travels through the looter's own <see cref="PlayerInventory"/>, which is owned and which
    /// re-checks range on the server before calling <see cref="Loot"/>. A bag that empties
    /// despawns itself, so nothing lingers to be farmed twice.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LootBag : NetworkBehaviour
    {
        [SerializeField]
        private float _interactRange = 3f;

        private readonly SyncList<ItemInstance> _contents = new();

        /// <summary>Replicated contents (read-only view; mutation is server-only).</summary>
        public IReadOnlyList<ItemInstance> Contents => _contents;

        /// <summary>True once every dropped item has been picked up.</summary>
        public bool IsEmpty => _contents.Count == 0;

        /// <summary>How close a looter must stand, in metres.</summary>
        public float InteractRange => Mathf.Max(0f, _interactRange);

        /// <summary>Reconfigures the pickup range (an editor baker or a test).</summary>
        public void SetInteractRange(float range)
        {
            _interactRange = Mathf.Max(0f, range);
        }

        /// <summary>
        /// Server: put <paramref name="items"/> into the bag. Empty instances are skipped, so a
        /// bag never replicates a hole. Called by the death resolver right after the bag spawns.
        /// </summary>
        [Server]
        public void Fill(IReadOnlyList<ItemInstance> items)
        {
            if (items == null)
                return;

            for (int i = 0; i < items.Count; i++)
            {
                if (!items[i].IsEmpty)
                    _contents.Add(items[i]);
            }
        }

        /// <summary>
        /// Server: move as much of the bag as <paramref name="inventory"/> can hold into it,
        /// preserving each item's durability, and despawn the bag once empty. Returns true when
        /// at least one item moved. Iterating backwards makes the removals safe, and an item that
        /// does not fit is left behind rather than destroyed.
        /// </summary>
        [Server]
        public bool Loot(PlayerInventory inventory)
        {
            if (inventory == null || _contents.Count == 0)
                return false;

            bool moved = false;
            for (int i = _contents.Count - 1; i >= 0; i--)
            {
                ItemInstance item = _contents[i];
                if (!inventory.TryAddExistingItem(item))
                    continue;

                _contents.RemoveAt(i);
                moved = true;
            }

            if (_contents.Count == 0 && NetworkManager != null && NetworkManager.ServerManager != null)
                NetworkManager.ServerManager.Despawn(NetworkObject);

            return moved;
        }

        /// <summary>
        /// True when <paramref name="looterPosition"/> is strictly closer to the bag than
        /// <paramref name="range"/>. Matches the combat and gathering reach tests so a looter on
        /// the boundary is refused the same way everywhere.
        /// </summary>
        public static bool IsWithinRange(Vector3 looterPosition, Vector3 bagPosition, float range)
        {
            return range > 0f && Vector3.Distance(looterPosition, bagPosition) < range;
        }
    }
}
