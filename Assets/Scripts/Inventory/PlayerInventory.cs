using System;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using MMO.Content;
using MMO.Crafting;
using MMO.Gameplay;
using MMO.Social;
using UnityEngine;

namespace MMO.Inventory
{
    /// <summary>
    /// The two containers a player carries: a fixed grid bag — 8×6 slots plus a five-cell mount
    /// row (<see cref="MountExtraSlots"/>) — of items (a <c>SyncList&lt;ItemInstance&gt;</c> whose
    /// entries carry their cell and footprint) and one explicit equipment slot per
    /// <see cref="EquipmentSlot"/> value, where every item in either list is identified by its
    /// <see cref="ItemInstance.ItemName"/> key rather than a reference, because FishNet serializes
    /// value fields and never Unity objects.
    ///
    /// The client never writes either list. It sends *intent* through
    /// <see cref="RequestEquip"/> / <see cref="RequestUnequip"/> — the idiomatic FishNet
    /// owner-checked <c>[ServerRpc]</c> round trip — and the server answers with a mutation of
    /// the <c>SyncList</c>s themselves, which replicate the new truth to the owning client (and
    /// every observer) without a bespoke result message to reconcile.
    ///
    /// Every equip passes three server-side gates before anything moves: the item resolves
    /// (<see cref="ItemDefinitionRegistry.Resolve"/>), the wearer meets
    /// <see cref="InventoryValidator.CanEquip"/>, and — when a slot swap displaces another item —
    /// the displaced item demonstrably fits back into the bag (checked against a scratch copy of
    /// the list *without* the moving item, so the moving item's own cell counts as free and no
    /// half-applied rollback can flash on screen). After any change,
    /// <see cref="RecalculateEquipmentStats"/> sums what is worn and pushes it into
    /// <see cref="PlayerStats"/>, whose caps then govern every health and mana clamp.
    ///
    /// <para>
    /// As an <see cref="IInventorySource"/> the component is also what a grid view binds to.
    /// The bag view subscribes to <see cref="ContainersChanged"/> — which the two
    /// <c>SyncList.OnChange</c> callbacks below already raise — and repaints from that, so the
    /// grid moves only when the server actually moved something.
    /// </para>
    /// </summary>
    public sealed class PlayerInventory : NetworkBehaviour, IInventorySource, IEquipmentSource
    {
        /// <summary>Bag columns.</summary>
        public const int GridWidth = 8;

        /// <summary>Bag rows.</summary>
        public const int GridHeight = 6;

        /// <summary>
        /// Extra carrying cells a mount adds. They sit in a partial row under the six full rows,
        /// so an 8-wide bag gets a five-cell last row.
        /// </summary>
        public const int MountExtraSlots = 5;

        /// <summary>
        /// Slots the bag has without a mount. A dismounted player carrying more than this is
        /// overloaded and walks slower (<see cref="MountRules"/>).
        /// </summary>
        public const int BaseSlotCount = GridWidth * GridHeight;

        /// <summary>Rows a bag view draws: the square rows plus the partial mount row.</summary>
        public const int VisibleGridHeight = GridHeight + 1;

        // The constants above cannot satisfy an interface property, so the geometry is handed to
        // IInventorySource readers through these explicit implementations instead. A bag screen
        // that only ever sees the interface still draws the extra mount row.
        int IInventorySource.GridWidth => GridWidth;

        int IInventorySource.GridHeight => VisibleGridHeight;

        // Both lists are server-owned SyncTypes: clients hold a replicated copy and are refused
        // if they try to write (the base class warns and the next delta overwrites them).
        private readonly SyncList<ItemInstance> _items = new();
        private readonly SyncList<ItemInstance> _equipment = new();

        // The geometry never changes for a given player, so one grid object serves every check.
        private readonly InventoryGrid _grid = new(GridWidth, GridHeight, MountExtraSlots);

        // True while a mount is active. Its trailing five slots are placeable then; the server
        // sets this from PlayerMount, and a dismounted player's existing items there stay put.
        private bool _mountCapacity;

        /// <summary>Server: open the mount's trailing slots while riding and close them after.</summary>
        [Server]
        public void SetMountCapacity(bool mounted)
        {
            _mountCapacity = mounted;
        }

