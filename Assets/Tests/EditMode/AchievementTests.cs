using System.Collections.Generic;
using MMO.Persistence;
using MMO.Progression;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MMO.Tests
{
    /// <summary>
    /// Achievements as data and arithmetic: the clamping curve, the grant-once guard, the tracker
    /// that applies server events, and the flat save round-trip. No player or socket is needed.
    /// </summary>
    public sealed class AchievementTests
    {
        private readonly List<AchievementData> _created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (AchievementData achievement in _created)
            {
                if (achievement != null)
                    Object.DestroyImmediate(achievement);
            }

            _created.Clear();
            AchievementRegistry.Clear();
        }

        [Test]
        public void Progress_ClampsAtThreshold()
        {
            AchievementProgress progress = AchievementRules.Progress(3, 2, 5);

            Assert.IsTrue(progress.Completed);
            Assert.AreEqual(3, progress.Value, "An overshoot clamps instead of growing forever.");
        }

        [Test]
        public void Progress_ZeroIncrementLeavesTheCounterAlone()
        {
            AchievementProgress progress = AchievementRules.Progress(3, 1, 0);

            Assert.IsFalse(progress.Completed);
            Assert.AreEqual(1, progress.Value);
        }

        [Test]
        public void Progress_NegativeIncrementCannotRewindProgress()
        {
            AchievementProgress progress = AchievementRules.Progress(3, 2, -5);

            Assert.IsFalse(progress.Completed);
            Assert.AreEqual(2, progress.Value, "A malformed event must not reset progress.");
        }

        [Test]
        public void Progress_NonPositiveThresholdReadsAsOne()
        {
            AchievementProgress progress = AchievementRules.Progress(0, 0, 1);

            Assert.IsTrue(progress.Completed);
            Assert.AreEqual(1, progress.Value);
        }

        [Test]
        public void ShouldGrant_OnlyOnTheTransition()
        {
            Assert.IsTrue(AchievementRules.ShouldGrant(false, true));
            Assert.IsFalse(AchievementRules.ShouldGrant(true, true));
            Assert.IsFalse(AchievementRules.ShouldGrant(false, false));
        }

        [Test]
        public void RecordEvent_CompletesOnceEvenWhenReplayed()
        {
            var tracker = new AchievementTracker(new AchievementRecord(), new[]
            {
                NewAchievement("gather-10", AchievementTrigger.Gather, threshold: 1),
            });

            List<AchievementData> first = tracker.RecordEvent(AchievementTrigger.Gather, 1);
            List<AchievementData> replay = tracker.RecordEvent(AchievementTrigger.Gather, 1);

            Assert.AreEqual(1, first.Count);
            Assert.IsEmpty(replay, "A replayed event must not re-complete or re-pay.");
            Assert.IsTrue(tracker.IsCompleted("gather-10"));
        }

        [Test]
        public void RecordEvent_IgnoresOtherTriggers()
        {
            var tracker = new AchievementTracker(new AchievementRecord(), new[]
            {
                NewAchievement("craft-5", AchievementTrigger.Craft, threshold: 1),
            });

            Assert.IsEmpty(tracker.RecordEvent(AchievementTrigger.Gather, 99));
            Assert.IsFalse(tracker.IsCompleted("craft-5"));
        }

        [Test]
        public void PendingRewards_StayUntilGranted()
        {
            var tracker = new AchievementTracker(new AchievementRecord(), new[]
            {
                NewAchievement("quest-1", AchievementTrigger.Quest, threshold: 1, gold: 25),
            });

            tracker.RecordEvent(AchievementTrigger.Quest, 1);
            Assert.AreEqual(1, tracker.PendingRewards().Count);

            tracker.MarkGranted("quest-1");
            Assert.IsEmpty(tracker.PendingRewards());
            Assert.IsTrue(tracker.IsGranted("quest-1"));
        }

        [Test]
        public void MarkGranted_DoesNothingForAnIncompleteAchievement()
        {
            var tracker = new AchievementTracker(new AchievementRecord(), new[]
            {
                NewAchievement("quest-2", AchievementTrigger.Quest, threshold: 3),
            });

            tracker.MarkGranted("quest-2");

            Assert.IsFalse(tracker.IsGranted("quest-2"));
        }

        [Test]
        public void Save_RoundTripsProgressCompletionAndGrant()
        {
            var tracker = new AchievementTracker(new AchievementRecord(), new[]
            {
                NewAchievement("level-2", AchievementTrigger.Level, threshold: 2, gold: 10),
                NewAchievement("gather-9", AchievementTrigger.Gather, threshold: 9),
            });

            // One achievement completed and paid, one still part-way: a save has to carry both,
            // or a reload either re-pays a reward or forgets where the player had got to.
            tracker.RecordEvent(AchievementTrigger.Level, 2);
            tracker.MarkGranted("level-2");
            tracker.RecordEvent(AchievementTrigger.Gather, 4);

            List<AchievementSaveData> saved = tracker.ToSave();
            var restored = new AchievementTracker(new AchievementRecord(), new[]
            {
                NewAchievement("level-2", AchievementTrigger.Level, threshold: 2, gold: 10),
                NewAchievement("gather-9", AchievementTrigger.Gather, threshold: 9),
            });
            restored.Load(saved);

            Assert.AreEqual(2, restored.Record.CountOf("level-2"));
            Assert.IsTrue(restored.IsCompleted("level-2"));
            Assert.IsTrue(restored.IsGranted("level-2"));
            Assert.AreEqual(4, restored.Record.CountOf("gather-9"));
            Assert.IsFalse(restored.IsCompleted("gather-9"),
                "A half-done achievement must not come back completed.");
        }

        [Test]
        public void Load_IgnoresEntriesForRemovedDefinitions()
        {
            var tracker = new AchievementTracker(new AchievementRecord(), new AchievementData[0]);

            tracker.Load(new List<AchievementSaveData>
            {
                new AchievementSaveData
                {
                    AchievementId = "retired",
                    Progress = 5,
                    Completed = true,
                    Granted = false,
                },
            });

            Assert.AreEqual(5, tracker.Record.CountOf("retired"));
            Assert.IsEmpty(tracker.PendingRewards(),
                "A retired definition is kept in the record but can never pay out.");
        }

        [Test]
        public void Registry_ResolvesAndClears()
        {
            AchievementData achievement = NewAchievement("registry-1", AchievementTrigger.Level, 1);
            AchievementRegistry.Register(achievement);

            Assert.AreSame(achievement, AchievementRegistry.Resolve("registry-1"));
            Assert.IsNull(AchievementRegistry.Resolve("missing"));

            AchievementRegistry.Clear();
            Assert.IsNull(AchievementRegistry.Resolve("registry-1"));
        }

        private AchievementData NewAchievement(
            string id, AchievementTrigger trigger, int threshold, int gold = 0)
        {
            var achievement = ScriptableObject.CreateInstance<AchievementData>();
            var serialized = new SerializedObject(achievement);
            serialized.FindProperty("_achievementId").stringValue = id;
            serialized.FindProperty("_title").stringValue = id;
            serialized.FindProperty("_trigger").intValue = (int)trigger;
            serialized.FindProperty("_threshold").intValue = threshold;
            serialized.FindProperty("_rewardGold").intValue = gold;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            AchievementRegistry.Register(achievement);
            _created.Add(achievement);
            return achievement;
        }
    }
}
