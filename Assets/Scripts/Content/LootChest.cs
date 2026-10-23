using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using MMO.Gameplay;
using MMO.Inventory;
using UnityEngine;
using Random = System.Random;

namespace MMO.Content
{
    /// <summary>
    /// A one-shot loot container. Opening is a server decision: the client only supplies the
    /// object it clicked, and the server re-checks reach before rolling the table.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LootChest : NetworkBehaviour
    {
        [SerializeField] private WeightedLootTable _table;
        [SerializeField, Min(0f)] private float _interactRange = 3f;

        private readonly SyncVar<bool> _opened = new();
        private Random _rng = new();

        public WeightedLootTable Table => _table;
        public bool Opened => _opened.Value;
        public float InteractRange => Mathf.Max(0f, _interactRange);

        /// <summary>Points the chest at a table (a prefab author or a test).</summary>
        public void SetTable(WeightedLootTable table) => _table = table;

        /// <summary>Reconfigures the reach (a prefab author or a test).</summary>
        public void SetInteractRange(float range) => _interactRange = Mathf.Max(0f, range);

        /// <summary>Replaces the roll source; tests use a deterministic random.</summary>
        public void SetRandom(Random rng) => _rng = rng ?? new Random();

        public override void OnStartNetwork()
        {
            _opened.OnChange += OnOpenedChanged;
            ApplyOpenedState(_opened.Value);
        }

        public override void OnStopNetwork()
        {
            _opened.OnChange -= OnOpenedChanged;
        }

        /// <summary>
        /// Server: roll the table once and award it to <paramref name="inventory"/>. A chest whose
        /// drops cannot all be placed stays closed so it can be retried instead of losing them.
        /// </summary>
        [Server]
        public bool TryOpen(PlayerInventory inventory)
        {
            if (!IsServer || _opened.Value || inventory == null)
                return false;

            List<LootDrop> drops = LootRules.Roll(_table, _rng);
            bool preferBag = ZoneRiskConfig.ResolveTierAt(transform.position) == ZoneRiskTier.Black;
            LootAwardResult result = LootAwarder.Award(
                drops, inventory, NetworkManager, transform.position, preferBag);

            if (result.Leftovers.Count > 0)
            {
                Debug.LogError(
                    $"{nameof(LootChest)}: '{name}' could not place {result.Leftovers.Count} item(s); the chest stays closed.");
                return false;
            }

            _opened.Value = true;
            inventory.GetComponent<PlayerQuestLog>()?.NotifyInteract(name);
            return true;
        }

        /// <summary>Unity click message: ask the local player's inventory to open this chest.</summary>
        private void OnMouseDown()
        {
            RequestOpenFromLocalPlayer();
        }

        /// <summary>
        /// The click's local effect: resolve the local player's inventory and send the intent.
        /// Does nothing on a client that does not own a player, or on a dedicated server.
        /// </summary>
        public void RequestOpenFromLocalPlayer()
        {
            if (_opened.Value)
                return;

            NetworkObject self = NetworkObject;
            if (self == null || self.NetworkManager == null)
                return;

            PlayerInventory inventory = PlayerInventory.FindLocal(self.NetworkManager);
            inventory?.RequestOpenChest(this);
        }

        private void OnOpenedChanged(bool previous, bool current, bool asServer)
        {
            ApplyOpenedState(current);
        }

        private void ApplyOpenedState(bool opened)
        {
            if (TryGetComponent(out Collider chestCollider))
                chestCollider.enabled = !opened;

            if (TryGetComponent(out Renderer chestRenderer))
                chestRenderer.enabled = !opened;
        }
    }
}
