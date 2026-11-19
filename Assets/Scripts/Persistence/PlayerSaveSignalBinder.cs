using System;
using MMO.Auction;
using MMO.Banking;
using MMO.Content;
using MMO.Crafting;
using MMO.Gameplay;
using MMO.Inventory;

namespace MMO.Persistence
{
    /// <summary>Subscribes a spawned player's component changes to save-dirty signals.</summary>
    internal sealed class PlayerSaveSignalBinder
    {
        private PlayerStats _stats;
        private PlayerInventory _inventory;
        private PlayerQuestLog _quests;
        private PlayerWallet _wallet;
        private PlayerBank _bank;
        private PlayerCrafting _crafting;
        private Action _lastLevelHandler;
        private Action _lastDebouncedHandler;

        public void Bind(
            PlayerStats stats,
            PlayerInventory inventory,
            PlayerQuestLog quests,
            PlayerWallet wallet,
            PlayerBank bank,
            PlayerCrafting crafting,
            Action onLevelChanged,
            Action onDebouncedChanged)
        {
            Unbind();

            _stats = stats;
            _inventory = inventory;
            _quests = quests;
            _wallet = wallet;
            _bank = bank;
            _crafting = crafting;
            _lastLevelHandler = onLevelChanged;
            _lastDebouncedHandler = onDebouncedChanged;

            if (_stats != null)
            {
                _stats.LevelChanged += onLevelChanged;
                _stats.ExperienceChanged += onDebouncedChanged;
            }

            if (_inventory != null)
                _inventory.ContainersChanged += onDebouncedChanged;

            if (_quests != null)
                _quests.ProgressChanged += onDebouncedChanged;

            if (_wallet != null)
            {
                _wallet.BalancesChanged += onDebouncedChanged;
                _wallet.BankChanged += onDebouncedChanged;
            }

            if (_bank != null)
                _bank.BankChanged += onDebouncedChanged;

            // XP can wait for the debounce window; a queue mutation is a durability point (escrow
            // left the bag / an order resolved), so it saves immediately.
            if (_crafting != null)
            {
                _crafting.CraftingChanged += onLevelChanged;
                _crafting.OrdersChanged += onLevelChanged;
            }
        }

        public void Unbind()
        {
            if (_stats != null)
            {
                _stats.LevelChanged -= _lastLevelHandler;
                _stats.ExperienceChanged -= _lastDebouncedHandler;
            }

            if (_inventory != null)
                _inventory.ContainersChanged -= _lastDebouncedHandler;

            if (_quests != null)
                _quests.ProgressChanged -= _lastDebouncedHandler;

            if (_wallet != null)
            {
                _wallet.BalancesChanged -= _lastDebouncedHandler;
                _wallet.BankChanged -= _lastDebouncedHandler;
            }

            if (_bank != null)
                _bank.BankChanged -= _lastDebouncedHandler;

            if (_crafting != null)
            {
                _crafting.CraftingChanged -= _lastLevelHandler;
                _crafting.OrdersChanged -= _lastLevelHandler;
            }

            _stats = null;
            _inventory = null;
            _quests = null;
            _wallet = null;
            _bank = null;
            _crafting = null;
            _lastLevelHandler = null;
            _lastDebouncedHandler = null;
        }
    }
}
