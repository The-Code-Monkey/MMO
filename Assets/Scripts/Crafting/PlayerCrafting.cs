using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using MMO.Content;
using MMO.Gameplay;
using MMO.Inventory;
using MMO.Persistence;
using MMO.Progression;
using MMO.UI;
using UnityEngine;

namespace MMO.Crafting
{
    /// <summary>
    /// The player's crafting: replicated XP, the authoritative craft, and the server-side answer
    /// to "what does this station have for me".
    ///
    /// <para>
    /// As an <see cref="ICraftingSource"/> this component is also what the crafting panel binds
    /// to, and the data flow is deliberately one-way. A click on a station sends
    /// <see cref="RpcRequestOpen"/>; the server checks ownership and proximity, then answers that
    /// one client with a <c>[TargetRpc]</c> carrying the filtered <see cref="RecipeOffer"/>
    /// payload. The client never enumerates the recipe registry, and it never decides what is
    /// craftable — <see cref="TryCraft"/> re-runs <see cref="CraftingValidator"/> before anything
    /// moves, so a forged or stale offer name fails safely.
    /// </para>
    /// </summary>
    public sealed class PlayerCrafting : NetworkBehaviour, ICraftingSource
    {
        public const float MaxCraftDistance = 6f;

        /// <summary>How often the server pushes a fresh countdown to its owner.</summary>
        public const float OrderPushIntervalSeconds = 1f;


        private readonly SyncVar<int> _craftingXp = new();

        // Server-owned queue: the orders, their escrow and their absolute UTC deadlines. A pure
        // client holds none of this; it only ever sees CraftOrderView payloads.
        private readonly List<CraftOrder> _orders = new();

        private CraftOrderView[] _orderViews = Array.Empty<CraftOrderView>();
        private string _message = string.Empty;
        private bool _processingOrders;
        private float _nextOrderPush;
        private PlayerSaveTrigger _saveTrigger;

        /// <summary>
        /// Whether a player standing at <paramref name="playerPosition"/> is close enough to a
        /// station at <paramref name="stationPosition"/> to use it.
        ///
        /// <para>
        /// One rule, shared: the server enforces it when it answers an open request and when it
        /// accepts a craft, and the client uses it to decide whether a station needs a walk before
        /// it can be opened. If the two ends measured reach differently, the client would ask for a
        /// station the server refuses — the walk would end and no panel would ever appear.
        /// </para>
        /// </summary>
        public static bool IsWithinReach(Vector3 playerPosition, Vector3 stationPosition)
            => Vector3.Distance(playerPosition, stationPosition) <= MaxCraftDistance;

        private PlayerInventory _inventory;
        private PlayerQuestLog _quests;
        private PlayerAchievements _achievements;
        private IReadOnlyList<RecipeOffer> _availableRecipes = Array.Empty<RecipeOffer>();

        public int CraftingXp => _craftingXp.Value;
        public int CraftingLevel => MMO.Crafting.CraftingXpTable.LevelForXp(_craftingXp.Value);

        /// <summary>The order payload the server last sent, or empty.</summary>
        public IReadOnlyList<CraftOrderView> CraftOrders => _orderViews;

        /// <summary>The last explanation the server sent with a payload, or empty.</summary>
        public string CraftMessage => _message;

        /// <summary>Server-only: the live queue, for save capture.</summary>
        public IReadOnlyList<CraftOrder> Orders => _orders;

        /// <summary>The station the last payload came from, or null.</summary>
        public CraftingStation OpenedStation { get; private set; }

        /// <summary>The offers carried by the last payload; the panel's whole recipe list.</summary>
        public IReadOnlyList<RecipeOffer> AvailableRecipes => _availableRecipes;

        /// <summary>Raised when crafting XP changes, which can change what a station would offer.</summary>
        public event Action RecipesChanged;

        /// <summary>Raised client-side when the server answers an open request with a payload.</summary>
        public event Action<CraftingStation, IReadOnlyList<RecipeOffer>> StationOpened;

        /// <summary>
        /// Raised when the order payload changes: server-side after a queue mutation (so a host's
        /// panel repaints immediately) and client-side when the server pushes a fresh payload.
        /// </summary>
        public event Action OrdersChanged;

        /// <summary>
        /// Server-side: raised after an instant craft's XP or the order list changes, so the save
        /// trigger can mark this player dirty. Never raised on a pure client's replicated copy.
        /// </summary>
        public event Action CraftingChanged;

        private PlayerInventory Inventory =>
            _inventory != null ? _inventory : _inventory = GetComponent<PlayerInventory>();

        private PlayerQuestLog Quests =>
            _quests != null ? _quests : _quests = GetComponent<PlayerQuestLog>();

        public override void OnStartNetwork()
        {
            _craftingXp.OnChange += OnCraftingXpChanged;
        }

