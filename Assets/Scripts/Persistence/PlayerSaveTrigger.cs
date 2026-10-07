using System.Collections.Generic;
using FishNet.Object;
using MMO.Gameplay;
using MMO.Inventory;
using UnityEngine;

namespace MMO.Persistence
{
    /// <summary>
    /// The per-player persistence hook. Added to the player prefab by
    /// <see cref="Net.PlayerPrefabFactory"/>; this is the component that turns the issue's three
    /// save moments into backend writes:
    ///
    /// <list type="bullet">
    /// <item><b>connect</b> — consume the record the gate loaded into
    /// <see cref="PlayerPersistenceService"/> and apply it to this object's <see cref="PlayerStats"/>
    /// and <see cref="PlayerInventory"/> before the player does anything;</item>
    /// <item><b>level up / inventory change</b> — mark the player dirty (immediately for a level,
    /// debounced for container churn) so the service flushes them;</item>
    /// <item><b>disconnect / despawn</b> — snapshot the final state while the SyncVars are still
    /// live and queue it as the last save.</item>
    /// </list>
    ///
    /// Everything is server-only: clients hold a replicated copy of the player but never persist it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerSaveTrigger : NetworkBehaviour
    {
        private PlayerStats _stats;
        private PlayerInventory _inventory;
        private string _accountId;
        private bool _subscribed;

        /// <summary>The account this player's record lives under; empty until the join is applied.</summary>
        public string AccountId => _accountId;

        /// <summary>
        /// Snapshots the player's live state right now — the handoff path a
        /// <see cref="Gameplay.ZoneTransferTrigger"/> uses to make sure the destination server reads
        /// this player's current health, mana, level and inventory, not a stale autosave. Server-only
        /// like everything else here; clients can never ask for a snapshot.
        /// </summary>
        public PlayerSaveRecord CaptureNow()
        {
            if (!IsServer || !_subscribed || string.IsNullOrEmpty(_accountId) || _stats == null || _inventory == null)
                return null;

            return Capture();
        }

        public override void OnStartNetwork()
        {
            if (!IsServer)
                return;

            _stats = GetComponent<PlayerStats>();
            _inventory = GetComponent<PlayerInventory>();
            if (_stats == null || _inventory == null)
            {
                Debug.LogError($"{nameof(PlayerSaveTrigger)}: the player prefab is missing PlayerStats or PlayerInventory.", this);
                return;
            }

            PlayerPersistenceService service = PlayerPersistenceService.Instance;
            int clientId = Owner != null ? Owner.ClientId : -1;
            if (service == null || !service.TryGetJoin(clientId, out _accountId, out PlayerSaveRecord record))
                return;

            // Apply the loaded save in one shot. Subscribing afterwards keeps the apply itself from
            // re-triggering the same events it is creating.
            PlayerSaveMapper.Apply(record, out List<ItemInstance> bag, out List<ItemInstance> equipment, out int skippedItems);
            if (skippedItems > 0)
            {
                Debug.LogWarning($"{nameof(PlayerSaveTrigger)}: dropped {skippedItems} item(s) from '{_accountId}' that no longer have a definition.");
            }

            if (record != null)
            {
                _stats.ApplySave(record.Health, record.Mana, record.Level);
            }

            _inventory.Restore(bag, equipment);

            service.RegisterLivePlayer(_accountId, Capture);
            _stats.LevelChanged += OnLevelChanged;
            _inventory.ContainersChanged += OnInventoryChanged;
            _subscribed = true;
        }

        public override void OnStopNetwork()
        {
            if (IsServer && _subscribed && !string.IsNullOrEmpty(_accountId))
            {
                PlayerPersistenceService service = PlayerPersistenceService.Instance;
                if (service != null)
                {
                    _stats.LevelChanged -= OnLevelChanged;
                    _inventory.ContainersChanged -= OnInventoryChanged;
                    _subscribed = false;

                    // The state is still live during this callback, so snapshot it now: the async
                    // flush that follows may run after the object is gone.
                    service.QueueFinalSave(
                        _accountId,
                        PlayerSaveMapper.Capture(
                            _accountId,
                            _stats.Level,
                            _stats.Health,
                            _stats.Mana,
                            transform.position,
                            _inventory.Items,
                            _inventory.Equipment));

                    service.UnregisterLivePlayer(_accountId);
                    service.ForgetClient(Owner != null ? Owner.ClientId : -1);
                }
            }

            _accountId = null;
        }

        private void OnLevelChanged()
        {
            if (_subscribed && !string.IsNullOrEmpty(_accountId))
                PlayerPersistenceService.Instance?.MarkDirty(_accountId, immediate: true);
        }

        private void OnInventoryChanged()
        {
            if (_subscribed && !string.IsNullOrEmpty(_accountId))
                PlayerPersistenceService.Instance?.MarkDirty(_accountId, immediate: false);
        }

        private PlayerSaveRecord Capture()
        {
            return PlayerSaveMapper.Capture(
                _accountId,
                _stats.Level,
                _stats.Health,
                _stats.Mana,
                transform.position,
                _inventory.Items,
                _inventory.Equipment);
        }
    }
}