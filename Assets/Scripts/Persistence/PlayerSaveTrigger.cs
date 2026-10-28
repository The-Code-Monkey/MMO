using System.Collections.Generic;
using System.Threading.Tasks;
using FishNet.Object;
using MMO.Auction;
using MMO.Banking;
using MMO.Content;
using MMO.Gameplay;
using MMO.Inventory;
using MMO.Social;
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
        private PlayerQuestLog _quests;
        private PlayerWallet _wallet;
        private PlayerBank _bank;
        private string _accountId;
        private bool _subscribed;
        private bool _reconcilingWalletReservations;
        private float _nextWalletReservationReconcileTime;
        private float _joinRetryUntil;
        private bool _joinTimeoutLogged;

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
            _quests = GetComponent<PlayerQuestLog>();
            _wallet = GetComponent<PlayerWallet>();
            _bank = GetComponent<PlayerBank>();
            if (_stats == null || _inventory == null)
            {
                Debug.LogError($"{nameof(PlayerSaveTrigger)}: the player prefab is missing PlayerStats or PlayerInventory.", this);
                return;
            }

            // The player can spawn on the client's start-scene load before the join gate has
            // finished loading the record. Retry briefly from Update instead of losing the join.
            _joinRetryUntil = Time.unscaledTime + 10f;
            TryApplyJoin();
        }

        private void TryApplyJoin()
        {
            if (_subscribed)
                return;

            PlayerPersistenceService service = PlayerPersistenceService.Instance;
            int clientId = Owner != null ? Owner.ClientId : -1;
            if (service == null || clientId < 0 ||
                !service.TryGetJoin(clientId, out _accountId, out PlayerSaveRecord record))
                return;

            // Apply the loaded save in one shot. Subscribing afterwards keeps the apply itself from
            // re-triggering the same events it is creating.
            PlayerSaveMapper.Apply(
                record,
                out List<ItemInstance> bag,
                out List<ItemInstance> equipment,
                out int skippedItems,
                out long gold,
                out int experience,
                out List<string> completedQuestIds,
                out List<QuestProgressEntry> questProgress);

            if (skippedItems > 0)
            {
                Debug.LogWarning($"{nameof(PlayerSaveTrigger)}: dropped {skippedItems} item(s) from '{_accountId}' that no longer have a definition.");
            }

            if (record != null)
            {
                _stats.ApplySave(record.Health, record.Mana, record.Level);
            }

            _stats.ApplyExperience(experience);
            _inventory.Restore(bag, equipment);
            _quests?.Restore(completedQuestIds, questProgress);
            _wallet?.InitializeFromSave(record);
            _bank?.InitializeFromSave(record);

            service.RegisterLivePlayer(_accountId, Capture);
            _stats.LevelChanged += OnLevelChanged;
            _stats.ExperienceChanged += OnExperienceChanged;
            _inventory.ContainersChanged += OnInventoryChanged;
            if (_quests != null)
                _quests.ProgressChanged += OnQuestProgressChanged;
            if (_wallet != null)
            {
                _wallet.BalancesChanged += OnWalletChanged;
                _wallet.BankChanged += OnWalletChanged;
            }
            if (_bank != null)
                _bank.BankChanged += OnWalletChanged;
            _subscribed = true;
        }

        /// <summary>
        /// Marks the player dirty when a server walk ends, so a later crash can only lose the
        /// distance walked since the last stop rather than a whole autosave interval.
        /// </summary>
        public void MarkDirtyOnArrival()
        {
            if (IsServer && _subscribed && !string.IsNullOrEmpty(_accountId))
                PlayerPersistenceService.Instance?.MarkDirty(_accountId, immediate: false);
        }

        /// <summary>
        /// Called after a zone handoff has already flushed the final state. Suppresses the second
        /// save OnStopNetwork would otherwise queue, which would race the destination's first save
        /// with the version the destination has already loaded.
        /// </summary>
        public void MarkTransferred()
        {
            if (!IsServer || !_subscribed)
                return;

            UnsubscribeFromChanges();
            PlayerPersistenceService.Instance?.UnregisterLivePlayer(_accountId);
            _subscribed = false;
        }

        private void UnsubscribeFromChanges()
        {
            if (_stats != null)
            {
                _stats.LevelChanged -= OnLevelChanged;
                _stats.ExperienceChanged -= OnExperienceChanged;
            }

            if (_inventory != null)
                _inventory.ContainersChanged -= OnInventoryChanged;

            if (_quests != null)
                _quests.ProgressChanged -= OnQuestProgressChanged;

            if (_wallet != null)
            {
                _wallet.BalancesChanged -= OnWalletChanged;
                _wallet.BankChanged -= OnWalletChanged;
            }

            if (_bank != null)
                _bank.BankChanged -= OnWalletChanged;
        }

        public override void OnStopNetwork()
        {
            if (IsServer && _subscribed && !string.IsNullOrEmpty(_accountId))
            {
                PlayerPersistenceService service = PlayerPersistenceService.Instance;
                if (service != null)
                {
                    UnsubscribeFromChanges();
                    _subscribed = false;

                    // The state is still live during this callback, so snapshot it now: the async
                    // flush that follows may run after the object is gone.
                    PlayerSaveRecord finalRecord = PlayerSaveMapper.Capture(
                        _accountId,
                        _stats.Level,
                        _stats.Health,
                        _stats.Mana,
                        _wallet != null ? _wallet.Gold : 0,
                        transform.position,
                        _inventory.Items,
                        _inventory.Equipment,
                        _stats.Experience,
                        _quests != null ? _quests.CompletedQuestIds : null,
                        _quests != null ? _quests.Progress : null);
                    _wallet?.CaptureToSave(finalRecord);
                    _bank?.CaptureToSave(finalRecord);
                    service.QueueFinalSave(_accountId, finalRecord);

                    service.UnregisterLivePlayer(_accountId);
                    service.ForgetClient(Owner != null ? Owner.ClientId : -1);
                }
            }

            _accountId = null;
        }

        private void Update()
        {
            if (!IsServer)
                return;

            if (!_subscribed)
            {
                if (Time.unscaledTime <= _joinRetryUntil)
                    TryApplyJoin();
                else if (!_joinTimeoutLogged)
                {
                    _joinTimeoutLogged = true;
                    int clientId = Owner != null ? Owner.ClientId : -1;
                    Debug.LogWarning(
                        $"{nameof(PlayerSaveTrigger)}: no join record for client {clientId}; " +
                        "this player will not be saved or transferable.", this);
                }

                return;
            }

            if (_reconcilingWalletReservations ||
                Time.unscaledTime < _nextWalletReservationReconcileTime)
            {
                return;
            }

            PlayerPersistenceService service = PlayerPersistenceService.Instance;
            if (service == null || service.GetPendingWalletReservations(_accountId).Count == 0)
                return;

            _nextWalletReservationReconcileTime = Time.unscaledTime + 5f;
            _ = ReconcileWalletReservationsAsync(service);
        }

        private async Task ReconcileWalletReservationsAsync(PlayerPersistenceService service)
        {
            _reconcilingWalletReservations = true;
            try
            {
                AuctionService auction = SocialHubService.Instance != null
                    ? SocialHubService.Instance.Auction
                    : null;
                if (auction == null)
                    return;

                List<WalletReservationRecord> reservations = service.GetPendingWalletReservations(_accountId);
                for (int i = 0; i < reservations.Count; i++)
                {
                    if (!await auction.ReconcileWalletReservationAsync(_accountId, _wallet, reservations[i]))
                        break;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"{nameof(PlayerSaveTrigger)}: failed to reconcile wallet reservations for '{_accountId}': {e.Message}");
            }
            finally
            {
                _reconcilingWalletReservations = false;
            }
        }

        private void OnLevelChanged()
        {
            if (_subscribed && !string.IsNullOrEmpty(_accountId))
                PlayerPersistenceService.Instance?.MarkDirty(_accountId, immediate: true);
        }

        private void OnExperienceChanged()
        {
            if (_subscribed && !string.IsNullOrEmpty(_accountId))
                PlayerPersistenceService.Instance?.MarkDirty(_accountId, immediate: false);
        }

        private void OnInventoryChanged()
        {
            if (_subscribed && !string.IsNullOrEmpty(_accountId))
                PlayerPersistenceService.Instance?.MarkDirty(_accountId, immediate: false);
        }

        private void OnQuestProgressChanged()
        {
            if (_subscribed && !string.IsNullOrEmpty(_accountId))
                PlayerPersistenceService.Instance?.MarkDirty(_accountId, immediate: false);
        }

        /// <summary>Any wallet or bank-vault change marks the player dirty; the next flush captures it.</summary>
        private void OnWalletChanged()
        {
            if (_subscribed && !string.IsNullOrEmpty(_accountId))
                PlayerPersistenceService.Instance?.MarkDirty(_accountId, immediate: false);
        }

        private PlayerSaveRecord Capture()
        {
            PlayerSaveRecord record = PlayerSaveMapper.Capture(
                _accountId,
                _stats.Level,
                _stats.Health,
                _stats.Mana,
                _wallet != null ? _wallet.Gold : 0,
                transform.position,
                _inventory.Items,
                _inventory.Equipment,
                _stats.Experience,
                _quests != null ? _quests.CompletedQuestIds : null,
                _quests != null ? _quests.Progress : null);
            _wallet?.CaptureToSave(record);
            _bank?.CaptureToSave(record);
            return record;
        }
    }
}
