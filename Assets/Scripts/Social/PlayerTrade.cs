using System;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using MMO.Inventory;
using MMO.Persistence;
using UnityEngine;

namespace MMO.Social
{
    /// <summary>The serializable snapshot one player's client paints in the trade window.</summary>
    [Serializable]
    public sealed class TradeView
    {
        public bool Active;
        public string PartnerName;
        public TradeState State;
        public bool MyLocked;
        public bool TheirLocked;
        public List<ItemInstance> MyOffer = new();
        public List<ItemInstance> TheirOffer = new();
    }

    /// <summary>
    /// A direct player-to-player trade. Clients submit intent only; the server owns the pure
    /// <see cref="TradeSession"/> and moves both sides in one tick once both locks land.
    ///
    /// <para>
    /// There is no escrow: items stay in their bags until execution, and any inventory change while
    /// a session is open cancels it. That removes the crash-rollback problem entirely and keeps the
    /// save trigger from persisting a half-trade.
    /// </para>
    /// </summary>
    public sealed class PlayerTrade : NetworkBehaviour
    {
        /// <summary>How close two players must be to open a trade, in metres.</summary>
        public const float Range = 5f;

        private static readonly TradeView EmptyView = new();

        private readonly SyncVar<string> _viewJson = new();
        private readonly InventoryGrid _grid =
            new(PlayerInventory.GridWidth, PlayerInventory.GridHeight, PlayerInventory.MountExtraSlots);

        private TradeSession _session;
        private PlayerTrade _partner;
        private PlayerInventory _inventory;
        private string _accountId;
        private bool _executing;

        /// <summary>Raised on the owning client whenever the replicated trade view changes.</summary>
        public event Action ViewChanged;

        /// <summary>Raised on the acting client when the server refuses a trade intent.</summary>
        public event Action<TradeRule> RuleFailed;

        /// <summary>The account id resolved for this player's connection, or null before server start.</summary>
        public string AccountId => _accountId;

        /// <summary>The current replicated view, or an empty one before a trade opens.</summary>
        public TradeView View
            => string.IsNullOrEmpty(_viewJson.Value)
                ? EmptyView
                : JsonUtility.FromJson<TradeView>(_viewJson.Value);

        /// <summary>Server-side: whether this player is currently in an open trade.</summary>
        public bool IsTrading => _session != null && !_session.IsFinished;

        public override void OnStartNetwork()
        {
            _viewJson.OnChange += OnViewChanged;

            if (!IsServer)
                return;

            _accountId = ResolveAccountId();
            _inventory = GetComponent<PlayerInventory>();
        }

        public override void OnStopNetwork()
        {
            _viewJson.OnChange -= OnViewChanged;

            if (IsServer)
                CancelTrade(publishSelf: false);
        }

        #region Client intent

        /// <summary>Opens a trade with <paramref name="target"/>. The server re-validates range.</summary>
        public void RequestTrade(NetworkObject target)
        {
            if (!OwnsSelf() || target == null)
                return;

            RpcRequestTrade(target);
        }

        /// <summary>
        /// Replaces this player's offer with the items under the supplied bag cells. Cells are
        /// encoded as <c>gridY * PlayerInventory.GridWidth + gridX</c>.
        /// </summary>
        public void RequestOffer(int[] bagCells)
        {
            if (!OwnsSelf() || bagCells == null)
                return;

            RpcRequestOffer(bagCells);
        }

        public void RequestLock()
        {
            if (OwnsSelf())
                RpcLock();
        }

        public void RequestCancel()
        {
            if (OwnsSelf())
                RpcCancel();
        }

        [ServerRpc]
        private void RpcRequestTrade(NetworkObject target, NetworkConnection sender = null)
        {
            if (!ValidSender(sender))
                return;

            OpenTrade(target);
        }

