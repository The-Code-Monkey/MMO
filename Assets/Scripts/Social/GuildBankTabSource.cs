using System;
using System.Collections.Generic;
using MMO.Inventory;

namespace MMO.Social
{
    /// <summary>
    /// The grid a bank tab paints into: a pure <see cref="IInventorySource"/> over the rank's
    /// currently open <see cref="GuildBankTab"/>, so the same <see cref="MMO.UI.InventoryGridView"/>
    /// a bag uses can render a bank tab without a server or a spawned player.
    ///
    /// <para>
    /// The source owns no access rules and no RPCs: reading the tab is <see cref="GuildBankRules.ItemAt"/>,
    /// and a right-click (via <see cref="RequestEquip"/>) becomes <see cref="WithdrawRequested"/>, which
    /// the frame wires to the player's intent. Repaints are event-driven exactly like a bag: whoever owns
    /// the current <see cref="GuildBankRecord"/> pushes a new one through <see cref="SetBank"/> and every
    /// bound grid redraws once.
    /// </para>
    /// </summary>
    public sealed class GuildBankTabSource : IInventorySource
    {
        private static readonly List<GuildBankTab> EmptyTabs = new List<GuildBankTab>();

        private GuildBankRecord _bank;
        private int _tabIndex;

        public GuildBankTabSource() { }

        public GuildBankTabSource(GuildBankRecord bank, int tabIndex = 0)
        {
            _bank = bank;
            _tabIndex = bank != null ? ClampIndex(bank, tabIndex) : tabIndex;
        }

        /// <summary>Bag columns: the bank tab grid is a fixed 8x6.</summary>
        public int GridWidth => GuildBankRules.TabWidth;

        /// <summary>Bag rows.</summary>
        public int GridHeight => GuildBankRules.TabHeight;

        /// <summary>The ordinal of the tab this source is painting, or -1 when out of range.</summary>
        public int TabIndex => _tabIndex;

        /// <summary>The bank this source is reading, or null.</summary>
        public GuildBankRecord Bank => _bank;

        /// <summary>The bank's tabs, or an empty list before a bank is bound.</summary>
        public IReadOnlyList<GuildBankTab> Tabs => _bank?.Tabs ?? EmptyTabs;

        /// <summary>The open tab, or null before a bank with that ordinal has been bound.</summary>
        public GuildBankTab CurrentTab => GuildBankRules.FindTab(_bank, _tabIndex);

        /// <summary>
        /// Called by the owning frame when the player right-clicks a bank cell: (tabIndex, gridX,
        /// gridY), mapped straight onto <c>PlayerGuild.RequestWithdraw</c>.
        /// </summary>
        public Action<int, int, int> WithdrawRequested;

        /// <summary>Raised when the bank or the selected tab changes, so bound grids repaint.</summary>
        public event Action ContainersChanged;

        /// <summary>Swaps the bank this source reads; raises <see cref="ContainersChanged"/>.</summary>
        public void SetBank(GuildBankRecord bank)
        {
            _bank = bank;
            _tabIndex = ClampIndex(bank, _tabIndex);
            ContainersChanged?.Invoke();
        }

        /// <summary>Changes the open tab, clamped to the bank's tabs; raises <see cref="ContainersChanged"/>.</summary>
        public void SelectTab(int index)
        {
            int target = ClampIndex(_bank, index);
            if (target == _tabIndex)
                return;

            _tabIndex = target;
            ContainersChanged?.Invoke();
        }

        /// <summary>The item covering a cell of the open tab, or an empty instance when the cell is free.</summary>
        public ItemInstance GetItemAt(int gridX, int gridY)
            => GuildBankRules.ItemAt(CurrentTab, gridX, gridY);

        /// <summary>
        /// The bank half of "right-click": a click on a bank cell asks the server to withdraw the
        /// item there, so <see cref="MMO.UI.InventorySlotView"/> needs no bank-specific wiring.
        /// </summary>
        public void RequestEquip(int gridX, int gridY)
            => WithdrawRequested?.Invoke(_tabIndex, gridX, gridY);

        private static int ClampIndex(GuildBankRecord bank, int index)
        {
            if (bank?.Tabs == null || bank.Tabs.Count < 1)
                return index;

            int first = bank.Tabs[0].Index;
            if (GuildBankRules.FindTab(bank, index) != null)
                return index;

            return first;
        }
    }
}