        public override void OnStopNetwork()
        {
            _craftingXp.OnChange -= OnCraftingXpChanged;
        }

        /// <summary>
        /// Asks the server to open <paramref name="station"/>; the panel populates when the
        /// server's payload arrives, not when this returns.
        /// </summary>
        public void RequestOpenStation(CraftingStation station)
        {
            if (!IsOwner || station == null)
                return;

            RpcRequestOpen(station.NetworkObject);
        }

        /// <summary>
        /// The server side of an open request: ownership was checked by the RPC, proximity is
        /// checked here, and a station that fails either check simply produces no payload.
        /// </summary>
        [ServerRpc]
        private void RpcRequestOpen(NetworkObject stationObject, NetworkConnection sender = null)
        {
            if (sender == null || !OwnerMatches(sender))
                return;

            TrySendOffers(sender, stationObject);
        }

        /// <summary>
        /// Builds and sends the offer payload for <paramref name="stationObject"/> to
        /// <paramref name="target"/>. Returns false (and sends nothing) when the station is gone
        /// or the player is too far away — the same reach <see cref="TryCraft"/> enforces, so a
        /// client cannot read a station it could not use.
        /// </summary>
        [Server]
        public bool TrySendOffers(NetworkConnection target, NetworkObject stationObject)
        {
            if (target == null ||
                stationObject == null ||
                !stationObject.TryGetComponent(out CraftingStation station))
                return false;

            if (!IsWithinReach(transform.position, station.transform.position))
                return false;

            Quests?.NotifyInteract(station.StationType.ToString());
            RpcOpenStation(target, stationObject, BuildOffers(station), BuildOrderViews(), _message);
            return true;
        }

        [TargetRpc]
        private void RpcOpenStation(NetworkConnection target, NetworkObject stationObject,
            RecipeOffer[] offers, CraftOrderView[] orders, string message)
        {
            if (!IsOwner || stationObject == null)
                return;

            CraftingStation station = stationObject.GetComponent<CraftingStation>();
            if (station == null)
                return;

            OpenedStation = station;
            _availableRecipes = offers ?? Array.Empty<RecipeOffer>();
            _orderViews = orders ?? Array.Empty<CraftOrderView>();
            _message = message ?? string.Empty;
            OrdersChanged?.Invoke();
            StationOpened?.Invoke(station, _availableRecipes);
        }

        [Server]
        private RecipeOffer[] BuildOffers(CraftingStation station)
        {
            IReadOnlyList<RecipeOffer> offers = CraftingOfferRules.Available(
                station.StationType, CraftingLevel, Inventory != null ? Inventory.Items : null);

            var payload = new RecipeOffer[offers.Count];
            for (int i = 0; i < offers.Count; i++)
                payload[i] = offers[i];

            return payload;
        }

        public void RequestCraft(CraftingStation station, string recipeName)
        {
            if (!IsOwner || station == null || string.IsNullOrEmpty(recipeName))
                return;

            RpcRequestCraft(station.NetworkObject, recipeName);
        }

        [ServerRpc]
        private void RpcRequestCraft(NetworkObject stationObject, string recipeName,
            NetworkConnection sender = null)
        {
            if (sender == null || !OwnerMatches(sender))
                return;

            // A successful craft changes what the bag can afford, so the panel is handed a fresh
            // payload instead of a stale one it would have to guess at.
            if (TryCraft(stationObject, recipeName))
                TrySendOffers(sender, stationObject);
        }

        [Server]
        public bool TryCraft(NetworkObject stationObject, string recipeName)
        {
            if (stationObject == null ||
                !stationObject.TryGetComponent(out CraftingStation station))
                return false;

            RecipeData recipe = RecipeRegistry.Resolve(recipeName);
            if (recipe == null)
            {
                Debug.LogError($"{nameof(PlayerCrafting)}: unknown recipe '{recipeName}'.");
                return false;
            }

            if (!IsWithinReach(transform.position, station.transform.position))
                return false;

            PlayerInventory inventory = Inventory;
            if (inventory == null ||
                !CraftingValidator.CanCraft(inventory.Items, recipe, station.StationType, CraftingLevel))
                return false;

            // A recipe with no duration is the old instant craft, unchanged. A timed recipe takes
            // its escrow now and lets the queue resolve it, so walking away cannot lose materials.
            if (!recipe.IsInstant)
                return TryEnqueue(inventory, recipe);

            if (!inventory.TryCraft(recipe))
                return false;

            AwardCraftingXp(recipe.CraftingXp);
            (_achievements ??= GetComponent<PlayerAchievements>())?.Record(AchievementTrigger.Craft, 1);
            return true;
        }

