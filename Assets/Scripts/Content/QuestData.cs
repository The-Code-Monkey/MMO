using System;
using System.Collections.Generic;
using UnityEngine;

namespace MMO.Content
{
    /// <summary>What the player has to do for one quest objective.</summary>
    public enum QuestObjectiveKind
    {
        Kill = 0,
        Collect = 1,
        Interact = 2,
    }

    /// <summary>
    /// One objective on a <see cref="QuestData"/> asset. Public fields are the serialized shape;
    /// the shell never authors these at runtime.
    /// </summary>
    [Serializable]
    public struct QuestObjective
    {
        public QuestObjectiveKind Kind;
        public string TargetName;
        public int RequiredCount;
    }

    /// <summary>
    /// A quest definition: objectives, prerequisite quests and the rewards for completion. Like
    /// every other data asset in the project it carries no networking; the server reads the same
    /// asset the client does.
    /// </summary>
    [CreateAssetMenu(fileName = "New Quest Data", menuName = "MMO/Quest Data")]
    public sealed class QuestData : ScriptableObject
    {
        [SerializeField] private string _questId = string.Empty;
        [SerializeField] private string _title = string.Empty;
        [SerializeField] private QuestObjective[] _objectives = Array.Empty<QuestObjective>();
        [SerializeField] private string[] _prerequisiteQuestIds = Array.Empty<string>();
        [SerializeField, Min(0)] private int _rewardXp;
        [SerializeField] private WeightedLootTable _rewardTable;

        public string QuestId => _questId;
        public string Title => _title;
        public IReadOnlyList<QuestObjective> Objectives => _objectives ?? Array.Empty<QuestObjective>();
        public IReadOnlyList<string> PrerequisiteQuestIds => _prerequisiteQuestIds ?? Array.Empty<string>();
        public int RewardXp => Mathf.Max(0, _rewardXp);
        public WeightedLootTable RewardTable => _rewardTable;

        private void OnEnable()
        {
            if (!string.IsNullOrEmpty(_questId))
                QuestRegistry.Register(this);
        }

        private void OnDisable() => QuestRegistry.Unregister(this);
    }
}
