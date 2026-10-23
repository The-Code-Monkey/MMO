using System.Collections.Generic;
using UnityEngine;

namespace MMO.Content
{
    /// <summary>
    /// Resolves a persisted quest id back to its <see cref="QuestData"/> asset. Mirrors
    /// <see cref="MMO.Crafting.RecipeRegistry"/>: self-registration on load plus a
    /// <c>Resources</c> fallback for shipped quests.
    /// </summary>
    public static class QuestRegistry
    {
        public const string QuestsFolder = "Content/Quests";

        private static readonly Dictionary<string, QuestData> Registered = new();

        public static int Count => Registered.Count;

        public static void Register(QuestData quest)
        {
            if (quest == null || string.IsNullOrEmpty(quest.QuestId))
                return;

            if (Registered.TryGetValue(quest.QuestId, out QuestData existing) && existing != quest)
            {
                Debug.LogWarning(
                    $"{nameof(QuestRegistry)}: '{quest.QuestId}' is already registered as " +
                    $"'{(existing != null ? existing.name : "<destroyed>")}'; replacing it with '{quest.name}'.");
            }

            Registered[quest.QuestId] = quest;
        }

        public static void Unregister(QuestData quest)
        {
            if (quest == null || string.IsNullOrEmpty(quest.QuestId))
                return;

            if (Registered.TryGetValue(quest.QuestId, out QuestData existing) && existing == quest)
                Registered.Remove(quest.QuestId);
        }

        public static QuestData Resolve(string questId)
        {
            if (string.IsNullOrEmpty(questId))
                return null;

            if (Registered.TryGetValue(questId, out QuestData quest) && quest != null)
                return quest;

            QuestData loaded = Resources.Load<QuestData>($"{QuestsFolder}/{questId}");
            if (loaded != null)
                Registered[questId] = loaded;

            return loaded;
        }

        public static void Clear() => Registered.Clear();

        /// <summary>A point-in-time snapshot of every registered quest, nulls skipped.</summary>
        public static IReadOnlyList<QuestData> All
        {
            get
            {
                var snapshot = new List<QuestData>(Registered.Count);
                foreach (KeyValuePair<string, QuestData> entry in Registered)
                {
                    if (entry.Value != null)
                        snapshot.Add(entry.Value);
                }

                return snapshot;
            }
        }

        public static void LoadAllFromResources()
        {
            foreach (QuestData quest in Resources.LoadAll<QuestData>(QuestsFolder))
                Register(quest);
        }
    }
}
