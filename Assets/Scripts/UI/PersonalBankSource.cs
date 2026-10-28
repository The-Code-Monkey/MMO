using System;
using System.Collections.Generic;
using MMO.Auction;
using MMO.Banking;
using MMO.Economy;
using MMO.Inventory;

namespace MMO.UI
{
    /// <summary>
    /// Binds the generic bank window to the local player's personal bank. Items come from
    /// <see cref="PlayerBank"/>; the currency strip reads the personal vault on
    /// <see cref="PlayerWallet.BankVault"/>. Both are replicated snapshots, so the window repaints
    /// from their change events and never polls.
    /// </summary>
    public sealed class PersonalBankSource : IBankSource
    {
        private static readonly List<CurrencyBalance> NoCurrency = new List<CurrencyBalance>();

        private readonly PlayerBank _bank;
        private readonly PlayerWallet _wallet;

        public PersonalBankSource(PlayerBank bank, PlayerWallet wallet)
        {
            _bank = bank;
            _wallet = wallet;

            if (_bank != null)
                _bank.BankChanged += NotifyChanged;
            if (_wallet != null)
                _wallet.BankChanged += NotifyChanged;
        }

        public string Title => "Bank";

        public IReadOnlyList<CurrencyBalance> Currency
            => _wallet != null ? _wallet.BankVault : NoCurrency;

        public int GridWidth => PersonalBankRules.BankWidth;

        public int GridHeight => PersonalBankRules.BankHeight;

        public event Action ContainersChanged;

        public ItemInstance GetItemAt(int gridX, int gridY)
            => _bank != null ? _bank.GetBankItemAt(gridX, gridY) : ItemInstance.Empty;

        public void RequestEquip(int gridX, int gridY) => RequestWithdraw(gridX, gridY);

        public void RequestWithdraw(int gridX, int gridY)
            => _bank?.RequestWithdraw(gridX, gridY);

        public void RequestDeposit(int bagX, int bagY)
            => _bank?.RequestDeposit(bagX, bagY);

        public void Unsubscribe()
        {
            if (_bank != null)
                _bank.BankChanged -= NotifyChanged;
            if (_wallet != null)
                _wallet.BankChanged -= NotifyChanged;
        }

        private void NotifyChanged() => ContainersChanged?.Invoke();
    }
}