        [ServerRpc]
        private void RpcRequestOffer(int[] bagCells, NetworkConnection sender = null)
        {
            if (!ValidSender(sender) || _session == null || _inventory == null || _session.IsFinished)
                return;

            IReadOnlyList<ItemInstance> offer = ResolveOffer(bagCells);
            if (offer == null && bagCells.Length > 0)
            {
                TargetNotify(Owner, (int)TradeRule.NoOffer);
                return;
            }

            TradeRule rule = _session.SetOffer(_accountId, offer);
            if (rule != TradeRule.Ok)
            {
                TargetNotify(Owner, (int)rule);
                return;
            }

            PublishAll();
        }
        [ServerRpc]
        private void RpcLock(NetworkConnection sender = null)
        {
            if (!ValidSender(sender) || _session == null || _inventory == null || _session.IsFinished)
                return;

            if (!TradeRules.OffersStillHeld(_session.OfferOf(_accountId), _inventory.Items))
            {
                CancelTrade();
                TargetNotify(Owner, (int)TradeRule.NoOffer);
                return;
            }

            TradeRule rule = _session.Lock(_accountId);
            if (rule != TradeRule.Ok)
            {
                TargetNotify(Owner, (int)rule);
                return;
            }

            PublishAll();

            if (_session.State == TradeState.BothLocked)
                Settle();
        }

        [ServerRpc]
        private void RpcCancel(NetworkConnection sender = null)
        {
            if (!ValidSender(sender))
                return;

            CancelTrade();
        }

        #endregion

        #region Server trade lifecycle

        private void OpenTrade(NetworkObject target)
        {
            if (_inventory == null || string.IsNullOrEmpty(_accountId))
                return;

            if (target == null || !target.IsSpawned || target == NetworkObject)
                return;

            PlayerTrade partner = target.GetComponent<PlayerTrade>();
            if (partner == null || partner == this || partner._inventory == null ||
                string.IsNullOrEmpty(partner._accountId))
                return;

            if (IsTrading || partner.IsTrading)
            {
                TargetNotify(Owner, (int)TradeRule.Busy);
                return;
            }

            if (Vector3.Distance(transform.position, target.transform.position) > Range)
            {
                TargetNotify(Owner, (int)TradeRule.OutOfRange);
                return;
            }

            _session = new TradeSession(_accountId, partner._accountId);
            _partner = partner;
            partner._partner = this;
            partner._session = _session;

            SubscribeInventory(_inventory);
            partner.SubscribeInventory(partner._inventory);

            PublishAll();
        }

        private IReadOnlyList<ItemInstance> ResolveOffer(int[] bagCells)
        {
            var offer = new List<ItemInstance>();
            var seenCells = new HashSet<int>();
            var seenItems = new HashSet<string>();

            for (int i = 0; i < bagCells.Length; i++)
            {
                int cell = bagCells[i];
                int x = cell % PlayerInventory.GridWidth;
                int y = cell / PlayerInventory.GridWidth;

                if (cell < 0 || !seenCells.Add(cell))
                    return null;

                int itemIndex = _grid.FindAt(_inventory.Items, x, y);
                if (itemIndex < 0)
                    return null;

                ItemInstance item = _inventory.Items[itemIndex];
                if (item.IsEmpty || !seenItems.Add(item.InstanceId))
                    return null;

                offer.Add(item);
            }

            return offer;
        }

        private void Settle()
        {
            if (_session == null || _partner == null || _partner._inventory == null)
            {
                CancelTrade();
                return;
            }

            if (!TradeRules.OffersStillHeld(_session.OfferOf(_accountId), _inventory.Items) ||
                !TradeRules.OffersStillHeld(
                    _session.OfferOf(_partner._accountId), _partner._inventory.Items) ||
                _session.TryBuildExchange(out TradeExchange exchange) != TradeRule.Ok)
            {
                CancelTrade();
                TargetNotify(Owner, (int)TradeRule.NoOffer);
                return;
            }

            _executing = true;

            var removedFromA = new List<ItemInstance>();
            var removedFromB = new List<ItemInstance>();
            bool ok = TryRemoveAll(_inventory, exchange.ToB, removedFromA) &&
                      TryRemoveAll(_partner._inventory, exchange.ToA, removedFromB);

            var addedToA = new List<ItemInstance>();
            var addedToB = new List<ItemInstance>();
            if (ok)
            {
                ok = TryAddAll(_partner._inventory, removedFromA, addedToB) &&
                     TryAddAll(_inventory, removedFromB, addedToA);
            }

            if (!ok)
            {
                RemoveAll(_partner._inventory, addedToB);
                RemoveAll(_inventory, addedToA);
                Return(_inventory, removedFromA);
                Return(_partner._inventory, removedFromB);

                _executing = false;
                CancelTrade();
                TargetNotify(Owner, (int)TradeRule.BagFull);
                return;
            }

            _executing = false;
            _session.Complete();

            PlayerTrade partner = _partner;
            PublishAll();
            Unlink(partner);
            PublishView();
            partner?.PublishView();
        }

