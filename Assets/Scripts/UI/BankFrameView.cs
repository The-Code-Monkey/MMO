using System;
using System.Collections.Generic;
using MMO.Economy;
using MMO.Inventory;
using MMO.Social;
using UnityEngine;
using UnityEngine.UI;

namespace MMO.UI
{
    /// <summary>
    /// The generic bank window: the bank's item grid and stored-currency strip on the left, the
    /// player's backpack on the right. A right-click on the bank withdraws; a right-click on the
    /// backpack deposits. The same view serves a personal bank and a guild bank — the bound
    /// <see cref="IBankSource"/> decides which.
    ///
    /// <para>
    /// The view owns no rules and no RPCs. It forwards intents to the source and repaints from the
    /// source's single change event. Tabs and the rank-permission editor are optional: the personal
    /// bank hides both, the guild bank shows them.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BankFrameView : MonoBehaviour
    {
        /// <summary>
        /// The backpack read through the bank window: identical geometry and change events to the
        /// real bag, but a right-click means "deposit this cell".
        /// </summary>
        public sealed class DepositBagSource : IInventorySource
        {
            private readonly IInventorySource _bag;
            private readonly IBankSource _bank;

            public DepositBagSource(IInventorySource bag, IBankSource bank)
            {
                _bag = bag;
                _bank = bank;

                if (_bag != null)
                    _bag.ContainersChanged += NotifyChanged;
            }

            public int GridWidth => _bag != null ? _bag.GridWidth : 1;
            public int GridHeight => _bag != null ? _bag.GridHeight : 1;

            public ItemInstance GetItemAt(int gridX, int gridY)
                => _bag != null ? _bag.GetItemAt(gridX, gridY) : ItemInstance.Empty;

            public void RequestEquip(int gridX, int gridY)
                => _bank?.RequestDeposit(gridX, gridY);

            public event Action ContainersChanged;

            public void Unsubscribe()
            {
                if (_bag != null)
                    _bag.ContainersChanged -= NotifyChanged;
            }

            private void NotifyChanged() => ContainersChanged?.Invoke();
        }

        private const float TabButtonWidth = 96f;
        private const float TabButtonHeight = 24f;

        private readonly List<Button> _tabButtons = new List<Button>();

        private Text _title;
        private InventoryGridView _bankGrid;
        private InventoryGridView _bagGrid;
        private Text _currency;
        private RectTransform _tabStrip;
        private GuildRankPermissionEditor _editor;

        private IBankSource _bank;
        private IBankTabSource _tabs;
        private DepositBagSource _bagSource;

        /// <summary>Whether the window is open.</summary>
        public bool IsOpen => _bank != null && gameObject.activeSelf;

        /// <summary>Declares the window's sub-views; called by the factory.</summary>
        public void SetHosts(
            Text title,
            InventoryGridView bankGrid,
            InventoryGridView bagGrid,
            Text currency,
            RectTransform tabStrip,
            GuildRankPermissionEditor editor)
        {
            _title = title;
            _bankGrid = bankGrid;
            _bagGrid = bagGrid;
            _currency = currency;
            _tabStrip = tabStrip;
            _editor = editor;
        }

        /// <summary>
        /// Opens the window for a bank source and the local backpack. A null source conceals it.
        /// <paramref name="guild"/> is only passed for a guild bank, which adds the rank editor.
        /// </summary>
        public void Reveal(IBankSource bank, PlayerInventory bag, PlayerGuild guild = null)
        {
            Conceal();

            _bank = bank;
            if (bank == null)
                return;

            bank.ContainersChanged += OnBankChanged;

            if (bank is IBankTabSource tabs)
            {
                _tabs = tabs;
                _tabs.TabsChanged += OnTabsChanged;
            }

            _bagSource = new DepositBagSource(bag, bank);

            if (_bankGrid != null)
                _bankGrid.Bind(bank);
            if (_bagGrid != null)
                _bagGrid.Bind(_bagSource);

            if (_editor != null)
            {
                bool showEditor = guild != null;
                _editor.gameObject.SetActive(showEditor);
                if (showEditor)
                    _editor.Open(guild);
                else
                    _editor.Close();
            }

            if (_title != null)
                _title.text = bank.Title;

            RefreshTabs();
            RefreshCurrency();
            SetVisible(true);
        }

        /// <summary>Detaches every subscription and hides the window.</summary>
        public void Conceal()
        {
            SetVisible(false);

            if (_bank != null)
            {
                _bank.ContainersChanged -= OnBankChanged;
                _bank.Unsubscribe();
            }

            _bank = null;

            if (_tabs != null)
                _tabs.TabsChanged -= OnTabsChanged;
            _tabs = null;

            if (_bagSource != null)
            {
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
            if (_tabStrip != null)
                _tabStrip.gameObject.SetActive(false);
        }

        /// <summary>Shows or hides the window without touching its bindings.</summary>
        public void SetVisible(bool visible) => gameObject.SetActive(visible);

        private void OnBankChanged() => RefreshCurrency();

        private void OnTabsChanged() => RefreshTabs();

        private void RefreshCurrency()
        {
            if (_currency != null)
                _currency.text = CurrencyDisplay.Format(_bank?.Currency);
        }

        private void RefreshTabs()
        {
            IReadOnlyList<string> names = _tabs?.TabNames;
            bool hasTabs = names != null && names.Count > 0;

            if (_tabStrip != null)
                _tabStrip.gameObject.SetActive(hasTabs);

            ClearTabButtons();
            if (!hasTabs || _tabStrip == null)
                return;

            for (int i = 0; i < names.Count; i++)
                CreateTabButton(names[i], i);

            RefreshTabButtons();
        }

        private void CreateTabButton(string name, int position)
        {
            var buttonObject = new GameObject($"Tab {position}", typeof(RectTransform), typeof(Image));
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
            label.text = name;

            int index = position;
            button.onClick.AddListener(() => _tabs?.SelectTab(index));
            _tabButtons.Add(button);
        }

        private void RefreshTabButtons()
        {
            if (_tabs == null)
                return;

            int selected = _tabs.SelectedTab;
            for (int i = 0; i < _tabButtons.Count; i++)
            {
                Image background = _tabButtons[i] != null ? _tabButtons[i].GetComponent<Image>() : null;
                if (background != null)
                {
                    background.color = i == selected
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
