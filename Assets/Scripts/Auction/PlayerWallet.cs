using System;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using MMO.Economy;
using MMO.Inventory;
using MMO.Persistence;
using MMO.Social;
using UnityEngine;

namespace MMO.Auction
{
    /// <summary>
    /// A player's multi-currency wallet. The server is the only writer; it mutates the balances
    /// through the pure <see cref="WalletRules"/> and publishes a snapshot, which replicates to the
    /// owning client as JSON (the same shape <see cref="PlayerGuild"/> uses for its bank).
    ///
    /// <para>
    /// Gold stays a first-class convenience: <see cref="Gold"/>, <see cref="AddGold"/>,
    /// <see cref="RemoveGold"/> and <see cref="TrySpend"/> all operate on
    /// <see cref="CurrencyIds.Gold"/> so the auction house, death penalties and saves keep working
    /// unchanged while silver and faction marks live beside it.
    /// </para>
    ///
    /// <para>
    /// Currency items convert through <see cref="ConvertItem"/>: the server verifies the item, the
    /// currency definition and the amount from the trusted <see cref="CurrencyData"/>, removes the
    /// item, then credits the wallet — restoring the item if the credit is refused, so an item is
    /// never destroyed without its balance being granted.
    /// </para>
    /// </summary>
    public sealed class PlayerWallet : NetworkBehaviour
    {
        private readonly SyncVar<string> _accountJson = new SyncVar<string>();
        private readonly SyncVar<string> _bankJson = new SyncVar<string>();
        private WalletAccount _account = new WalletAccount();
        private List<CurrencyBalance> _bankVault = new List<CurrencyBalance>();
        private PlayerInventory _inventory;

        /// <summary>The gold balance. Convenience over <see cref="GetBalance"/>.</summary>
        public long Gold => GetBalance(CurrencyIds.Gold);

        /// <summary>The current balances, for a UI or a save snapshot.</summary>
        public IReadOnlyList<CurrencyBalance> Balances => _account.Balances;

        /// <summary>Currency held in the personal bank vault, separate from the carried wallet.</summary>
        public IReadOnlyList<CurrencyBalance> BankVault => _bankVault;

        /// <summary>Raised when the gold balance changes, on both server and client.</summary>
        public event Action<long> OnGoldChanged;

        /// <summary>Raised after any wallet balance change, on both server and client.</summary>
        public event Action BalancesChanged;

        /// <summary>Raised after any bank-vault change, on both server and client.</summary>
        public event Action BankChanged;

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            _accountJson.OnChange += OnAccountJsonChanged;
            _bankJson.OnChange += OnBankJsonChanged;
            _inventory = GetComponent<PlayerInventory>();

            if (IsServer && SocialHubService.Instance != null)
            {
                SocialHubService.Instance.AttachWalletView(GetAccountId(), this);
            }
        }

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();
            _accountJson.OnChange -= OnAccountJsonChanged;
            _bankJson.OnChange -= OnBankJsonChanged;

