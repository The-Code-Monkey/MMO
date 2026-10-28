using System;
using System.Collections.Generic;
using MMO.Inventory;
using MMO.Social;
using UnityEngine;
using UnityEngine.UI;

namespace MMO.UI
{
    /// <summary>
    /// The direct-trade window: one grid per side's offer, the player's bag (right-click to add or
    /// remove an offer cell), and Lock / Cancel. The view owns no rules; it forwards intents to
    /// <see cref="PlayerTrade"/> and paints the replicated <see cref="TradeView"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TradeFrameView : MonoBehaviour
    {
        /// <summary>The bag read through the trade window: a right-click toggles one offer cell.</summary>
        public sealed class TradeBagSource : IInventorySource
        {
            private readonly IInventorySource _bag;

            public TradeBagSource(IInventorySource bag)
            {
                _bag = bag;
                if (_bag != null)
                    _bag.ContainersChanged += NotifyChanged;
            }

            public Action<int, int> OfferRequested;

            public int GridWidth => _bag != null ? _bag.GridWidth : 1;
            public int GridHeight => _bag != null ? _bag.GridHeight : 1;

            public ItemInstance GetItemAt(int gridX, int gridY)
                => _bag != null ? _bag.GetItemAt(gridX, gridY) : ItemInstance.Empty;

            public void RequestEquip(int gridX, int gridY)
                => OfferRequested?.Invoke(gridX, gridY);

            public event Action ContainersChanged;

            public void Unsubscribe()
            {
                if (_bag != null)
                    _bag.ContainersChanged -= NotifyChanged;
            }

            private void NotifyChanged() => ContainersChanged?.Invoke();
        }

        [SerializeField] private KeyCode _openKey = KeyCode.T;

        /// <summary>The key the owning player HUD uses to target the nearest trade partner.</summary>
        public KeyCode OpenKey => _openKey;

        private readonly TradeOfferSource _myOffer = new();
        private readonly TradeOfferSource _theirOffer = new();
        private readonly List<int> _offeredCells = new();

        private InventoryGridView _myGrid;
        private InventoryGridView _theirGrid;
        private InventoryGridView _bagGrid;
        private Text _partnerLabel;
        private Text _stateLabel;

        private PlayerTrade _trade;
        private PlayerInventory _bag;
        private TradeBagSource _bagSource;

        public bool IsOpen => _trade != null && _trade.View.Active;

        public TradeOfferSource MyOfferSource => _myOffer;

        public TradeOfferSource TheirOfferSource => _theirOffer;

        /// <summary>Declares the window's sub-views; called by the factory.</summary>
        public void SetHosts(
            InventoryGridView myGrid,
            InventoryGridView theirGrid,
            InventoryGridView bagGrid,
            Text partnerLabel,
            Text stateLabel)
        {
            _myGrid = myGrid;
            _theirGrid = theirGrid;
            _bagGrid = bagGrid;
            _partnerLabel = partnerLabel;
            _stateLabel = stateLabel;
        }

        /// <summary>Binds the window to the local player's trade and bag. Null conceals it.</summary>
        public void Reveal(PlayerTrade trade, PlayerInventory bag)
        {
            Conceal();

            _trade = trade;
            _bag = bag;
            if (_trade == null)
                return;

            _trade.ViewChanged += OnViewChanged;

            _myOffer.RemoveRequested += OnRemoveRequested;

            if (_bag != null)
            {
                _bagSource = new TradeBagSource(_bag);
                _bagSource.OfferRequested += OnBagCellClicked;
            }

            if (_myGrid != null)
                _myGrid.Bind(_myOffer);
            if (_theirGrid != null)
                _theirGrid.Bind(_theirOffer);
            if (_bagGrid != null)
                _bagGrid.Bind(_bagSource);

            ApplyView();
        }

        /// <summary>Detaches every subscription and hides the window.</summary>
        public void Conceal()
        {
            SetVisible(false);

            if (_trade != null)
                _trade.ViewChanged -= OnViewChanged;
            _trade = null;
            _bag = null;

            _myOffer.RemoveRequested -= OnRemoveRequested;
            _myOffer.SetOffer(null);
            _theirOffer.SetOffer(null);
            _offeredCells.Clear();

            if (_bagSource != null)
            {
                _bagSource.OfferRequested -= OnBagCellClicked;
                _bagSource.Unsubscribe();
                _bagSource = null;
            }

            if (_myGrid != null)
                _myGrid.Unbind();
            if (_theirGrid != null)
                _theirGrid.Unbind();
            if (_bagGrid != null)
                _bagGrid.Unbind();

            if (_partnerLabel != null)
                _partnerLabel.text = string.Empty;
            if (_stateLabel != null)
                _stateLabel.text = string.Empty;
        }

        public void SetVisible(bool visible) => gameObject.SetActive(visible);

        public void Lock() => _trade?.RequestLock();

        public void CancelTrade() => _trade?.RequestCancel();

        /// <summary>Opens a trade with a specific player (the keybind uses the nearest one).</summary>
        public void OpenWith(PlayerTrade partner)
        {
            if (_trade == null || partner == null || partner == _trade)
                return;

            _trade.RequestTrade(partner.NetworkObject);
        }

        /// <summary>
        /// Opens a trade with the nearest player in range. Called by the owning player HUD because
        /// this window deactivates itself while closed, so its own Update would not run.
        /// </summary>
        public void OpenNearest()
        {
            if (_trade == null || _trade.View.Active)
                return;

            PlayerTrade nearest = FindNearestPartner();
            if (nearest != null)
                OpenWith(nearest);
        }

        private PlayerTrade FindNearestPartner()
        {
            PlayerTrade[] candidates = FindObjectsByType<PlayerTrade>(FindObjectsSortMode.None);
            PlayerTrade nearest = null;
            float best = TradeRangeSquared(_trade.transform.position);

            for (int i = 0; i < candidates.Length; i++)
            {
                PlayerTrade candidate = candidates[i];
                if (candidate == null || candidate == _trade || candidate.IsTrading)
                    continue;

                float distance = (candidate.transform.position - _trade.transform.position).sqrMagnitude;
                if (distance > best)
                    continue;

                best = distance;
                nearest = candidate;
            }

            return nearest;
        }

        private static float TradeRangeSquared(Vector3 position) => PlayerTrade.Range * PlayerTrade.Range;

        private void OnViewChanged() => ApplyView();

        private void ApplyView()
        {
            TradeView view = _trade != null ? _trade.View : null;
            if (view == null)
                return;

            if (view.Active)
                RebuildOfferCells(view.MyOffer);
            else
                _offeredCells.Clear();

            _myOffer.SetOffer(view.MyOffer);
            _theirOffer.SetOffer(view.TheirOffer);

            if (_partnerLabel != null)
                _partnerLabel.text = view.Active ? view.PartnerName : string.Empty;
            if (_stateLabel != null)
                _stateLabel.text = StateText(view);

            SetVisible(view.Active);
        }

        private static string StateText(TradeView view)
        {
            if (!view.Active)
                return string.Empty;

            if (view.MyLocked && view.TheirLocked)
                return "Trading...";
            if (view.MyLocked)
                return "Waiting for partner";
            if (view.TheirLocked)
                return "Partner locked";

            return "Open";
        }

        private void RebuildOfferCells(IReadOnlyList<ItemInstance> offer)
        {
            _offeredCells.Clear();
            for (int i = 0; i < offer.Count; i++)
                _offeredCells.Add(offer[i].GridY * PlayerInventory.GridWidth + offer[i].GridX);
        }

        private void OnBagCellClicked(int gridX, int gridY)
        {
            if (_trade == null || !_trade.View.Active)
                return;

            int cell = gridY * PlayerInventory.GridWidth + gridX;
            ItemInstance offered = _myOffer.GetItemAt(gridX, gridY);
            if (!offered.IsEmpty)
            {
                int origin = offered.GridY * PlayerInventory.GridWidth + offered.GridX;
                _offeredCells.Remove(origin);
            }
            else if (!_offeredCells.Remove(cell))
            {
                _offeredCells.Add(cell);
            }

            _trade.RequestOffer(_offeredCells.ToArray());
        }

        private void OnRemoveRequested(int gridX, int gridY)
            => OnBagCellClicked(gridX, gridY);

        private void OnDestroy() => Conceal();
    }
}
