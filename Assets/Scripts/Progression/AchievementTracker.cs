using System.Collections.Generic;
using MMO.Persistence;

namespace MMO.Progression
{
    /// <summary>
    /// Applies server events to one <see cref="AchievementRecord"/> and reports which definitions
    /// completed. It holds no clock and touches no Unity object, so the whole progress/grant
    /// lifecycle can be driven from an EditMode test.
    ///
    /// <para>
    /// A definition that has been removed from content is ignored on load: its counter and
    /// completed id simply sit unused in the record, which is what lets content be retired
    /// without rewriting every player's save.
    /// </para>
    /// </summary>
    public sealed class AchievementTracker
    {
        private readonly AchievementRecord _record;
        private readonly IReadOnlyList<AchievementData> _definitions;

        public AchievementTracker(AchievementRecord record, IReadOnlyList<AchievementData> definitions)
        {
            _record = record ?? new AchievementRecord();
            _definitions = definitions ?? System.Array.Empty<AchievementData>();
        }

        public AchievementRecord Record => _record;

        public bool IsCompleted(string achievementId)
            => !string.IsNullOrEmpty(achievementId) && _record.Completed.Contains(achievementId);

        public bool IsGranted(string achievementId)
            => !string.IsNullOrEmpty(achievementId) && _record.Granted.Contains(achievementId);

        /// <summary>
        /// Applies one event to every definition of <paramref name="trigger"/> and returns the
        /// ones that completed on this call (not those already complete).
        /// </summary>
        public List<AchievementData> RecordEvent(AchievementTrigger trigger, int amount)
        {
            var completed = new List<AchievementData>();

            for (int i = 0; i < _definitions.Count; i++)
            {
                AchievementData definition = _definitions[i];
                if (definition == null || definition.Trigger != trigger)
                    continue;

                string id = definition.AchievementId;
                int current = _record.CountOf(id);
                AchievementProgress progress = AchievementRules.Progress(
                    definition.Threshold, current, amount);

                _record.SetCount(id, progress.Value);
                if (!AchievementRules.ShouldGrant(IsCompleted(id), progress.Completed))
                    continue;

                _record.Completed.Add(id);
                completed.Add(definition);
            }

            return completed;
        }

        /// <summary>
        /// Completed definitions whose reward is still unpaid, in definition order. This is the
        /// retry list after a full bag: completion stands, the grant waits.
        /// </summary>
        public List<AchievementData> PendingRewards()
        {
            var pending = new List<AchievementData>();
            for (int i = 0; i < _definitions.Count; i++)
            {
                AchievementData definition = _definitions[i];
                if (definition != null && IsCompleted(definition.AchievementId)
                    && !IsGranted(definition.AchievementId))
                {
                    pending.Add(definition);
                }
            }

            return pending;
        }

        /// <summary>Marks an achievement's reward as paid; a replay will never pay it again.</summary>
        public void MarkGranted(string achievementId)
        {
            if (string.IsNullOrEmpty(achievementId) || IsGranted(achievementId))
                return;

            if (IsCompleted(achievementId))
                _record.Granted.Add(achievementId);
        }

        /// <summary>Rebuilds the runtime record from the persisted save entries.</summary>
        public void Load(IReadOnlyList<AchievementSaveData> saved)
        {
            _record.Counters.Clear();
            _record.Completed.Clear();
            _record.Granted.Clear();

            if (saved == null)
                return;

            for (int i = 0; i < saved.Count; i++)
            {
                AchievementSaveData entry = saved[i];
                if (string.IsNullOrEmpty(entry.AchievementId))
                    continue;

                _record.SetCount(entry.AchievementId, entry.Progress);
                if (entry.Completed && !IsCompleted(entry.AchievementId))
                    _record.Completed.Add(entry.AchievementId);
                if (entry.Granted && !IsGranted(entry.AchievementId))
                    _record.Granted.Add(entry.AchievementId);
            }
        }

        /// <summary>The record as the flat save entries the player record carries.</summary>
        public List<AchievementSaveData> ToSave()
        {
            var saved = new List<AchievementSaveData>(_record.Counters.Count);
            for (int i = 0; i < _record.Counters.Count; i++)
            {
                AchievementCounter counter = _record.Counters[i];
                if (counter == null || string.IsNullOrEmpty(counter.AchievementId))
                    continue;

                saved.Add(new AchievementSaveData
                {
                    AchievementId = counter.AchievementId,
                    Progress = counter.Value,
                    Completed = IsCompleted(counter.AchievementId),
                    Granted = IsGranted(counter.AchievementId),
                });
            }

            return saved;
        }
    }
}
