using System;
using System.Threading;
using System.Threading.Tasks;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using MMO.Inventory;
using MMO.Persistence;
using MMO.Social;
using UnityEngine;

namespace MMO.Auction
{
    /// <summary>
    /// Player wallet component that syncs gold amount from the server.
    /// The server is authoritative; clients only receive updates.
    /// </summary>
    public sealed class PlayerWallet : NetworkBehaviour
    {
        private readonly SyncVar<long> _gold = new SyncVar<long>(0);
        private PlayerInventory _inventory;

        public long Gold => _gold.Value;

        public event Action<long> OnGoldChanged;

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            _gold.OnChange += OnGoldChangedCallback;
            _inventory = GetComponent<PlayerInventory>();

            if (IsServer && SocialHubService.Instance != null)
            {
                SocialHubService.Instance.AttachWalletView(GetAccountId(), this);
            }
        }

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();
            _gold.OnChange -= OnGoldChangedCallback;

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

        private void OnGoldChangedCallback(long oldValue, long newValue, bool asServer)
        {
            OnGoldChanged?.Invoke(newValue);
        }

        /// <summary>Server: initializes gold from the loaded player save record.</summary>
        [Server]
        public void InitializeFromSave(PlayerSaveRecord record)
        {
            if (record != null)
                _gold.Value = Math.Max(0, record.Gold);
        }

        /// <summary>Server: sets the gold amount directly (e.g., after loading from database).</summary>
        [Server]
        public void SetGold(long amount)
        {
            _gold.Value = Math.Max(0, amount);
        }

        /// <summary>Server: adds gold (e.g., from selling items, quest rewards).</summary>
        [Server]
        public void AddGold(long amount)
        {
            if (amount <= 0) return;
            _gold.Value += amount;
        }

        /// <summary>Server: removes gold (e.g., for purchases). Returns false if insufficient.</summary>
        [Server]
        public bool RemoveGold(long amount)
        {
            if (amount <= 0) return true;
            if (_gold.Value < amount) return false;
            _gold.Value -= amount;
            return true;
        }

        /// <summary>Server: attempts to spend gold, returns true if successful.</summary>
        [Server]
        public bool TrySpend(long amount)
        {
            return RemoveGold(amount);
        }

        /// <summary>Server: captures current gold into the save record.</summary>
        [Server]
        public void CaptureToSave(PlayerSaveRecord record)
        {
            if (record != null)
                record.Gold = _gold.Value;
        }
    }
}