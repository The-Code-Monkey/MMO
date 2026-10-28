using System;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using MMO.Inventory;
using MMO.Persistence;
using MMO.Social;
using UnityEngine;

namespace MMO.Banking
{
    /// <summary>
    /// The player's personal bank: an 8x6 grid of items stored server-side and replicated to the
    /// owner as JSON, plus the entry point every bank teller uses (`RequestOpen`). The bank's
    /// currency is the personal vault on <see cref="MMO.Auction.PlayerWallet.BankVault"/>; this
    /// component owns only the items.
    ///
    /// <para>
    /// The server is the only writer. A teller click asks the server to open the bank; the server
    /// checks the player is close enough to that teller and answers with the teller's
    /// <see cref="BankKind"/>. Every deposit and withdrawal re-checks the same distance, so a
    /// forged RPC cannot move items from across the map.
    /// </para>
    ///
    /// <para>
    /// Deposit order is space-check, remove-from-bag, add-to-bank. Withdrawal is
    /// space-check, remove-from-bank, add-to-bag, restoring the bank entry if the bag add fails —
    /// the same "never lose an item" order the guild bank uses.
    /// </para>
    /// </summary>
    public sealed class PlayerBank : NetworkBehaviour
    {
        /// <summary>The item list as it travels in the replicated snapshot (JsonUtility needs a root class).</summary>
        [Serializable]
        private sealed class BankSnapshot
        {
            public List<ItemInstance> Items = new List<ItemInstance>();
        }

        private readonly SyncVar<string> _bankJson = new SyncVar<string>();
        private readonly List<ItemInstance> _bank = new List<ItemInstance>();

        private PlayerInventory _inventory;
        private PlayerGuild _guild;

        /// <summary>The teller the bank was last opened at, so every mutation can re-check reach.</summary>
        private NetworkObject _openTeller;

        /// <summary>Raised on the owning client when a teller accepted an open request.</summary>
        public event Action<BankKind> BankOpened;

        /// <summary>Raised when the replicated bank contents change, on server and client.</summary>
        public event Action BankChanged;

        /// <summary>The bank's items on the client, or the authoritative list on the server.</summary>
        public IReadOnlyList<ItemInstance> BankItems => _bank;

        private PlayerInventory Inventory =>
            _inventory != null ? _inventory : _inventory = GetComponent<PlayerInventory>();