        private static bool TryRemoveAll(
            PlayerInventory bag, IReadOnlyList<ItemInstance> items, List<ItemInstance> removed)
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (!bag.TryRemoveItem(items[i].InstanceId, out ItemInstance item))
                    return false;

                removed.Add(item);
            }

            return true;
        }

        private static bool TryAddAll(
            PlayerInventory bag, IReadOnlyList<ItemInstance> items, List<ItemInstance> added)
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (!bag.TryAddExistingItem(items[i]))
                    return false;

                added.Add(items[i]);
            }

            return true;
        }

        private static void Return(PlayerInventory bag, IReadOnlyList<ItemInstance> items)
        {
            for (int i = 0; i < items.Count; i++)
                bag.TryAddExistingItem(items[i]);
        }

        private static void RemoveAll(PlayerInventory bag, IReadOnlyList<ItemInstance> items)
        {
            for (int i = 0; i < items.Count; i++)
                bag.TryRemoveItem(items[i].InstanceId, out _);
        }

        private void CancelTrade(bool publishSelf = true)
        {
            if (_session == null)
                return;

            _session.Cancel();
            PlayerTrade partner = _partner;
            Unlink(partner);

            if (publishSelf)
                PublishView();

            partner?.PublishView();
        }

        private void Unlink(PlayerTrade partner)
        {
            UnsubscribeInventory(_inventory);
            _session = null;
            _partner = null;

            if (partner == null)
                return;

            partner.UnsubscribeInventory(partner._inventory);
            partner._session = null;
            partner._partner = null;
        }

        #endregion

        #region Inventory guard and delivery

        private void SubscribeInventory(PlayerInventory bag)
        {
            if (bag != null)
                bag.ContainersChanged += OnInventoryChanged;
        }

        private void UnsubscribeInventory(PlayerInventory bag)
        {
            if (bag != null)
                bag.ContainersChanged -= OnInventoryChanged;
        }

        private void OnInventoryChanged()
        {
            if (!IsServer || _session == null || _executing)
                return;

            CancelTrade();
        }

        private void PublishAll()
        {
            PublishView();
            _partner?.PublishView();
        }

        private void PublishView()
        {
            var view = new TradeView();
            if (_session != null)
            {
                view.Active = !_session.IsFinished;
                view.PartnerName = _partner != null ? _partner._accountId : null;
                view.State = _session.State;
                view.MyLocked = _session.IsLocked(_accountId);
                view.TheirLocked = _partner != null && _session.IsLocked(_partner._accountId);

                if (!_session.IsFinished)
                {
                    view.MyOffer.AddRange(_session.OfferOf(_accountId));
                    if (_partner != null)
                        view.TheirOffer.AddRange(_session.OfferOf(_partner._accountId));
                }
            }

            string json = JsonUtility.ToJson(view);
            if (_viewJson.Value != json)
                _viewJson.Value = json;
        }

        private void OnViewChanged(string previous, string current, bool asServer)
            => ViewChanged?.Invoke();

        [TargetRpc]
        private void TargetNotify(NetworkConnection conn, int rule)
            => RuleFailed?.Invoke((TradeRule)rule);

        #endregion

        private bool OwnsSelf()
        {
            NetworkObject self = NetworkObject;
            return self != null && self.IsOwner;
        }

        private bool ValidSender(NetworkConnection sender)
            => sender != null && OwnerMatches(sender);

        private string ResolveAccountId()
        {
            PlayerPersistenceService service = PlayerPersistenceService.Instance;
            int clientId = Owner != null ? Owner.ClientId : -1;
            if (service != null && service.TryGetJoin(clientId, out string accountId, out _))
                return accountId;

            return null;
        }
    }
}
