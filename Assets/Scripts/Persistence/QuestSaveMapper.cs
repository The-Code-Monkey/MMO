using System.Collections.Generic;
using MMO.Content;
using UnityEngine;

namespace MMO.Persistence
{
    /// <summary>Maps quest completion and progress between a save record and live values.</summary>
    internal static class QuestSaveMapper
    {
        public static void CaptureInto(
            List<string> completedTarget,
            List<QuestProgressEntry> progressTarget,
            IReadOnlyList<string> completedQuestIds,
            IReadOnlyList<QuestProgressEntry> questProgress)
        {
            if (completedQuestIds != null)
            {
                for (int i = 0; i < completedQuestIds.Count; i++)
                {
                    if (!string.IsNullOrEmpty(completedQuestIds[i]))
                        completedTarget.Add(completedQuestIds[i]);
                }
            }

            if (questProgress != null)
            {
                for (int i = 0; i < questProgress.Count; i++)
                {
                    QuestProgressEntry entry = questProgress[i];
                    if (string.IsNullOrEmpty(entry.QuestId))
                        continue;

                    entry.Count = Mathf.Max(0, entry.Count);
                    progressTarget.Add(entry);
                }
            }
        }

        public static void Apply(
            PlayerSaveRecord record,
            out List<string> completedQuestIds,
            out List<QuestProgressEntry> questProgress)
        {
            completedQuestIds = new List<string>();
            questProgress = new List<QuestProgressEntry>();

            if (record?.CompletedQuestIds != null)
            {
                for (int i = 0; i < record.CompletedQuestIds.Count; i++)
                {
                    if (!string.IsNullOrEmpty(record.CompletedQuestIds[i]))
                        completedQuestIds.Add(record.CompletedQuestIds[i]);
                }
            }

            if (record?.QuestProgress != null)
            {
                for (int i = 0; i < record.QuestProgress.Count; i++)
                {
                    QuestProgressEntry entry = record.QuestProgress[i];
                    if (string.IsNullOrEmpty(entry.QuestId))
                        continue;

                    entry.Count = Mathf.Max(0, entry.Count);
                    questProgress.Add(entry);
                }
            }
        }
    }
}