        private PlayerGuild Guild =>
            _guild != null ? _guild : _guild = GetComponent<PlayerGuild>();

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            _bankJson.OnChange += OnBankJsonChanged;
        }

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();
            _bankJson.OnChange -= OnBankJsonChanged;
        }

        /// <summary>The item covering a bank cell, or an empty instance when the cell is free.</summary>
        public ItemInstance GetBankItemAt(int gridX, int gridY)
            => PersonalBankRules.ItemAt(_bank, gridX, gridY);

        /// <summary>Server: seeds the bank from the loaded save.</summary>
        [Server]
        public void InitializeFromSave(PlayerSaveRecord record)
        {
            _bank.Clear();
            PlayerSaveMapper.ApplyBank(record?.BankItems, out List<ItemInstance> items, out _);
            _bank.AddRange(items);
            Publish();
        }

        /// <summary>Server: captures the bank into the save record.</summary>
        [Server]
        public void CaptureToSave(PlayerSaveRecord record)
        {
            if (record == null)
                return;

            record.BankItems = PlayerSaveMapper.CaptureBank(_bank);
        }

        /// <summary>Client: asks the server to open the teller's bank.</summary>
        public void RequestOpen(BankTeller teller)
        {
            if (!OwnsSelf() || teller == null)
                return;

            RpcRequestOpen(teller.NetworkObject);
        }

        [ServerRpc]
        private void RpcRequestOpen(NetworkObject tellerObject, NetworkConnection sender = null)
        {
            if (sender == null || Owner == null || sender != Owner)
                return;

            if (tellerObject == null || !tellerObject.TryGetComponent(out BankTeller teller))
                return;

            if (!BankTeller.IsWithinReach(transform.position, teller.transform.position))
                return;

            // A guild teller is only useful to a member; refuse silently otherwise.
            if (teller.Kind == BankKind.Guild && string.IsNullOrEmpty(Guild?.GuildId))
                return;

            _openTeller = tellerObject;
            TargetOpenBank(Owner, (int)teller.Kind);
        }

        [TargetRpc]
        private void TargetOpenBank(NetworkConnection target, int kind)
        {
            if (!IsOwner)
                return;

            BankOpened?.Invoke((BankKind)kind);
        }

        /// <summary>Client: deposits the item at a backpack cell into the personal bank.</summary>
        public void RequestDeposit(int bagX, int bagY)
        {
            if (!OwnsSelf())
                return;

            RpcDeposit(bagX, bagY);
        }

        /// <summary>Client: withdraws the item at a bank cell into the backpack.</summary>
        public void RequestWithdraw(int gridX, int gridY)
        {
            if (!OwnsSelf())
                return;

            RpcWithdraw(gridX, gridY);
        }

        [ServerRpc]
        private void RpcDeposit(int bagX, int bagY, NetworkConnection sender = null)
        {
            if (sender == null || Owner == null || sender != Owner)
                return;

            if (!AtOpenTeller())
                return;

            Deposit(bagX, bagY);
        }

        [ServerRpc]
        private void RpcWithdraw(int gridX, int gridY, NetworkConnection sender = null)
        {
            if (sender == null || Owner == null || sender != Owner)
                return;

            if (!AtOpenTeller())
                return;

            Withdraw(gridX, gridY);
        }

        /// <summary>Server: moves the item at a backpack cell into the bank.</summary>
        [Server]
        public bool Deposit(int bagX, int bagY)
        {
            PlayerInventory inventory = Inventory;
            if (inventory == null)
                return false;

            ItemInstance item = inventory.GetItemAt(bagX, bagY);
            if (item.IsEmpty || !PersonalBankRules.BankHasRoom(_bank, item))
                return false;

            if (!PersonalBankRules.TryFindBankCell(_bank, item, out int bankX, out int bankY))
                return false;

            if (!inventory.RemoveItemAt(bagX, bagY))
                return false;

            item.GridX = bankX;
            item.GridY = bankY;
            _bank.Add(item);
            Publish();
            return true;
        }

        /// <summary>Server: moves the item at a bank cell into the backpack.</summary>
        [Server]
        public bool Withdraw(int gridX, int gridY)
        {
            PlayerInventory inventory = Inventory;
            if (inventory == null)
                return false;

            int index = PersonalBankRules.IndexOf(_bank, gridX, gridY);
            if (index < 0)
                return false;

            ItemInstance item = _bank[index];
            if (!PersonalBankRules.BagHasRoom(inventory.Items, item))
                return false;

            _bank.RemoveAt(index);
            if (!inventory.TryAddExistingItem(item))
            {
                // The bag shifted between the space check and the add: put the item back rather
                // than delete it.
                _bank.Add(item);
                return false;
            }

            Publish();
            return true;
        }

        private bool AtOpenTeller()
        {
            if (_openTeller == null)
                return false;

            return BankTeller.IsWithinReach(transform.position, _openTeller.transform.position);
        }

        private bool OwnsSelf()
        {
            NetworkObject self = NetworkObject;
            return self != null && self.IsOwner;
        }

        private void Publish()
        {
            _bankJson.Value = JsonUtility.ToJson(new BankSnapshot { Items = _bank });
            BankChanged?.Invoke();
        }

        private void OnBankJsonChanged(string previous, string current, bool asServer)
        {
            // The server owns the list and already raised BankChanged in Publish.
            if (asServer)
                return;

            _bank.Clear();
            if (!string.IsNullOrEmpty(current))
            {
                BankSnapshot snapshot = JsonUtility.FromJson<BankSnapshot>(current);
                if (snapshot?.Items != null)
                    _bank.AddRange(snapshot.Items);
            }

            BankChanged?.Invoke();
        }

        /// <summary>The local owner's bank shell, or null on a dedicated server / before spawn.</summary>
        public static PlayerBank FindLocal(FishNet.Managing.NetworkManager networkManager)
            => MMO.Gameplay.PlayerCombat.FindLocal(networkManager) is { } combat
                ? combat.GetComponent<PlayerBank>()
                : null;
    }
}