        // How many of the grid's cells may receive a new item right now.
        private int CellLimit => _mountCapacity ? _grid.CellCount : BaseSlotCount;

        // Server-side staging list for "the bag minus one item" checks; never touches replication.
        private readonly List<ItemInstance> _scratch = new();

        // Reused across recalculations so a recalc allocates nothing.
        private readonly List<ItemDefinition> _equippedDefinitions = new();

        private PlayerStats _stats;

        /// <summary>Replicated bag contents (read-only view; mutation is server-only).</summary>
        public IReadOnlyList<ItemInstance> Items => _items;

        /// <summary>
        /// How many item entries the bag holds. Items do not stack yet, so this is also the number
        /// of occupied slots and the number the overload rule counts.
        /// </summary>
        public int ItemCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < _items.Count; i++)
                {
                    if (!_items[i].IsEmpty)
                        count++;
                }

                return count;
            }
        }

        /// <summary>Replicated equipment slots, indexed by <see cref="EquipmentSlot"/>.</summary>
        public IReadOnlyList<ItemInstance> Equipment => _equipment;

        /// <summary>
        /// Raised on the server and on the owning client whenever either container changes —
        /// the single hook a bag or paper-doll view needs to repaint, whether the change came
        /// from this machine's request or from the server acting on someone else's.
        /// </summary>
        public event Action ContainersChanged;

        // Null-guarded lazy fetch so pure-logic tests can construct the component without a
        // sibling PlayerStats and still exercise everything except the stat push.
        private PlayerStats Stats => _stats != null ? _stats : _stats = GetComponent<PlayerStats>();

        /// <summary>Server-wide item ownership registry for dupe protection; null in tests or on clients.</summary>
        public ItemOwnershipRegistry _registry;
        private ItemOwnershipRegistry Registry => IsServer && SocialHubService.Instance != null ? SocialHubService.Instance.OwnershipRegistry : (_registry ??= SocialHubService.Instance?.OwnershipRegistry);

        private string _holderId;

        /// <summary>Sets the persistent account id for this inventory's holder. Called by PlayerGuild on server start.</summary>
        public void SetHolderId(string accountId)
        {
            _holderId = accountId;
        }

        /// <summary>The persistent account id this inventory belongs to; falls back to connection ClientId if not set.</summary>
        internal string HolderId => string.IsNullOrEmpty(_holderId)
            ? (NetworkObject != null && NetworkObject.Owner != null ? NetworkObject.Owner.ClientId.ToString() : string.Empty)
            : _holderId;

        public override void OnStartNetwork()
        {
            // Subscribe before seeding: SyncList buffers pre-start changes and flushes them right
            // after this callback, so both the server's seed below and a late-joining client's
            // initial full-list fill arrive through the same subscription.
            _items.OnChange += OnInventoryChanged;
            _equipment.OnChange += OnEquipmentChanged;

            if (IsServer)
                EnsureEquipmentSlots();
        }

        public override void OnStopNetwork()
        {
            _items.OnChange -= OnInventoryChanged;
            _equipment.OnChange -= OnEquipmentChanged;
        }

        /// <summary>Bag entry at a list index, or an empty instance when out of range.</summary>
        public ItemInstance GetInventoryItem(int index)
        {
            return index >= 0 && index < _items.Count ? _items[index] : ItemInstance.Empty;
        }

        /// <summary>
        /// The item covering a bag cell, or an empty instance when the cell is empty. Resolved
        /// from live list contents, so it never returns an index the server has since shifted.
        /// </summary>
        public ItemInstance GetItemAt(int gridX, int gridY)
        {
            int index = _grid.FindAt(_items, gridX, gridY);
            return index >= 0 ? _items[index] : ItemInstance.Empty;
        }

        /// <summary>The item worn in <paramref name="slot"/>, or an empty instance.</summary>
        public ItemInstance GetEquipped(EquipmentSlot slot)
        {
            return EquipmentSlots.IsValid(slot) && (int)slot < _equipment.Count
                ? _equipment[(int)slot]
                : ItemInstance.Empty;
        }

        /// <summary>
        /// Owner-side intent: ask the server to equip whatever sits at this bag cell. The client
        /// decides nothing — no level check, no space check, not even that the cell is occupied;
        /// the server will look at its own list when the request lands.
        /// </summary>
        public void RequestEquip(int gridX, int gridY)
        {
            if (!IsOwner)
                return;

            RpcRequestEquip(gridX, gridY);
        }

        [ServerRpc]
        private void RpcRequestEquip(int gridX, int gridY, NetworkConnection sender = null)
        {
            // NetworkConnection has no IsOwner in FishNet 4.x; compare against our owner so a
            // client can only ever move items out of its own bag.
            if (sender == null || !OwnerMatches(sender))
                return;

            TryEquip(gridX, gridY);
        }

        /// <summary>Owner-side intent: ask the server to remove whatever is worn in this slot.</summary>
        public void RequestUnequip(EquipmentSlot slot)
        {
            if (!IsOwner)
                return;

            RpcRequestUnequip(slot);
        }

        [ServerRpc]
        private void RpcRequestUnequip(EquipmentSlot slot, NetworkConnection sender = null)
        {
            if (sender == null || !OwnerMatches(sender))
                return;

            TryUnequip(slot);
        }

        /// <summary>
        /// Owner-side intent: pick up everything in <paramref name="bag"/>. The client supplies
        /// only a reference to the bag it clicked; the server re-derives the distance and decides
        /// whether the looter is close enough, so a client cannot vacuum a bag from across the map.
        /// </summary>
        public void RequestLoot(LootBag bag)
        {
            if (!IsOwner || bag == null || bag.NetworkObject == null)
                return;

            RpcRequestLoot(bag.NetworkObject);
        }

        [ServerRpc]
        private void RpcRequestLoot(NetworkObject bagObject, NetworkConnection sender = null)
        {
            if (sender == null || !OwnerMatches(sender) || bagObject == null)
                return;

            if (!bagObject.TryGetComponent(out LootBag bag))
                return;

            if (!LootBag.IsWithinRange(transform.position, bag.transform.position, bag.InteractRange))
                return;

            bag.Loot(this);
        }

        /// <summary>
        /// Owner-side intent: open <paramref name="chest"/>. The client supplies only the object
        /// it clicked; the server re-checks reach and rolls the table.
        /// </summary>
        public void RequestOpenChest(LootChest chest)
        {
            if (!IsOwner || chest == null || chest.NetworkObject == null)
                return;

            RpcRequestOpenChest(chest.NetworkObject);
        }

        [ServerRpc]
        private void RpcRequestOpenChest(NetworkObject chestObject, NetworkConnection sender = null)
        {
            if (sender == null || !OwnerMatches(sender) || chestObject == null)
                return;

            if (!chestObject.TryGetComponent(out LootChest chest))
                return;

            if (!LootBag.IsWithinRange(transform.position, chest.transform.position, chest.InteractRange))
                return;

            chest.TryOpen(this);
        }

        /// <summary>
        /// Server: place a newly granted item at the first free spot
        /// (<see cref="InventoryGrid.TryFindFreeSpace"/>), or refuse when the bag is full.
        /// Inventory acquisition never checks level — you may hold what you cannot yet wear.
        /// </summary>
        [Server]
        public bool TryAddItem(ItemDefinition definition)
        {
            if (definition == null)
                return false;

            if (!_grid.TryFindFreeSpace(_items, definition.GridWidth, definition.GridHeight, out int x, out int y, CellLimit))
                return false;

            var instance = ItemInstance.Create(definition, x, y);
            _items.Add(instance);

            var registry = Registry;
            if (registry != null && !string.IsNullOrEmpty(instance.InstanceId))
                registry.Grant(instance.InstanceId, HolderId);

            return true;
        }

        /// <summary>
        /// Server: move the item covering bag cell (<paramref name="gridX"/>,
        /// <paramref name="gridY"/>) into the slot its definition designates, verifying level
        /// requirement, slot validity, and — when the slot is occupied — that the displaced item
        /// fits back in the bag, all before a single element moves. Returns false when any gate
        /// refuses; a false here is a no-op, never a rollback.
        /// </summary>
        [Server]
        public bool TryEquip(int gridX, int gridY)
        {
            EnsureEquipmentSlots();

            // Resolve through the cell, not a client-supplied index: the list may have shifted
            // between the click and this call, and a stale index would grab the wrong item.
            int index = _grid.FindAt(_items, gridX, gridY);
            if (index < 0)
                return false;

            ItemInstance instance = _items[index];
            ItemDefinition definition = ItemDefinitionRegistry.Resolve(instance.ItemName);
            if (definition == null)
            {
                Debug.LogError($"{nameof(PlayerInventory)}: cannot equip '{instance.ItemName}'; no definition registered or loadable.");
                return false;
            }

            PlayerStats stats = Stats;
            int playerLevel = stats != null ? stats.Level : 0;
            if (!InventoryValidator.CanEquip(playerLevel, definition))
                return false;

            EquipmentSlot slot = definition.EquipSlot;
            if (!EquipmentSlots.IsValid(slot))
            {
                Debug.LogError($"{nameof(PlayerInventory)}: '{instance.ItemName}' names an unknown equip slot.");
                return false;
            }

            ItemInstance displaced = _equipment[(int)slot];

            // Space gate for a swap: stage the bag without the moving item, so its own cell
            // counts as free, and ask whether the displaced item has anywhere to land.
            _scratch.Clear();
            for (int i = 0; i < _items.Count; i++)
            {
                if (i != index)
                    _scratch.Add(_items[i]);
            }

            int freeX = 0;
            int freeY = 0;
            if (!displaced.IsEmpty &&
                !_grid.TryFindFreeSpace(_scratch, displaced.Width, displaced.Height, out freeX, out freeY, CellLimit))
            {
                return false;
            }

            // Every gate passed — now, and only now, mutate.
            _items.RemoveAt(index);
            _equipment.Set((int)slot, instance);

            if (!displaced.IsEmpty)
            {
                displaced.GridX = freeX;
                displaced.GridY = freeY;
                _items.Add(displaced);
            }

            RecalculateEquipmentStats();
            return true;
        }

        /// <summary>
        /// Server: take the item out of <paramref name="slot"/> and drop it into the first free
        /// bag space. A full bag refuses the whole operation — an item that cannot be stored is
        /// not removed from the slot, which is what keeps gear from being deleted by unequipping.
        /// </summary>
        [Server]
        public bool TryUnequip(EquipmentSlot slot)
        {
            if (!EquipmentSlots.IsValid(slot))
                return false;

            EnsureEquipmentSlots();

            ItemInstance worn = _equipment[(int)slot];
            if (worn.IsEmpty)
                return false;

            if (!_grid.TryFindFreeSpace(_items, worn.Width, worn.Height, out int x, out int y, CellLimit))
                return false;

            _equipment.Set((int)slot, ItemInstance.Empty);
            worn.GridX = x;
            worn.GridY = y;
            _items.Add(worn);

            RecalculateEquipmentStats();
            return true;
        }

        /// <summary>
        /// Server: remove the item covering bag cell (<paramref name="gridX"/>, <paramref name="gridY"/>).
        /// Used by the guild bank deposit path, which removes from the bag only after the bank's
        /// versioned write has won, so the item can never be lost. Returns false on an empty cell.
        /// </summary>
        [Server]
        public bool RemoveItemAt(int gridX, int gridY)
        {
            int index = _grid.FindAt(_items, gridX, gridY);
            if (index < 0)
                return false;

            var item = _items[index];
            _items.RemoveAt(index);

            var registry = Registry;
            if (registry != null && !string.IsNullOrEmpty(item.InstanceId))
                registry.Release(item.InstanceId, HolderId);

            return true;
        }

        /// <summary>
        /// Server: remove the bag item with <paramref name="instanceId"/> and hand it back to the
        /// caller. Used by direct trade, which resolves by identity rather than by a cell that may
        /// have shifted since the client clicked.
        /// </summary>
        [Server]
        public bool TryRemoveItem(string instanceId, out ItemInstance item)
        {
            item = ItemInstance.Empty;
            if (string.IsNullOrEmpty(instanceId))
                return false;

            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i].InstanceId != instanceId)
                    continue;

                item = _items[i];
                _items.RemoveAt(i);

                var registry = Registry;
                if (registry != null && !string.IsNullOrEmpty(item.InstanceId))
                    registry.Release(item.InstanceId, HolderId);

                return true;
            }

            return false;
        }

        /// <summary>
        /// Server: drop an existing item (e.g. one withdrawn from a guild bank) into a specific free
        /// cell, re-validating that the rectangle is still clear. Returns false when the cell is
        /// occupied — the caller must not lose the item in that case.
        /// </summary>
        [Server]
        public bool TryAddExistingItem(ItemInstance item, int gridX, int gridY)
        {
            if (item.IsEmpty)
                return false;

            if (!_grid.CanPlace(_items, gridX, gridY, Math.Max(1, item.Width), Math.Max(1, item.Height), CellLimit))
                return false;

            item.GridX = gridX;
            item.GridY = gridY;
            _items.Add(item);

            var registry = Registry;
            if (registry != null && !string.IsNullOrEmpty(item.InstanceId))
                registry.Grant(item.InstanceId, HolderId);

            return true;
        }

        /// <summary>
        /// Server: drop an existing item into the first free bag cell, preserving its durability
        /// and stack count. The loot-bag pickup path: an item that must keep the wear it was
        /// found with cannot go through <see cref="TryAddItem(ItemDefinition)"/>, which mints a
        /// fresh instance. Returns false when the bag cannot hold the item.
        /// </summary>
        [Server]
        public bool TryAddExistingItem(ItemInstance item)
        {
            if (item.IsEmpty)
                return false;

            if (!_grid.TryFindFreeSpace(_items, Math.Max(1, item.Width), Math.Max(1, item.Height), out int x, out int y, CellLimit))
                return false;

            item.GridX = x;
            item.GridY = y;
            _items.Add(item);

            var registry = Registry;
            if (registry != null && !string.IsNullOrEmpty(item.InstanceId))
                registry.Grant(item.InstanceId, HolderId);

            return true;
        }

        /// <summary>
        /// Server: wear down every destructible item in the equipment list by
        /// <paramref name="amount"/>, returning how many items actually changed. Applied on a
        /// safe-zone death as the repair-bill half of the gold sink; indestructible items (a
        /// definition whose <see cref="ItemDefinition.MaxDurability"/> is zero) are left alone.
        /// </summary>
        [Server]
        public int ApplyDurabilityDamage(int amount)
        {
            if (amount <= 0)
                return 0;

            int damaged = 0;
            for (int i = 0; i < _equipment.Count; i++)
            {
                ItemInstance worn = _equipment[i];
                if (worn.IsEmpty)
                    continue;

                ItemInstance wornOut = RiskRules.Wear(worn, amount);
                if (wornOut.Durability == worn.Durability)
                    continue;

                _equipment.Set(i, wornOut);
                damaged++;
            }

            return damaged;
        }

        /// <summary>
        /// Server: everything the player is carrying — bag and worn gear — as a flat list, the
        /// snapshot a black-zone death instantiates as a loot bag. Empty cells and vacant slots
        /// are omitted; the returned instances are copies, so filling a loot bag does not alias
        /// the live containers.
        /// </summary>
        [Server]
        public List<ItemInstance> LootableItems()
        {
            return CollectLootable(_items, _equipment);
        }

        /// <summary>
        /// Server: empty the bag and every equipment slot, then re-derive the stat caps. The
        /// "database profile is wiped of those items" half of a full-loot death; callers only
        /// do this once the dropped items demonstrably exist in a loot bag, so nothing is lost
        /// to a failed spawn.
        /// </summary>
        [Server]
        public void ClearAll()
        {
            var registry = Registry;
            var holder = HolderId;

            for (int i = 0; i < _items.Count; i++)
            {
                var item = _items[i];
                if (!item.IsEmpty && registry != null && !string.IsNullOrEmpty(item.InstanceId))
                    registry.Release(item.InstanceId, holder);
            }

            for (int i = 0; i < _equipment.Count; i++)
            {
                var item = _equipment[i];
                if (!item.IsEmpty && registry != null && !string.IsNullOrEmpty(item.InstanceId))
                    registry.Release(item.InstanceId, holder);
            }

            _items.Clear();
            for (int i = 0; i < _equipment.Count; i++)
                _equipment.Set(i, ItemInstance.Empty);

            RecalculateEquipmentStats();
        }

        /// <summary>
        /// The pure half of <see cref="LootableItems"/>: the non-empty entries of two containers
        /// in order. Callable from an EditMode test with two plain lists and no server.
        /// </summary>
        public static List<ItemInstance> CollectLootable(
            IReadOnlyList<ItemInstance> bag,
            IReadOnlyList<ItemInstance> equipment)
        {
            var loot = new List<ItemInstance>();

            if (bag != null)
            {
                for (int i = 0; i < bag.Count; i++)
                {
                    if (!bag[i].IsEmpty)
                        loot.Add(bag[i]);
                }
            }

            if (equipment != null)
            {
                for (int i = 0; i < equipment.Count; i++)
                {
                    if (!equipment[i].IsEmpty)
                        loot.Add(equipment[i]);
                }
            }

            return loot;
        }

        /// <summary>
        /// Server: rebuild the equipped-item picture from the equipment list and push the sums
        /// into <c>PlayerStats</c>. Called after every equip/unequip so the stat caps are always
        /// derived from state, never incremented — recalculating instead of adding is what makes
        /// a swap one event instead of two to keep in sync.
        /// </summary>
        [Server]
        public void RecalculateEquipmentStats()
        {
            PlayerStats stats = Stats;
            if (stats == null)
            {
                Debug.LogWarning($"{nameof(PlayerInventory)}: no {nameof(PlayerStats)} on this object; equipment bonuses were not applied.");
                return;
            }

            EquipmentBonusCalculator.Collect(_equipment, _equippedDefinitions);
            stats.ApplyEquipmentBonuses(
                EquipmentBonusCalculator.MaxHealthBonus(_equippedDefinitions),
                EquipmentBonusCalculator.MaxManaBonus(_equippedDefinitions));
        }

        /// <summary>Total <see cref="ItemDefinition.MaxHealthBonus"/> over a set of definitions.</summary>
        public static int ComputeMaxHealthBonus(IReadOnlyList<ItemDefinition> definitions)
            => EquipmentBonusCalculator.MaxHealthBonus(definitions);

        /// <summary>Total <see cref="ItemDefinition.MaxManaBonus"/> over a set of definitions.</summary>
        public static int ComputeMaxManaBonus(IReadOnlyList<ItemDefinition> definitions)
            => EquipmentBonusCalculator.MaxManaBonus(definitions);

        /// <summary>
        /// Server: rebuild both containers from a loaded save in one shot, then re-derive the
        /// equipment stat caps. Empty bag slots are not stored, so the bag is rebuilt as a dense
        /// list of the saved items; equipment entries are padded to the slot count, and anything
        /// past it is ignored rather than grown into the container.
        /// </summary>
        [Server]
        public void Restore(IReadOnlyList<ItemInstance> bag, IReadOnlyList<ItemInstance> equipment)
        {
            EnsureEquipmentSlots();

            var registry = Registry;
            var holder = HolderId;

            _items.Clear();
            if (bag != null)
            {
                for (int i = 0; i < bag.Count; i++)
                {
                    if (!bag[i].IsEmpty)
                    {
                        _items.Add(bag[i]);
                        if (registry != null && !string.IsNullOrEmpty(bag[i].InstanceId))
                            registry.Grant(bag[i].InstanceId, holder);
                    }
                }
            }

            for (int i = 0; i < _equipment.Count; i++)
            {
                ItemInstance restored = equipment != null && i < equipment.Count
                    ? equipment[i]
                    : ItemInstance.Empty;

                _equipment.Set(i, restored);
                if (!restored.IsEmpty && registry != null && !string.IsNullOrEmpty(restored.InstanceId))
                    registry.Grant(restored.InstanceId, holder);
            }

            RecalculateEquipmentStats();
        }

        [Server]
        public bool TryCraft(MMO.Crafting.RecipeData recipe)
        {
            if (recipe == null || recipe.Output == null)
                return false;

            _scratch.Clear();
            for (int i = 0; i < _items.Count; i++)
                _scratch.Add(_items[i]);

            var ingredients = recipe.Ingredients;
            var consumedInstanceIds = new List<string>();

            for (int i = 0; i < ingredients.Count; i++)
            {
                var ingredient = ingredients[i];
                if (ingredient.Item == null)
                    return false;

                for (int n = 0; n < ingredient.Count; n++)
                {
                    int index = FindByName(_scratch, ingredient.Item.ItemName);
                    if (index < 0)
                        return false;
                    if (!_scratch[index].IsEmpty && !string.IsNullOrEmpty(_scratch[index].InstanceId))
                        consumedInstanceIds.Add(_scratch[index].InstanceId);
                    _scratch.RemoveAt(index);
                }
            }

            if (!_grid.TryFindFreeSpace(_scratch, recipe.Output.GridWidth, recipe.Output.GridHeight,
                    out int x, out int y, CellLimit))
                return false;

            for (int i = 0; i < ingredients.Count; i++)
            {
                var ingredient = ingredients[i];
                for (int n = 0; n < ingredient.Count; n++)
                {
                    int index = FindByName(_items, ingredient.Item.ItemName);
                    if (index >= 0)
                    {
                        var consumed = _items[index];
                        if (!consumed.IsEmpty && !string.IsNullOrEmpty(consumed.InstanceId))
                            consumedInstanceIds.Add(consumed.InstanceId);
                        _items.RemoveAt(index);
                    }
                }
            }

            var registry = Registry;
            var holder = HolderId;

            foreach (var instanceId in consumedInstanceIds)
            {
                if (registry != null && !string.IsNullOrEmpty(instanceId))
                    registry.Release(instanceId, holder);
            }

            for (int outCount = 0; outCount < recipe.OutputCount; outCount++)
            {
                var output = ItemInstance.Create(recipe.Output, x, y);
                _items.Add(output);
                if (registry != null && !string.IsNullOrEmpty(output.InstanceId))
                    registry.Grant(output.InstanceId, holder);
            }
            return true;
        }

        private static int FindByName(IList<ItemInstance> list, string itemName)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (!list[i].IsEmpty && list[i].ItemName == itemName)
                    return i;
            }
            return -1;
        }

        [Server]
        public int CountItem(string itemName) => MMO.Crafting.CraftingValidator.Count(_items, itemName);

        /// <summary>
        /// Finds this process's player inventory — the spawned object owned by the local client.
        /// Used by world click handlers that need to send an owner-checked intent.
        /// </summary>
        public static PlayerInventory FindLocal(NetworkManager networkManager)
        {
            if (networkManager == null || networkManager.ServerManager == null || networkManager.ClientManager == null)
                return null;

            IReadOnlyDictionary<int, NetworkObject> spawned = networkManager.IsHostStarted
                ? networkManager.ServerManager.Objects.Spawned
                : networkManager.ClientManager.Objects.Spawned;

            foreach (NetworkObject nob in spawned.Values)
            {
                if (nob != null && nob.IsSpawned && nob.IsOwner && nob.TryGetComponent(out PlayerInventory inventory))
                    return inventory;
            }

            return null;
        }

        // Idempotent, and tolerant of running before or after the list first syncs: an equipment
        // list shorter than the slot count would index past its end, and a host runs OnStartNetwork
        // once while a pure client receives its five seeded entries in the spawn payload.
        private void EnsureEquipmentSlots()
        {
            while (_equipment.Count < EquipmentSlots.Count)
                _equipment.Add(ItemInstance.Empty);
        }

        private void OnInventoryChanged(
            SyncListOperation operation, int index, ItemInstance oldItem, ItemInstance newItem, bool asServer)
        {
            LogContainerChange("inventory", operation, index, oldItem, newItem);
            ContainersChanged?.Invoke();
        }

        private void OnEquipmentChanged(
            SyncListOperation operation, int index, ItemInstance oldItem, ItemInstance newItem, bool asServer)
        {
            LogContainerChange("equipment", operation, index, oldItem, newItem);
            ContainersChanged?.Invoke();
        }

        private void LogContainerChange(
            string container, SyncListOperation operation, int index, ItemInstance oldItem, ItemInstance newItem)
        {
            Debug.Log($"[ClientId {OwnerId}] {container}[{index}] {operation}: " +
                      $"{(oldItem.IsEmpty ? "empty" : oldItem.ItemName)} -> {(newItem.IsEmpty ? "empty" : newItem.ItemName)}");
        }
    }
}