        /// <summary>
        /// Server: takes the ingredients into escrow and books the order. The escrow and the UTC
        /// deadline are written to the player's save record as soon as this returns, so a transfer,
        /// a disconnect or a restart finds the same order instead of a bag missing its materials.
        /// </summary>
        [Server]
        private bool TryEnqueue(PlayerInventory inventory, RecipeData recipe)
        {
            if (!CraftQueueRules.HasFreeSlot(_orders))
            {
                _message = CraftQueueRules.QueueFullMessage;
                SendOrders();
                return false;
            }

            var taken = new List<ItemInstance>();
            if (!inventory.TryTakeIngredients(recipe, taken))
                return false;

            var escrow = new CraftEscrowItem[taken.Count];
            for (int i = 0; i < taken.Count; i++)
                escrow[i] = CraftEscrowItem.From(taken[i]);

            _orders.Add(CraftOrder.Create(
                recipe.RecipeName,
                CraftQueueRules.DueAtUtcTicks(DateTime.UtcNow, recipe.CraftDurationSeconds),
                escrow));

            _message = string.Empty;
            NotifyOrdersChanged();
            _ = PersistAsync();
            return true;
        }

        [Server]
        public void AwardCraftingXp(int amount)
        {
            _craftingXp.Value = Mathf.Max(0, _craftingXp.Value + Mathf.Max(0, amount));
            CraftingChanged?.Invoke();
        }

        /// <summary>Owner-side intent: claim a completed order's output or a cancelled order's escrow.</summary>
        public void RequestClaimOrder(string orderId)
        {
            if (!IsOwner || string.IsNullOrEmpty(orderId))
                return;

            RpcRequestClaimOrder(orderId);
        }

        [ServerRpc]
        private void RpcRequestClaimOrder(string orderId, NetworkConnection sender = null)
        {
            if (sender == null || !OwnerMatches(sender))
                return;

            _ = ClaimOrderAsync(orderId);
        }

        /// <summary>
        /// Server: resolves one claimable order. A Completed order grants its output and XP (only
        /// when it fits); a Cancelled order returns its escrow. The claim is recorded durably
        /// before the slot is freed, so a reload that finds the order does not grant again.
        /// </summary>
        [Server]
        public async Task<bool> ClaimOrderAsync(string orderId)
        {
            CraftOrder order = CraftQueueRules.Find(_orders, orderId);
            if (!CraftQueueRules.IsClaimable(order))
                return false;

            bool resolved = order.State == CraftOrderState.Completed
                ? TryGrantCompleted(order)
                : TryRefund(order);

            if (!resolved)
            {
                // The bag has no room: the order keeps its slot and stays claimable.
                NotifyOrdersChanged();
                return false;
            }

            await PersistAsync();
            _orders.Remove(order);
            NotifyOrdersChanged();
            return true;
        }

        /// <summary>
        /// Server: the queue's clock. Only due InProgress orders move, and each one persists
        /// Completed before anything is granted. A save that fails leaves the order where it is and
        /// the next tick retries, so a backend hiccup cannot silently grant twice.
        /// </summary>
        private async Task ProcessOrdersAsync()
        {
            _processingOrders = true;
            try
            {
                DateTime now = DateTime.UtcNow;
                bool changed = false;

                // Iterate a snapshot: a claim or cancel can land while a save is in flight, and the
                // live list may have moved on by the time this resumes.
                CraftOrder[] pending = _orders.ToArray();
                for (int i = 0; i < pending.Length; i++)
                {
                    CraftOrder order = pending[i];
                    if (!_orders.Contains(order) || !CraftQueueRules.TryComplete(order, now))
                        continue;

                    changed = true;
                    if (!await PersistAsync())
                        continue;

                    // A claim that resolved the order while the save ran has already claimed it.
                    if (!TryGrantCompleted(order))
                        continue;

                    await PersistAsync();
                    _orders.Remove(order);
                }

                if (changed)
                    NotifyOrdersChanged();
            }
            finally
            {
                _processingOrders = false;
            }
        }

        /// <summary>
        /// Server: grants a Completed order's output and its crafting XP in one step, then marks
        /// it Claimed. False (and no change) when the recipe no longer resolves or the bag is full.
        /// </summary>
        [Server]
        private bool TryGrantCompleted(CraftOrder order)
        {
            if (order == null || order.State != CraftOrderState.Completed)
                return false;

            RecipeData recipe = RecipeRegistry.Resolve(order.RecipeName);
            PlayerInventory inventory = Inventory;
            if (recipe == null || inventory == null || !inventory.TryGrantCraftOutput(recipe))
                return false;

            AwardCraftingXp(recipe.CraftingXp);
            (_achievements ??= GetComponent<PlayerAchievements>())?.Record(AchievementTrigger.Craft, 1);
            CraftQueueRules.MarkClaimed(order);
            return true;
        }

