using System.Collections.Generic;
using System.Threading.Tasks;
using FishNet.Object;
using MMO.Auction;
using MMO.Banking;
using MMO.Content;
using MMO.Crafting;
using MMO.Gameplay;
using MMO.Inventory;
using MMO.Progression;
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
        private PlayerAchievements _achievements;
        private PlayerCrafting _crafting;
        private string _accountId;
        private bool _subscribed;
        private readonly WalletReservationReconciler _walletReconciler = new WalletReservationReconciler();
        private readonly PlayerSaveSignalBinder _signals = new PlayerSaveSignalBinder();
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
            _achievements = GetComponent<PlayerAchievements>();
            _crafting = GetComponent<PlayerCrafting>();
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

            PlayerSaveApplier.Apply(
                record, _accountId, _stats, _inventory, _quests, _wallet, _bank, _achievements, _crafting);

            service.RegisterLivePlayer(_accountId, Capture);
            _signals.Bind(
                _stats, _inventory, _quests, _wallet, _bank, _crafting,
                OnLevelChanged, OnDebouncedChanged);
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
            => _signals.Unbind();

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
                    service.QueueFinalSave(_accountId, Capture());

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

            _walletReconciler.Tick(_accountId, _wallet);
        }

        private void OnLevelChanged()
        {
            if (_subscribed && !string.IsNullOrEmpty(_accountId))
                PlayerPersistenceService.Instance?.MarkDirty(_accountId, immediate: true);
        }

        /// <summary>Any inventory, quest, wallet or bank change marks the player dirty for debounce.</summary>
        private void OnDebouncedChanged()
        {
            if (_subscribed && !string.IsNullOrEmpty(_accountId))
                PlayerPersistenceService.Instance?.MarkDirty(_accountId, immediate: false);
        }

        private PlayerSaveRecord Capture()
            => PlayerSnapshotFactory.Capture(
                _accountId, _stats, _inventory, _quests, _wallet, _bank, _achievements, _crafting,
                transform.position);
    }
}
