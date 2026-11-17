using System;
using System.Collections.Generic;
using MMO.Inventory;
using MMO.Social;
using UnityEngine;
using UnityEngine.UI;

namespace MMO.UI
{
    /// <summary>
    /// The guild bank window: a tab strip over the replicated bank, a grid that right-clicks out
    /// as a withdraw intent, a second grid over the local player's bag that right-clicks out as a
    /// deposit intent, and the <see cref="GuildRankPermissionEditor"/> beneath.
    ///
    /// <para>
    /// Like every screen in this namespace the view owns no rules and no RPCs. Selecting a tab
    /// repaints the source grid, a right-click forwards coordinates to
    /// <c>PlayerGuild.RequestWithdraw</c>/<c>RequestDeposit</c>, and the server's answer — a
    /// replicated bank or a <c>BankRuleFailed</c> refusal — is the only thing that ever changes
    /// what is on screen. Binding is explicit: <see cref="Reveal"/> takes the two networked pieces
    /// it needs and <see cref="Conceal"/> detaches everything, so a despawned player leaves no
    /// subscriptions behind.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GuildBankFrameView : MonoBehaviour
    {
        /// <summary>
        /// The player's bag read through the bank frame: identical geometry and change events to
        /// the real inventory, but a right-click here means "deposit into the open tab" instead of
        /// "equip". Pure and self-contained so the frame can be EditMode-tested with a fake bag.
        /// </summary>
        public sealed class DepositBagSource : IInventorySource
        {
            private readonly IInventorySource _bag;
            private readonly Func<int> _tabIndex;

            /// <param name="bag">The bag being read, e.g. the local player's <c>PlayerInventory</c>.</param>
            /// <param name="tabIndex">The frame's currently selected tab, resolved at click time.</param>
            public DepositBagSource(IInventorySource bag, Func<int> tabIndex)
            {
                _bag = bag;
                _tabIndex = tabIndex;

                if (_bag != null)
                    _bag.ContainersChanged += NotifyChanged;
            }

            /// <summary>A right-click on a bag cell: (tabIndex, bagX, bagY), for the server.</summary>
            public Action<int, int, int> DepositRequested;

            public int GridWidth => _bag != null ? _bag.GridWidth : 1;
            public int GridHeight => _bag != null ? _bag.GridHeight : 1;

            public ItemInstance GetItemAt(int gridX, int gridY)
                => _bag != null ? _bag.GetItemAt(gridX, gridY) : ItemInstance.Empty;

            public void RequestEquip(int gridX, int gridY)
                => DepositRequested?.Invoke(_tabIndex(), gridX, gridY);

            public event Action ContainersChanged;

            /// <summary>Stops forwarding the bag's change events; called when the frame detaches.</summary>
            public void Unsubscribe()
            {
                if (_bag != null)
                    _bag.ContainersChanged -= NotifyChanged;
            }

            private void NotifyChanged() => ContainersChanged?.Invoke();
        }

        private const float TabButtonWidth = 96f;
        private const float TabButtonHeight = 26f;

        private readonly List<Button> _tabButtons = new List<Button>();

        private RectTransform _tabStrip;
        private InventoryGridView _bankGrid;
        private InventoryGridView _bagGrid;
        private GuildRankPermissionEditor _editor;
        private Text _revenueLabel;

        private readonly GuildBankTabSource _bankSource = new GuildBankTabSource();
        private DepositBagSource _bagSource;

        private PlayerGuild _guild;

        /// <summary>The tab grid's read-only source, exposed for tests and the factory.</summary>
        public GuildBankTabSource BankSource => _bankSource;

        /// <summary>The bag grid's deposit source while the frame is open, or null.</summary>
        public DepositBagSource BagSource => _bagSource;

        /// <summary>Whether the frame is currently open.</summary>
        public bool IsOpen => _guild != null && gameObject.activeSelf;

        /// <summary>Declares where the frame's sub-screens live; public so the factory can wire them.</summary>
        public void SetHosts(
            RectTransform tabStrip,
            InventoryGridView bankGrid,
            InventoryGridView bagGrid,
            GuildRankPermissionEditor editor)
        {
            _tabStrip = tabStrip;
            _bankGrid = bankGrid;
            _bagGrid = bagGrid;
            _editor = editor;
        }

        /// <summary>
        /// The read-only revenue label the factory built, or null. It shows the durable
        /// <see cref="GuildBankRecord.TerritoryRevenue"/> from the replicated bank record; the client
        /// never computes a tax.
        /// </summary>
        public void SetRevenueLabel(Text label) => _revenueLabel = label;

        /// <summary>
        /// Opens the frame for a player: binds the tab strip, both grids and the editor, and shows
        /// the window. Either argument may be null (a bag is optional) — passing null is a conceal.
        /// </summary>
        public void Reveal(PlayerGuild guild, PlayerInventory bag)
        {
            Conceal();

            _guild = guild;
            if (guild == null)
                return;

            guild.BankChanged += OnBankChanged;
            _bankSource.WithdrawRequested += OnWithdrawRequested;

            if (bag != null)
            {
                _bagSource = new DepositBagSource(bag, () => _bankSource.TabIndex);
                _bagSource.DepositRequested += OnDepositRequested;
            }

            if (_bankGrid != null)
                _bankGrid.Bind(_bankSource);
            if (_bagGrid != null)
                _bagGrid.Bind(_bagSource);
            if (_editor != null)
                _editor.Open(guild);

            _bankSource.SetBank(guild.Bank);
            RefreshTabStrip();
            RefreshRevenue();
            SetVisible(true);
        }

        /// <summary>Detaches every subscription, hides the window and clears its tab buttons.</summary>
        public void Conceal()
        {
            SetVisible(false);

            if (_guild != null)
                _guild.BankChanged -= OnBankChanged;
            _guild = null;

            _bankSource.WithdrawRequested -= OnWithdrawRequested;
            _bankSource.SetBank(null);

            if (_bagSource != null)
            {
                _bagSource.DepositRequested -= OnDepositRequested;
                _bagSource.Unsubscribe();
                _bagSource = null;
            }

            if (_bankGrid != null)
                _bankGrid.Unbind();
            if (_bagGrid != null)
                _bagGrid.Unbind();
            if (_editor != null)
                _editor.Close();

            ClearTabButtons();
        }

        /// <summary>Shows or hides the window without touching its bindings.</summary>
        public void SetVisible(bool visible) => gameObject.SetActive(visible);

        /// <summary>Switches the open tab, driving the source grid to repaint.</summary>
        public void SelectTab(int index)
        {
            _bankSource.SelectTab(index);
            RefreshTabButtons();
        }

        private void OnBankChanged()
        {
            _bankSource.SetBank(_guild?.Bank);
            RefreshTabStrip();
            RefreshRevenue();
        }

        private void RefreshRevenue()
        {
            if (_revenueLabel == null)
                return;

            GuildBankRecord bank = _guild?.Bank;
            _revenueLabel.text = bank != null
                ? $"Territory revenue: {bank.TerritoryRevenue:N0} gold"
                : string.Empty;
        }

        private void OnWithdrawRequested(int tabIndex, int gridX, int gridY)
            => _guild?.RequestWithdraw(tabIndex, gridX, gridY);

        private void OnDepositRequested(int tabIndex, int bagX, int bagY)
            => _guild?.RequestDeposit(tabIndex, bagX, bagY);

        private void RefreshTabStrip()
        {
            ClearTabButtons();

            if (_tabStrip == null)
                return;

            IReadOnlyList<GuildBankTab> tabs = _bankSource.Tabs;
            for (int i = 0; i < tabs.Count; i++)
                CreateTabButton(tabs[i], i);

            RefreshTabButtons();
        }

        private void CreateTabButton(GuildBankTab tab, int position)
        {
            var buttonObject = new GameObject($"Tab {tab.Index}", typeof(RectTransform), typeof(Image));
            RectTransform rect = (RectTransform)buttonObject.transform;
            rect.SetParent(_tabStrip, false);
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(position * (TabButtonWidth + 6f), 0f);
            rect.sizeDelta = new Vector2(TabButtonWidth, TabButtonHeight);

            Image background = buttonObject.GetComponent<Image>();
            background.color = new Color(0.3f, 0.3f, 0.3f, 0.9f);

            Button button = buttonObject.AddComponent<Button>();
            button.targetGraphic = background;

            var textObject = new GameObject("Text", typeof(RectTransform), typeof(Text));
            RectTransform textRect = (RectTransform)textObject.transform;
            textRect.SetParent(rect, false);
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            Text label = textObject.GetComponent<Text>();
            label.font = UIFont.Default;
            label.fontSize = 12;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.white;
            label.raycastTarget = false;
            label.text = tab.Name;

            int tabIndex = tab.Index;
            button.onClick.AddListener(() => SelectTab(tabIndex));
            _tabButtons.Add(button);
        }

        private void RefreshTabButtons()
        {
            int selected = _bankSource.TabIndex;
            IReadOnlyList<GuildBankTab> tabs = _bankSource.Tabs;

            for (int i = 0; i < _tabButtons.Count; i++)
            {
                Button button = _tabButtons[i];
                if (button == null)
                    continue;

                bool isSelected = i < tabs.Count && tabs[i].Index == selected;
                Image background = button.GetComponent<Image>();
                if (background != null)
                {
                    background.color = isSelected
                        ? new Color(0.5f, 0.45f, 0.2f, 0.95f)
                        : new Color(0.3f, 0.3f, 0.3f, 0.9f);
                }
            }
        }

        private void ClearTabButtons()
        {
            for (int i = 0; i < _tabButtons.Count; i++)
            {
                if (_tabButtons[i] != null)
                    ViewObject.Destroy(_tabButtons[i].gameObject);
            }

            _tabButtons.Clear();
        }

        private void OnDestroy() => Conceal();
    }
}