        /// <summary>Server: returns a Cancelled order's escrow exactly as it was taken.</summary>
        [Server]
        private bool TryRefund(CraftOrder order)
        {
            if (order == null || order.State != CraftOrderState.Cancelled)
                return false;

            PlayerInventory inventory = Inventory;
            if (inventory == null || !inventory.TryGrantEscrow(order.Escrow))
                return false;

            CraftQueueRules.MarkClaimed(order);
            return true;
        }

        /// <summary>
        /// Server: copies the queue for a save snapshot. Detached, because the snapshot is written
        /// after this returns while the live queue can still move.
        /// </summary>
        public List<CraftOrder> CaptureOrders()
        {
            var copy = new List<CraftOrder>(_orders.Count);
            for (int i = 0; i < _orders.Count; i++)
                copy.Add(_orders[i].Clone());

            return copy;
        }

        /// <summary>
        /// Server: rebuilds crafting XP and the queue from a loaded save. Claimed entries are
        /// pruned rather than restored, so a reload can never re-grant one.
        /// </summary>
        [Server]
        public void RestoreFromSave(int craftingXp, IReadOnlyList<CraftOrder> orders)
        {
            _craftingXp.Value = Mathf.Max(0, craftingXp);
            _orders.Clear();

            if (orders != null)
            {
                for (int i = 0; i < orders.Count; i++)
                {
                    if (orders[i] != null)
                        _orders.Add(orders[i].Clone());
                }
            }

            CraftQueueRules.PruneClaimed(_orders);
        }

        [Server]
        private void NotifyOrdersChanged()
        {
            CraftingChanged?.Invoke();
            OrdersChanged?.Invoke();
            SendOrders();
        }

        [Server]
        private void SendOrders()
        {
            if (!IsServer || Owner == null)
                return;

            RpcOrders(Owner, BuildOrderViews(), _message);
        }

        [TargetRpc]
        private void RpcOrders(NetworkConnection target, CraftOrderView[] orders, string message)
        {
            if (!IsOwner)
                return;

            _orderViews = orders ?? Array.Empty<CraftOrderView>();
            _message = message ?? string.Empty;
            OrdersChanged?.Invoke();
        }

        [Server]
        private CraftOrderView[] BuildOrderViews()
        {
            DateTime now = DateTime.UtcNow;
            var views = new CraftOrderView[_orders.Count];
            for (int i = 0; i < _orders.Count; i++)
            {
                CraftOrder order = _orders[i];
                RecipeData recipe = RecipeRegistry.Resolve(order.RecipeName);
                views[i] = new CraftOrderView
                {
                    OrderId = order.OrderId,
                    RecipeName = order.RecipeName,
                    State = order.State,
                    RemainingSeconds = (float)CraftQueueRules.RemainingSeconds(order, now),
                    OutputCount = recipe != null ? recipe.OutputCount : 0,
                    OutputItemName = recipe != null && recipe.Output != null
                        ? recipe.Output.ItemName
                        : string.Empty,
                };
            }

            return views;
        }

        private async Task<bool> PersistAsync()
        {
            PlayerSaveTrigger trigger = SaveTrigger;
            PlayerPersistenceService service = PlayerPersistenceService.Instance;
            if (trigger == null || service == null || string.IsNullOrEmpty(trigger.AccountId))
                return true;

            return await service.FlushAsync(trigger.AccountId);
        }

        private PlayerSaveTrigger SaveTrigger
            => _saveTrigger != null ? _saveTrigger : _saveTrigger = GetComponent<PlayerSaveTrigger>();

        /// <summary>Server tick: completes due orders and keeps the owner's countdown fresh.</summary>
        private void Update()
        {
            if (!IsServer || _orders.Count == 0)
                return;

            if (!_processingOrders)
                _ = ProcessOrdersAsync();

            if (Time.time >= _nextOrderPush)
            {
                _nextOrderPush = Time.time + OrderPushIntervalSeconds;
                SendOrders();
            }
        }

        public static PlayerCrafting FindLocal(FishNet.Managing.NetworkManager networkManager)
            => PlayerCombat.FindLocal(networkManager) is { } combat
                ? combat.GetComponent<PlayerCrafting>()
                : null;

        private void OnCraftingXpChanged(int previous, int current, bool asServer)
        {
            if (asServer)
                return;

            // A level change can only widen what a station offers, but the panel is looking at
            // the last payload; repainting at least keeps its level readout honest.
            RecipesChanged?.Invoke();
        }

        public static class CraftingXpTableImpl
        {
            public static int XpForLevel(int level)
            {
                int l = Mathf.Max(1, level);
                return 100 * (l - 1) * (l - 1);
            }

            public static int LevelForXp(int xp)
            {
                int x = Mathf.Max(0, xp);
                return Mathf.FloorToInt(Mathf.Sqrt(x / 100f)) + 1;
            }
        }
    }
}
