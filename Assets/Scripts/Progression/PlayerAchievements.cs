using System;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Object;
using MMO.Auction;
using MMO.Content;
using MMO.Gameplay;
using MMO.Inventory;
using MMO.Persistence;
using UnityEngine;

namespace MMO.Progression
{
    /// <summary>
    /// A player's achievements, evaluated only on the server. Gameplay systems raise a
    /// <see cref="AchievementTrigger"/> through <see cref="Record"/>; this component advances the
    /// counters, pays each completion exactly once, and pushes the completion to the owner for a
    /// toast.
    ///
    /// <para>
    /// Level-ups and quest completions are subscribed here because they already exist as server
    /// events. Gathering and crafting call <see cref="Record"/> at their success points, and a raid
    /// system can call it when #83 lands. The client never calls any of it — there is no RPC that
    /// creates an event, which is what keeps achievements server-validated.
    /// </para>
    ///
    /// <para>
    /// Progress is persisted inside the player's save record, so it travels with the rest of the
    /// player state and survives a zone handoff without a second backend round trip.
    /// </para>
    /// </summary>
    public sealed class PlayerAchievements : NetworkBehaviour
    {
        private readonly AchievementRecord _record = new();
        private AchievementTracker _tracker;
        private PlayerStats _stats;
        private PlayerQuestLog _quests;
        private PlayerInventory _inventory;
        private PlayerWallet _wallet;

        /// <summary>Raised on the owning client when an achievement completes.</summary>
        public event Action<AchievementData> AchievementCompleted;

        /// <summary>How many achievements this player has completed.</summary>
        public int CompletedCount => _record.Completed.Count;

        /// <summary>The completed ids, in completion order. Read-only for a client-side list.</summary>
        public IReadOnlyList<string> CompletedIds => _record.Completed;

        public override void OnStartNetwork()
        {
            if (!IsServer)
                return;

            _stats = GetComponent<PlayerStats>();
            _quests = GetComponent<PlayerQuestLog>();
            _inventory = GetComponent<PlayerInventory>();
            _wallet = GetComponent<PlayerWallet>();

            // An asset nobody has referenced yet is not registered; force the shipped set in so a
            // fresh server counts achievements without relying on load order.
            AchievementRegistry.LoadAllFromResources();
            _tracker = new AchievementTracker(_record, AchievementRegistry.All());

            if (_stats != null)
                _stats.LevelChanged += OnLevelChanged;

            if (_quests != null)
                _quests.QuestCompleted += OnQuestCompleted;
        }

        public override void OnStopNetwork()
        {
            if (!IsServer)
                return;

            if (_stats != null)
                _stats.LevelChanged -= OnLevelChanged;

            if (_quests != null)
                _quests.QuestCompleted -= OnQuestCompleted;
        }

        /// <summary>
        /// Server: apply one gameplay event. Called by the gathering/crafting success paths (and a
        /// future raid-clear hook); never reachable from a client RPC.
        /// </summary>
        [Server]
        public void Record(AchievementTrigger trigger, int amount)
        {
            if (_tracker == null)
                return;

            List<AchievementData> completed = _tracker.RecordEvent(trigger, amount);
            for (int i = 0; i < completed.Count; i++)
                Grant(completed[i]);
        }

        /// <summary>Server: rebuild progress from a loaded player save, then retry unpaid rewards.</summary>
        [Server]
        public void Restore(IReadOnlyList<AchievementSaveData> saved)
        {
            if (_tracker == null)
            {
                AchievementRegistry.LoadAllFromResources();
                _tracker = new AchievementTracker(_record, AchievementRegistry.All());
            }

            _tracker.Load(saved);
            GrantPending();
        }

        /// <summary>Server: the flat entries the player save carries.</summary>
        [Server]
        public List<AchievementSaveData> CaptureSave()
            => _tracker != null ? _tracker.ToSave() : new List<AchievementSaveData>();

        private void OnLevelChanged() => Record(AchievementTrigger.Level, 1);

        private void OnQuestCompleted(QuestData quest) => Record(AchievementTrigger.Quest, 1);

        private void GrantPending()
        {
            List<AchievementData> pending = _tracker.PendingRewards();
            for (int i = 0; i < pending.Count; i++)
                Grant(pending[i]);
        }

        private bool Grant(AchievementData definition)
        {
            if (definition == null || _tracker == null)
                return false;

            string id = definition.AchievementId;
            if (_tracker.IsGranted(id))
                return true;

            // Check both reward sinks before touching either, so a failed grant leaves nothing
            // half-paid and a retry cannot duplicate the half that succeeded.
            if (definition.RewardGold > 0 && _wallet == null)
                return false;

            if (definition.RewardItem != null && _inventory == null)
                return false;

            if (definition.RewardItem != null && !_inventory.TryAddItem(definition.RewardItem))
                return false;

            if (definition.RewardGold > 0)
                _wallet.AddGold(definition.RewardGold);

            _tracker.MarkGranted(id);

            if (IsClientStarted && Owner != null)
                TargetRpcCompleted(Owner, id);

            AchievementCompleted?.Invoke(definition);
            return true;
        }

        [TargetRpc]
        private void TargetRpcCompleted(NetworkConnection conn, string achievementId)
        {
            AchievementData definition = AchievementRegistry.Resolve(achievementId);
            if (definition != null)
                AchievementCompleted?.Invoke(definition);
        }
    }
}
