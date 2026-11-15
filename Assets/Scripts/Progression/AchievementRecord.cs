using System;
using System.Collections.Generic;

namespace MMO.Progression
{
    /// <summary>One achievement's counter in a player's record. A list keeps the document JsonUtility-serializable.</summary>
    [Serializable]
    public sealed class AchievementCounter
    {
        public string AchievementId;
        public int Value;
    }

    /// <summary>
    /// A player's achievement progress: the counters, the completed ids, and the ids whose reward
    /// has been paid. The last list is what makes the grant idempotent — a completion without a
    /// paid reward is retried, a paid one never is.
    /// </summary>
    [Serializable]
    public sealed class AchievementRecord
    {
        public List<AchievementCounter> Counters = new List<AchievementCounter>();
        public List<string> Completed = new List<string>();
        public List<string> Granted = new List<string>();

        public int CountOf(string achievementId)
        {
            if (string.IsNullOrEmpty(achievementId))
                return 0;

            for (int i = 0; i < Counters.Count; i++)
            {
                if (Counters[i] != null && Counters[i].AchievementId == achievementId)
                    return Counters[i].Value;
            }

            return 0;
        }

        public void SetCount(string achievementId, int value)
        {
            if (string.IsNullOrEmpty(achievementId))
                return;

            for (int i = 0; i < Counters.Count; i++)
            {
                if (Counters[i] != null && Counters[i].AchievementId == achievementId)
                {
                    Counters[i].Value = value;
                    return;
                }
            }

            Counters.Add(new AchievementCounter { AchievementId = achievementId, Value = value });
        }
    }
}
