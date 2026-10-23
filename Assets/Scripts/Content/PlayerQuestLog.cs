using System;
using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using MMO.Gameplay;
using MMO.Inventory;
using UnityEngine;
using Random = System.Random;

namespace MMO.Content
{
    /// <summary>
    /// A player's server-owned quest state: one progress row per objective and the completed quest
    /// ids. Progress is replicated; rewards are granted only on the server and only once.
    /// </summary>
    public sealed class PlayerQuestLog : NetworkBehaviour, IQuestKillSink
    {
        private readonly SyncList<QuestProgressEntry> _progress = new();
        private readonly SyncList<string> _completedQuestIds = new();

        private PlayerStats _stats;
        private PlayerInventory _inventory;
        private Random _rng = new();
        private bool _refreshing;
        private bool _refreshQueued;

        public IReadOnlyList<QuestProgressEntry> Progress => _progress;
        public IReadOnlyList<string> CompletedQuestIds => _completedQuestIds;

        /// <summary>Raised on either side when progress or the completed set changes.</summary>
        public event Action ProgressChanged;

        /// <summary>Raised on the server when a quest is completed.</summary>
        public event Action<QuestData> QuestCompleted;

        private PlayerStats Stats => _stats != null ? _stats : _stats = GetComponent<PlayerStats>();
        private PlayerInventory Inventory => _inventory != null ? _inventory : _inventory = GetComponent<PlayerInventory>();

        public override void OnStartNetwork()
        {
            _progress.OnChange += OnProgressChanged;
            _completedQuestIds.OnChange += OnCompletedChanged;

            if (IsServer && Inventory != null)
                Inventory.ContainersChanged += OnInventoryChanged;
        }

        public override void OnStopNetwork()
        {
            _progress.OnChange -= OnProgressChanged;
            _completedQuestIds.OnChange -= OnCompletedChanged;

            if (_inventory != null)
                _inventory.ContainersChanged -= OnInventoryChanged;
        }

        /// <summary>True when the quest has progress rows and is not completed yet.</summary>
        public bool IsActive(string questId)
        {
            if (string.IsNullOrEmpty(questId) || IsCompleted(questId))
                return false;

            for (int i = 0; i < _progress.Count; i++)
            {
                if (_progress[i].QuestId == questId)
                    return true;
            }

            return false;
        }

