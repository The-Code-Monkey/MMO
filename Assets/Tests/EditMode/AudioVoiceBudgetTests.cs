using System.Collections.Generic;
using MMO.Client;
using NUnit.Framework;

namespace MMO.Tests
{
    /// <summary>
    /// The voice cap is what stops a large fight from playing every impact at once. These tests
    /// pin the ordering: priority first, then nearest, then loudest.
    /// </summary>
    public sealed class AudioVoiceBudgetTests
    {
        [Test]
        public void Select_KeepsHighestPriority_AndDropsTheRest()
        {
            var voices = new[]
            {
                new VoiceRequest(distanceSquared: 1f, priority: 0, volume: 1f),
                new VoiceRequest(distanceSquared: 100f, priority: 5, volume: 1f),
                new VoiceRequest(distanceSquared: 4f, priority: 0, volume: 1f),
            };

            IReadOnlyList<int> selected = AudioVoiceBudget.Select(voices, maxVoices: 2);

            Assert.AreEqual(2, selected.Count);
            Assert.IsTrue(AudioVoiceBudget.Contains(selected, 1), "The priority-5 voice must survive.");
            Assert.IsTrue(AudioVoiceBudget.Contains(selected, 0), "The nearest low-priority voice must survive.");
            Assert.IsFalse(AudioVoiceBudget.Contains(selected, 2));
        }

        [Test]
        public void Select_EqualPriority_KeepsNearest()
        {
            var voices = new[]
            {
                new VoiceRequest(distanceSquared: 400f, priority: 0, volume: 1f),
                new VoiceRequest(distanceSquared: 4f, priority: 0, volume: 1f),
            };

            IReadOnlyList<int> selected = AudioVoiceBudget.Select(voices, maxVoices: 1);

            Assert.AreEqual(1, selected.Count);
            Assert.AreEqual(1, selected[0]);
        }

        [Test]
        public void Select_EqualPriorityAndDistance_KeepsLoudest()
        {
            var voices = new[]
            {
                new VoiceRequest(distanceSquared: 4f, priority: 0, volume: 0.3f),
                new VoiceRequest(distanceSquared: 4f, priority: 0, volume: 0.9f),
            };

            IReadOnlyList<int> selected = AudioVoiceBudget.Select(voices, maxVoices: 1);

            Assert.AreEqual(1, selected[0]);
        }

        [Test]
        public void Select_CompleteTie_IsStableByIndex()
        {
            var voices = new[]
            {
                new VoiceRequest(4f, 0, 1f),
                new VoiceRequest(4f, 0, 1f),
            };

            IReadOnlyList<int> selected = AudioVoiceBudget.Select(voices, maxVoices: 1);

            Assert.AreEqual(0, selected[0]);
        }

        [Test]
        public void Select_ZeroCap_SelectsNothing()
        {
            var voices = new[] { new VoiceRequest(1f, 0, 1f) };

            Assert.IsEmpty(AudioVoiceBudget.Select(voices, maxVoices: 0));
        }

        [Test]
        public void Select_NullOrEmpty_SelectsNothing()
        {
            Assert.IsEmpty(AudioVoiceBudget.Select(null, maxVoices: 4));
            Assert.IsEmpty(AudioVoiceBudget.Select(new List<VoiceRequest>(), maxVoices: 4));
        }

        [Test]
        public void Select_CapAboveCount_ReturnsEveryIndex()
        {
            var voices = new[]
            {
                new VoiceRequest(1f, 0, 1f),
                new VoiceRequest(2f, 0, 1f),
            };

            IReadOnlyList<int> selected = AudioVoiceBudget.Select(voices, maxVoices: 8);

            Assert.AreEqual(2, selected.Count);
        }
    }
}
