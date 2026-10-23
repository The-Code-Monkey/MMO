using System;
using System.Collections.Generic;
using System.Linq;

namespace MMO.Content
{
    /// <summary>
    /// Pure quest rules. Progress is a count per objective; the server shell owns the state and
    /// calls these decisions with plain values.
    /// </summary>
    public static class QuestRules
    {
        /// <summary>True when every prerequisite quest is already complete.</summary>
        public static bool IsAvailable(QuestData quest, IReadOnlyCollection<string> completedQuestIds)
        {
            if (quest == null)
                return false;

            IReadOnlyList<string> prerequisites = quest.PrerequisiteQuestIds;
            if (prerequisites == null || prerequisites.Count == 0)
                return true;

            if (completedQuestIds == null || completedQuestIds.Count == 0)
                return false;

            for (int i = 0; i < prerequisites.Count; i++)
            {
                if (!completedQuestIds.Contains(prerequisites[i]))
                    return false;
            }

            return true;
        }

        /// <summary>Adds progress, never goes below zero, and never exceeds the required count.</summary>
        public static int Advance(int current, QuestObjective objective, int amount = 1)
        {
            int required = Math.Max(0, objective.RequiredCount);
            int next = Math.Max(0, current) + Math.Max(0, amount);
            return Math.Min(required, next);
        }

        /// <summary>True when <paramref name="current"/> has reached the objective's requirement.</summary>
        public static bool IsObjectiveComplete(int current, QuestObjective objective)
        {
            return current >= Math.Max(0, objective.RequiredCount);
        }

        /// <summary>
        /// True when every objective has reached its required count. An empty quest is not
        /// considered complete: a quest nobody has to do must not reward itself.
        /// </summary>
        public static bool IsComplete(QuestData quest, IReadOnlyList<int> counts)
        {
            if (quest == null || counts == null)
                return false;

            IReadOnlyList<QuestObjective> objectives = quest.Objectives;
            if (objectives.Count == 0 || counts.Count < objectives.Count)
                return false;

            for (int i = 0; i < objectives.Count; i++)
            {
                if (!IsObjectiveComplete(counts[i], objectives[i]))
                    return false;
            }

            return true;
        }
    }
}
