using System;
using System.Collections.Generic;
using MMO.Economy;
using MMO.Inventory;
using MMO.Social;

namespace MMO.UI
{
    /// <summary>
    /// Binds the generic bank window to the local player's guild bank. Items come from the open
    /// guild tab (via <see cref="GuildBankTabSource"/>); the currency strip reads the guild vault;
    /// the tab strip is exposed through <see cref="IBankTabSource"/>.
    ///
    /// The source owns no rules: deposits and withdrawals are the existing
    /// <see cref="PlayerGuild.RequestDeposit"/> / <see cref="PlayerGuild.RequestWithdraw"/> intents,
    /// so rank permissions and quotas are enforced by the server exactly as before.
    /// </summary>
    public sealed class GuildBankSource : IBankSource, IBankTabSource
    {
        private static readonly List<string> NoTabs = new List<string>();
        private static readonly List<CurrencyBalance> NoCurrency = new List<CurrencyBalance>();

        private readonly PlayerGuild _guild;
        private readonly GuildBankTabSource _tab = new GuildBankTabSource();
        private readonly List<string> _tabNames = new List<string>();

        public GuildBankSource(PlayerGuild guild)
        {
            _guild = guild;
            if (_guild != null)
            {
                _guild.BankChanged += OnBankChanged;
                _tab.SetBank(_guild.Bank);
            }

            RebuildTabNames();
        }

        public string Title => "Guild Bank";

        public IReadOnlyList<CurrencyBalance> Currency
            => _guild?.Bank?.Vault ?? NoCurrency;

        public int GridWidth => _tab.GridWidth;

        public int GridHeight => _tab.GridHeight;

        public event Action ContainersChanged;

        public event Action TabsChanged;

        public IReadOnlyList<string> TabNames => _tabNames.Count > 0 ? _tabNames : NoTabs;

        public int SelectedTab => _tab.TabIndex;

        public ItemInstance GetItemAt(int gridX, int gridY) => _tab.GetItemAt(gridX, gridY);

        public void RequestEquip(int gridX, int gridY) => RequestWithdraw(gridX, gridY);

        public void RequestWithdraw(int gridX, int gridY)
            => _guild?.RequestWithdraw(_tab.TabIndex, gridX, gridY);

        public void RequestDeposit(int bagX, int bagY)
            => _guild?.RequestDeposit(_tab.TabIndex, bagX, bagY);

        public void SelectTab(int index)
        {
            _tab.SelectTab(index);
            TabsChanged?.Invoke();
        }

        public void Unsubscribe()
        {
            if (_guild != null)
                _guild.BankChanged -= OnBankChanged;
        }

        private void OnBankChanged()
        {
            _tab.SetBank(_guild?.Bank);
            RebuildTabNames();
            TabsChanged?.Invoke();
            ContainersChanged?.Invoke();
        }

        private void RebuildTabNames()
        {
            _tabNames.Clear();
            IReadOnlyList<GuildBankTab> tabs = _guild?.Bank?.Tabs;
            if (tabs == null)
                return;

            for (int i = 0; i < tabs.Count; i++)
                _tabNames.Add(tabs[i].Name);
        }
    }
}
