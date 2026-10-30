using System.Collections.Generic;

namespace MMO.Client
{
    /// <summary>What the budget needs to know about a sound asking for a voice.</summary>
    public readonly struct VoiceRequest
    {
        public VoiceRequest(float distanceSquared, int priority, float volume)
        {
            DistanceSquared = distanceSquared;
            Priority = priority;
            Volume = volume;
        }

        /// <summary>Squared distance from the listener; closer wins when priorities tie.</summary>
        public float DistanceSquared { get; }

        /// <summary>Higher wins. A local player's own impact should outrank a distant ally's.</summary>
        public int Priority { get; }

        /// <summary>Requested linear volume; louder wins when distance and priority tie.</summary>
        public float Volume { get; }
    }

    /// <summary>
    /// Chooses which simultaneous sounds play under a hard voice cap.
    ///
    /// Pure: the audio device only ever asks "which indices survive?", so the rule that stops a
    /// 50-player fight from stacking 200 impacts is tested without speakers. Ordering is
    /// priority, then nearest, then loudest, then stable index for ties.
    /// </summary>
    public static class AudioVoiceBudget
    {
        /// <summary>
        /// Indices of the voices that should play, at most <paramref name="maxVoices"/>.
        /// A non-positive cap selects nothing.
        /// </summary>
        public static IReadOnlyList<int> Select(IReadOnlyList<VoiceRequest> voices, int maxVoices)
        {
            var selected = new List<int>();
            if (voices == null || voices.Count == 0 || maxVoices <= 0)
                return selected;

            var order = new List<int>(voices.Count);
            for (int i = 0; i < voices.Count; i++)
                order.Add(i);

            order.Sort((left, right) =>
            {
                VoiceRequest a = voices[left];
                VoiceRequest b = voices[right];

                int byPriority = b.Priority.CompareTo(a.Priority);
                if (byPriority != 0)
                    return byPriority;

                int byDistance = a.DistanceSquared.CompareTo(b.DistanceSquared);
                if (byDistance != 0)
                    return byDistance;

                int byVolume = b.Volume.CompareTo(a.Volume);
                if (byVolume != 0)
                    return byVolume;

                return left.CompareTo(right);
            });

            int count = maxVoices < order.Count ? maxVoices : order.Count;
            for (int i = 0; i < count; i++)
                selected.Add(order[i]);

            return selected;
        }

        /// <summary>True when <paramref name="index"/> is one of the selected voices.</summary>
        public static bool Contains(IReadOnlyList<int> selected, int index)
        {
            if (selected == null)
                return false;

            for (int i = 0; i < selected.Count; i++)
            {
                if (selected[i] == index)
                    return true;
            }

            return false;
        }
    }
}