            if (IsServer && SocialHubService.Instance != null)
            {
                SocialHubService.Instance.DetachWalletView(GetAccountId());
            }
        }

        private string GetAccountId()
        {
            PlayerSaveTrigger saveTrigger = GetComponent<PlayerSaveTrigger>();
            return saveTrigger != null ? saveTrigger.AccountId : Owner?.ClientId.ToString();
        }

        /// <summary>The amount of a currency held, or zero when the wallet has none.</summary>
        public long GetBalance(string currencyId) => WalletRules.BalanceOf(_account.Balances, currencyId);

        /// <summary>The amount of a currency held in the personal bank vault, or zero.</summary>
        public long GetBankBalance(string currencyId) => WalletRules.BalanceOf(_bankVault, currencyId);

        /// <summary>Server: seeds the wallet from the loaded save, migrating legacy scalar gold.</summary>
        [Server]
        public void InitializeFromSave(PlayerSaveRecord record)
        {
            long before = Gold;

            _account = WalletAccount.Empty(GetAccountId());
            if (record != null)
            {
                _account.Balances = WalletSaveMigration.WalletBalances(record);
                _bankVault = WalletSaveMigration.BankBalances(record);
            }

            Publish(before);
            PublishBank();
        }

        /// <summary>Server: captures the balances into the save record, keeping legacy gold in sync.</summary>
        [Server]
        public void CaptureToSave(PlayerSaveRecord record)
        {
            if (record == null)
                return;

            record.Currencies = WalletRules.Sanitize(_account.Balances);
            record.BankVault = WalletRules.Sanitize(_bankVault);
            record.Gold = Gold;
        }

        /// <summary>Server: sets a currency to an exact amount (creating it when absent).</summary>
        [Server]
        public bool SetBalance(string currencyId, long amount)
        {
            long before = Gold;
            if (WalletRules.Set(_account.Balances, currencyId, amount) != WalletError.Ok)
                return false;

            Publish(before);
            return true;
        }

        /// <summary>Server: credits a currency.</summary>
        [Server]
        public bool Add(string currencyId, long amount)
        {
            if (amount < 0)
                return false;

            long before = Gold;
            if (WalletRules.Credit(_account.Balances, currencyId, amount) != WalletError.Ok)
                return false;

            Publish(before);
            return true;
        }

        /// <summary>Server: debits a currency; false when the balance is short.</summary>
        [Server]
        public bool Remove(string currencyId, long amount)
        {
            if (amount < 0)
                return false;

            long before = Gold;
            if (WalletRules.Debit(_account.Balances, currencyId, amount) != WalletError.Ok)
                return false;

            Publish(before);
            return true;
        }

        /// <summary>Server: sets the gold balance directly (e.g. after loading from a database).</summary>
        [Server]
        public void SetGold(long amount) => SetBalance(CurrencyIds.Gold, amount);

        /// <summary>Server: adds gold (e.g. from selling items, quest rewards).</summary>
        [Server]
        public void AddGold(long amount) => Add(CurrencyIds.Gold, amount);

        /// <summary>Server: removes gold. Returns false if insufficient.</summary>
        [Server]
        public bool RemoveGold(long amount) => Remove(CurrencyIds.Gold, amount);

        /// <summary>Server: attempts to spend gold, returns true if successful.</summary>
        [Server]
        public bool TrySpend(long amount) => RemoveGold(amount);

        /// <summary>
        /// Server: moves currency from this wallet into another balance list (a guild vault). The
        /// transfer is atomic: a refused credit leaves the wallet untouched.
        /// </summary>
        [Server]
        public WalletError TransferTo(List<CurrencyBalance> destination, string currencyId, long amount)
        {
            long before = Gold;
            WalletError error = WalletRules.Transfer(_account.Balances, destination, currencyId, amount);
            if (error == WalletError.Ok)
                Publish(before);

            return error;
        }

        /// <summary>Server: moves currency from another balance list (a guild vault) into this wallet.</summary>
        [Server]
        public WalletError TransferFrom(List<CurrencyBalance> source, string currencyId, long amount)
        {
            long before = Gold;
            WalletError error = WalletRules.Transfer(source, _account.Balances, currencyId, amount);
            if (error == WalletError.Ok)
                Publish(before);

            return error;
        }

        /// <summary>Client: moves currency from the carried wallet into the personal bank vault.</summary>
        public void RequestBankDeposit(string currencyId, long amount)
        {
            if (!OwnsSelf() || string.IsNullOrEmpty(currencyId) || amount <= 0)
                return;

            RpcBankDeposit(currencyId, amount);
        }

        /// <summary>Client: moves currency from the personal bank vault into the carried wallet.</summary>
        public void RequestBankWithdraw(string currencyId, long amount)
        {
            if (!OwnsSelf() || string.IsNullOrEmpty(currencyId) || amount <= 0)
                return;

            RpcBankWithdraw(currencyId, amount);
        }

        [ServerRpc]
        private void RpcBankDeposit(string currencyId, long amount, NetworkConnection sender = null)
        {
            if (sender == null || Owner == null || sender != Owner)
                return;

            BankDeposit(currencyId, amount);
        }

        [ServerRpc]
        private void RpcBankWithdraw(string currencyId, long amount, NetworkConnection sender = null)
        {
            if (sender == null || Owner == null || sender != Owner)
                return;

            BankWithdraw(currencyId, amount);
        }

        /// <summary>Server: deposits carried currency into the personal bank vault.</summary>
        [Server]
        public bool BankDeposit(string currencyId, long amount)
        {
            long before = Gold;
            WalletError error = WalletRules.Transfer(_account.Balances, _bankVault, currencyId, amount);
            if (error != WalletError.Ok)
                return false;

            Publish(before);
            PublishBank();
            return true;
        }

        /// <summary>Server: withdraws currency from the personal bank vault into the carried wallet.</summary>
        [Server]
        public bool BankWithdraw(string currencyId, long amount)
        {
            long before = Gold;
            WalletError error = WalletRules.Transfer(_bankVault, _account.Balances, currencyId, amount);
            if (error != WalletError.Ok)
                return false;

            Publish(before);
            PublishBank();
            return true;
        }

        /// <summary>Client: asks the server to convert one carried currency item into its balance.</summary>
        public void RequestConvert(string itemInstanceId)
        {
            if (!OwnsSelf() || string.IsNullOrEmpty(itemInstanceId))
                return;

            RpcRequestConvert(itemInstanceId);
        }

        [ServerRpc]
        private void RpcRequestConvert(string itemInstanceId, NetworkConnection sender = null)
        {
            if (sender == null || Owner == null || sender != Owner)
                return;

            ConvertItem(itemInstanceId);
        }

        /// <summary>
        /// Server: converts the bag item with this instance id into its currency. The amount comes
        /// from the trusted <see cref="CurrencyData"/>, never from the client. Order is verify,
        /// consume, credit, with the item restored if the credit is refused.
        /// </summary>
        [Server]
        public bool ConvertItem(string itemInstanceId)
        {
            if (_inventory == null)
                _inventory = GetComponent<PlayerInventory>();

            if (_inventory == null || string.IsNullOrEmpty(itemInstanceId))
                return false;

            if (!TryFindBagItem(itemInstanceId, out ItemInstance item))
                return false;

            CurrencyData currency = CurrencyRegistry.ResolveByItem(item.ItemName);
            if (currency == null || string.IsNullOrEmpty(currency.CurrencyId))
                return false;

            int count = item.StackCount < 1 ? 1 : item.StackCount;
            long credit = (long)currency.ItemValue * count;
            if (credit <= 0)
                return false;

            if (!_inventory.TryRemoveItem(itemInstanceId, out ItemInstance removed))
                return false;

            long before = Gold;
            if (WalletRules.Credit(_account.Balances, currency.CurrencyId, credit) != WalletError.Ok)
            {
                // The credit was refused (for example, overflow). Put the item back rather than
                // destroying it for nothing.
                _inventory.TryAddExistingItem(removed);
                return false;
            }

            Publish(before);
            return true;
        }

        private bool TryFindBagItem(string instanceId, out ItemInstance item)
        {
            item = ItemInstance.Empty;
            IReadOnlyList<ItemInstance> items = _inventory.Items;
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].InstanceId == instanceId)
                {
                    item = items[i];
                    return true;
                }
            }

            return false;
        }

        private bool OwnsSelf()
        {
            NetworkObject self = NetworkObject;
            return self != null && self.IsOwner;
        }

        /// <summary>
        /// Server: publishes the authoritative balances to observers and raises the change events.
        /// <paramref name="previousGold"/> is captured before the mutation so a gold change can be
        /// reported even though the ServerRpc/SyncVar write happens after it.
        /// </summary>
        private void Publish(long previousGold)
        {
            _accountJson.Value = JsonUtility.ToJson(_account);

            BalancesChanged?.Invoke();
            long now = Gold;
            if (now != previousGold)
                OnGoldChanged?.Invoke(now);
        }

        private void OnAccountJsonChanged(string previous, string current, bool asServer)
        {
            // The server owns the account and already raised the events in Publish; only clients
            // rebuild their copy from the replicated snapshot.
            if (asServer)
                return;

            long before = Gold;
            if (!string.IsNullOrEmpty(current))
            {
                WalletAccount parsed = JsonUtility.FromJson<WalletAccount>(current);
                if (parsed != null)
                    _account = parsed;
            }

            BalancesChanged?.Invoke();
            long now = Gold;
            if (now != before)
                OnGoldChanged?.Invoke(now);
        }

        /// <summary>Server: publishes the personal bank vault and raises the change event.</summary>
        private void PublishBank()
        {
            _bankJson.Value = JsonUtility.ToJson(new WalletAccount { Balances = _bankVault });
            BankChanged?.Invoke();
        }

        private void OnBankJsonChanged(string previous, string current, bool asServer)
        {
            if (asServer)
                return;

            if (!string.IsNullOrEmpty(current))
            {
                WalletAccount parsed = JsonUtility.FromJson<WalletAccount>(current);
                if (parsed != null)
                    _bankVault = parsed.Balances ?? new List<CurrencyBalance>();
            }

            BankChanged?.Invoke();
        }
    }
}