        public bool IsCompleted(string questId)
        {
            if (string.IsNullOrEmpty(questId))
                return false;

            for (int i = 0; i < _completedQuestIds.Count; i++)
            {
                if (_completedQuestIds[i] == questId)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Server: start a quest when it exists, has objectives, is not already active/completed,
        /// and every prerequisite is complete.
        /// </summary>
        [Server]
        public bool TryStartQuest(QuestData quest)
        {
            if (quest == null || string.IsNullOrEmpty(quest.QuestId) || quest.Objectives.Count == 0)
                return false;

            if (IsActive(quest.QuestId) || IsCompleted(quest.QuestId))
                return false;

            QuestRegistry.Register(quest);

            if (!QuestRules.IsAvailable(quest, CompletedSnapshot()))
                return false;

            IReadOnlyList<QuestObjective> objectives = quest.Objectives;
            for (int i = 0; i < objectives.Count; i++)
            {
                QuestObjective objective = objectives[i];
                _progress.Add(new QuestProgressEntry
                {
                    QuestId = quest.QuestId,
                    Kind = objective.Kind,
                    TargetName = objective.TargetName,
                    Count = 0,
                });
            }

            RefreshCollectObjectives(Inventory);
            return true;
        }

        /// <summary>
        /// Server: complete and reward a quest once every objective is met. Marking the completed
        /// set first is what makes a repeated kill/interact/turn-in unable to double-reward.
        /// </summary>
        [Server]
        public bool TryComplete(QuestData quest)
        {
            if (quest == null || string.IsNullOrEmpty(quest.QuestId))
                return false;

            if (IsCompleted(quest.QuestId) || !IsActive(quest.QuestId))
                return false;

            if (!TryBuildCounts(quest, out List<int> counts) || !QuestRules.IsComplete(quest, counts))
                return false;

            _completedQuestIds.Add(quest.QuestId);

            Stats?.AddExperience(quest.RewardXp);

            if (quest.RewardTable != null)
            {
                List<LootDrop> drops = LootRules.Roll(quest.RewardTable, _rng);
                LootAwardResult result = LootAwarder.Award(
                    drops, Inventory, NetworkManager, transform.position, preferBag: false);

                if (result.Leftovers.Count > 0)
                {
                    Debug.LogError(
                        $"{nameof(PlayerQuestLog)}: quest '{quest.QuestId}' could not place " +
                        $"{result.Leftovers.Count} reward item(s).");
                }
            }

            QuestCompleted?.Invoke(quest);
            return true;
        }

        // IQuestKillSink: NpcLoot calls this on the killer's log.
        public void NotifyKill(string targetName)
        {
            if (!IsServer)
                return;

            AdvanceObjective(QuestObjectiveKind.Kill, targetName);
        }

        public void NotifyInteract(string targetName)
        {
            if (!IsServer)
                return;

            AdvanceObjective(QuestObjectiveKind.Interact, targetName);
        }

        /// <summary>
        /// Server: refresh every collect objective from the bag's current contents. Re-entrant
        /// reward loot changes the bag, so a queued second pass is deferred rather than recursed.
        /// </summary>
        [Server]
        public void RefreshCollectObjectives(PlayerInventory inventory)
        {
            if (_refreshing)
            {
                _refreshQueued = true;
                return;
            }

            _refreshing = true;
            try
            {
                var affected = new HashSet<string>();

                for (int i = 0; i < _progress.Count; i++)
                {
                    QuestProgressEntry entry = _progress[i];
                    if (entry.Kind != QuestObjectiveKind.Collect || IsCompleted(entry.QuestId))
                        continue;

                    QuestData quest = QuestRegistry.Resolve(entry.QuestId);
                    if (!TryFindObjective(quest, entry.Kind, entry.TargetName, out QuestObjective objective))
                        continue;

                    int current = inventory != null ? inventory.CountItem(entry.TargetName) : 0;
                    int next = Mathf.Clamp(current, 0, Mathf.Max(0, objective.RequiredCount));
                    if (next == entry.Count)
                        continue;

                    entry.Count = next;
                    _progress.Set(i, entry);
                    affected.Add(entry.QuestId);
                }

                foreach (string questId in affected)
                    TryComplete(QuestRegistry.Resolve(questId));
            }
            finally
            {
                _refreshing = false;
            }

            if (_refreshQueued)
            {
                _refreshQueued = false;
                RefreshCollectObjectives(inventory);
            }
        }

        /// <summary>Server: replace the live state from a save, then re-evaluate collect goals.</summary>
        [Server]
        public void Restore(
            IReadOnlyList<string> completedQuestIds,
            IReadOnlyList<QuestProgressEntry> progress)
        {
            _progress.Clear();
            _completedQuestIds.Clear();

            if (completedQuestIds != null)
            {
                for (int i = 0; i < completedQuestIds.Count; i++)
                {
                    if (!string.IsNullOrEmpty(completedQuestIds[i]))
                        _completedQuestIds.Add(completedQuestIds[i]);
                }
            }

            if (progress != null)
            {
                for (int i = 0; i < progress.Count; i++)
                {
                    if (!string.IsNullOrEmpty(progress[i].QuestId))
                        _progress.Add(progress[i]);
                }
            }

            RefreshCollectObjectives(Inventory);
        }

        private void AdvanceObjective(QuestObjectiveKind kind, string targetName)
        {
            if (string.IsNullOrEmpty(targetName))
                return;

            var affected = new HashSet<string>();

            for (int i = 0; i < _progress.Count; i++)
            {
                QuestProgressEntry entry = _progress[i];
                if (entry.Kind != kind || entry.TargetName != targetName || IsCompleted(entry.QuestId))
                    continue;

                QuestData quest = QuestRegistry.Resolve(entry.QuestId);
                if (!TryFindObjective(quest, kind, targetName, out QuestObjective objective))
                    continue;

                int next = QuestRules.Advance(entry.Count, objective);
                if (next == entry.Count)
                    continue;

                entry.Count = next;
                _progress.Set(i, entry);
                affected.Add(entry.QuestId);
            }

            foreach (string questId in affected)
                TryComplete(QuestRegistry.Resolve(questId));
        }

        private bool TryBuildCounts(QuestData quest, out List<int> counts)
        {
            IReadOnlyList<QuestObjective> objectives = quest.Objectives;
            counts = new List<int>(objectives.Count);

            for (int i = 0; i < objectives.Count; i++)
            {
                QuestObjective objective = objectives[i];
                if (!TryFindProgress(quest.QuestId, objective.Kind, objective.TargetName, out int count))
                    return false;

                counts.Add(count);
            }

            return true;
        }

        private bool TryFindProgress(
            string questId,
            QuestObjectiveKind kind,
            string targetName,
            out int count)
        {
            for (int i = 0; i < _progress.Count; i++)
            {
                QuestProgressEntry entry = _progress[i];
                if (entry.QuestId == questId && entry.Kind == kind && entry.TargetName == targetName)
                {
                    count = entry.Count;
                    return true;
                }
            }

            count = 0;
            return false;
        }

        private static bool TryFindObjective(
            QuestData quest,
            QuestObjectiveKind kind,
            string targetName,
            out QuestObjective objective)
        {
            objective = default;
            if (quest == null)
                return false;

            IReadOnlyList<QuestObjective> objectives = quest.Objectives;
            for (int i = 0; i < objectives.Count; i++)
            {
                if (objectives[i].Kind == kind && objectives[i].TargetName == targetName)
                {
                    objective = objectives[i];
                    return true;
                }
            }

            return false;
        }

        private List<string> CompletedSnapshot()
        {
            var completed = new List<string>(_completedQuestIds.Count);
            for (int i = 0; i < _completedQuestIds.Count; i++)
                completed.Add(_completedQuestIds[i]);

            return completed;
        }

        private void OnInventoryChanged()
        {
            if (IsServer)
                RefreshCollectObjectives(Inventory);
        }

        private void OnProgressChanged(
            SyncListOperation operation,
            int index,
            QuestProgressEntry oldItem,
            QuestProgressEntry newItem,
            bool asServer)
        {
            ProgressChanged?.Invoke();
        }

        private void OnCompletedChanged(
            SyncListOperation operation,
            int index,
            string oldItem,
            string newItem,
            bool asServer)
        {
            ProgressChanged?.Invoke();
        }
    }
}
