using System.Collections.Generic;
using UnityEngine;

namespace MMO.Progression
{
    /// <summary>
    /// Resolves an achievement id back to its <see cref="AchievementData"/> asset. Mirrors
    /// <see cref="MMO.Content.QuestRegistry"/>: self-registration on load plus a
    /// <c>Resources</c> fallback for shipped content.
    /// </summary>
    public static class AchievementRegistry
    {
        /// <summary>The <c>Resources</c> subfolder the shipped achievements live under.</summary>
        public const string AchievementsFolder = "Content/Achievements";

        private static readonly Dictionary<string, AchievementData> Registered = new();

        public static int Count => Registered.Count;

        public static void Register(AchievementData achievement)
        {
            if (achievement == null || string.IsNullOrEmpty(achievement.AchievementId))
                return;

            if (Registered.TryGetValue(achievement.AchievementId, out AchievementData existing)
                && existing != achievement)
            {
                Debug.LogWarning(
                    $"{nameof(AchievementRegistry)}: '{achievement.AchievementId}' is already registered as " +
                    $"'{(existing != null ? existing.name : "<destroyed>")}'; replacing it with '{achievement.name}'.");
            }

            Registered[achievement.AchievementId] = achievement;
        }

        public static void Unregister(AchievementData achievement)
        {
            if (achievement == null || string.IsNullOrEmpty(achievement.AchievementId))
                return;

            if (Registered.TryGetValue(achievement.AchievementId, out AchievementData existing)
                && existing == achievement)
            {
                Registered.Remove(achievement.AchievementId);
            }
        }

        /// <summary>The definition for <paramref name="achievementId"/>, or null when it no longer exists.</summary>
        public static AchievementData Resolve(string achievementId)
        {
            if (string.IsNullOrEmpty(achievementId))
                return null;

            if (Registered.TryGetValue(achievementId, out AchievementData achievement) && achievement != null)
                return achievement;

            AchievementData loaded = Resources.Load<AchievementData>(
                $"{AchievementsFolder}/{achievementId}");
            if (loaded != null)
                Registered[achievementId] = loaded;

            return loaded;
        }

        /// <summary>A point-in-time snapshot of every registered definition, nulls skipped.</summary>
        public static IReadOnlyList<AchievementData> All()
        {
            var all = new List<AchievementData>(Registered.Count);
            foreach (AchievementData achievement in Registered.Values)
            {
                if (achievement != null)
                    all.Add(achievement);
            }

            return all;
        }

        /// <summary>
        /// Forces every shipped achievement asset to load and register. Called when a player's
        /// achievement component starts: an asset nobody has referenced yet is not registered, and
        /// a tracker built from an empty registry would silently count nothing.
        /// </summary>
        public static void LoadAllFromResources()
        {
            foreach (AchievementData achievement in Resources.LoadAll<AchievementData>(AchievementsFolder))
                Register(achievement);
        }

        public static void Clear() => Registered.Clear();
    }
}
