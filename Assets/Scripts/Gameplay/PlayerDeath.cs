using System.Collections.Generic;
using FishNet.Object;
using MMO.Auction;
using MMO.Inventory;
using MMO.Net;
using MMO.Persistence;
using UnityEngine;

namespace MMO.Gameplay
{
    /// <summary>
    /// Turns a death into a zone-tier consequence. Added to the player prefab by
    /// <see cref="Net.PlayerPrefabFactory"/>, it subscribes to <see cref="PlayerStats.Died"/> on
    /// the server and, on that single crossing into zero health, asks
    /// <see cref="ZoneRiskConfig.ResolveTierAt"/> where the player fell, resolves
    /// <see cref="RiskRules.ResolveDeath"/>, and applies the outcome:
    ///
    /// <list type="bullet">
    /// <item><b>Safe / Yellow</b> — wear the destructible worn gear and sink gold to repair;</item>
    /// <item><b>Red</b> — the above (heavier) plus a respawn that costs progress;</item>
    /// <item><b>Black</b> — instantiate the bag and worn gear as a networked
    /// <see cref="LootBag"/> on the ground, then wipe them from the live profile.</item>
    /// </list>
    ///
    /// All of it is server-only. The component never runs on a client, and it decides nothing
    /// itself — every number comes from the pure <see cref="RiskRules"/>. The loot bag is spawned
    /// through the player's own <see cref="NetworkManager"/> so it replicates to everyone, and the
    /// bag is filled <em>before</em> the profile is cleared, so a failed spawn leaves the player
    /// with their items rather than deleting them into the void.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerDeath : NetworkBehaviour
    {
        [SerializeField]
        private NetworkObject _lootBagPrefab;

        private PlayerStats _stats;
        private PlayerInventory _inventory;
        private PlayerWallet _wallet;
        private bool _subscribed;

        /// <summary>The prefab a black-zone death drops, or null when drops are unconfigured.</summary>
        public NetworkObject LootBagPrefab => _lootBagPrefab;

        /// <summary>Reconfigures the drop prefab (an editor baker or a test).</summary>
        public void SetLootBagPrefab(NetworkObject prefab)
        {
            _lootBagPrefab = prefab;
        }

        public override void OnStartNetwork()
        {
            if (!IsServer)
                return;

            _stats = GetComponent<PlayerStats>();
            _inventory = GetComponent<PlayerInventory>();
            _wallet = GetComponent<PlayerWallet>();
            if (_stats == null)
            {
                Debug.LogError($"{nameof(PlayerDeath)}: the player prefab has no {nameof(PlayerStats)}; death rules are inactive.", this);
                return;
            }

            _stats.Died += OnDied;
            _subscribed = true;
        }

        public override void OnStopNetwork()
        {
            if (_subscribed && _stats != null)
                _stats.Died -= OnDied;

            _subscribed = false;
        }

        /// <summary>
        /// The zone tier at the player's current position. Exposed so a test can assert the tier
        /// the resolver sees without reaching into the private subscription.
        /// </summary>
        public ZoneRiskTier ResolveCurrentTier()
        {
            return ZoneRiskConfig.ResolveTierAt(transform.position);
        }

        private void OnDied()
        {
            if (!IsServer || !isActiveAndEnabled)
                return;

            ApplyOutcome(RiskRules.ResolveDeath(ResolveCurrentTier()));
        }

        /// <summary>
        /// Applies a resolved outcome in the order that keeps the profile safe: drop loot before
        /// anything is cleared, wear gear, charge the repair bill, and only then respawn. Pure
        /// enough for a test to drive directly with a hand-built <see cref="DeathOutcome"/> and no
        /// actual death.
        /// </summary>
        public void ApplyOutcome(DeathOutcome outcome)
        {
            if (!IsServer)
                return;

            if (outcome.DropInventory)
                DropLoot();

            if (outcome.DurabilityDamage > 0 && _inventory != null)
                _inventory.ApplyDurabilityDamage(outcome.DurabilityDamage);

            if (outcome.RepairCost > 0 && _wallet != null)
                ChargeRepair(outcome.RepairCost);

            // Every death resurrects; a risk tier decides the *consequences*, never whether the
            // player stands back up. Red and Black mark the outcome permanent (lost progress and
            // dropped loot), but a safe-zone death must not leave the player at zero health.
            if (_stats != null)
                _stats.Respawn();
        }

        private void DropLoot()
        {
            if (_inventory == null)
                return;

            List<ItemInstance> loot = _inventory.LootableItems();
            if (loot.Count == 0)
                return;

            LootBag bag = LootBagSpawner.Spawn(NetworkManager, _lootBagPrefab, transform.position, loot);
            if (bag == null)
            {
                // No bag could be spawned: keep the items. Deleting them here would be
                // unrecoverable, so a misconfigured drop is loud and lossless.
                Debug.LogError($"{nameof(PlayerDeath)}: could not spawn a loot bag; '{name}' keeps their items instead of dropping them.", this);
                return;
            }

            _inventory.ClearAll();
            PersistNow();
        }

        private void ChargeRepair(int repairCost)
        {
            long charge = RiskRules.ClampRepair(_wallet.Gold, repairCost);
            if (charge > 0)
                _wallet.TrySpend(charge);
        }

        /// <summary>
        /// Pushes the wiped profile to the backend immediately instead of waiting for the debounced
        /// autosave, so a black-zone death is durable even if the server dies before the next
        /// flush. A player with no loaded record (no account id yet) simply has nothing to write.
        /// </summary>
        private void PersistNow()
        {
            PlayerSaveTrigger trigger = GetComponent<PlayerSaveTrigger>();
            if (trigger == null || string.IsNullOrEmpty(trigger.AccountId))
                return;

            PlayerPersistenceService.Instance?.MarkDirty(trigger.AccountId, immediate: true);
        }
    }
}
