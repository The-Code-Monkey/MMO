using System;
using System.Collections.Generic;
using UnityEngine;

namespace MMO.Persistence
{
    /// <summary>
    /// Dirty-player queue with debounce windows and one-shot final snapshots. Kept free of
    /// repository I/O so the save coordinator can be driven with a fake clock.
    /// </summary>
    internal sealed class PlayerSaveQueue
    {
        private readonly HashSet<string> _dirty = new();
        private readonly Dictionary<string, double> _debounceUntil = new();
        private readonly Dictionary<string, PlayerSaveRecord> _finalRecords = new();

        public bool IsSaving { get; private set; }
        public int DirtyCount => _dirty.Count;

        public void MarkDirty(string accountId, bool immediate, double debounceSeconds)
        {
            if (string.IsNullOrEmpty(accountId))
                return;

            _dirty.Add(accountId);
            _debounceUntil[accountId] = immediate
                ? Time.unscaledTimeAsDouble
                : Time.unscaledTimeAsDouble + debounceSeconds;
        }

        public void QueueFinalSave(string accountId, PlayerSaveRecord record, double debounceSeconds)
        {
            if (string.IsNullOrEmpty(accountId) || record == null)
                return;

            _finalRecords[accountId] = record;
            MarkDirty(accountId, immediate: true, debounceSeconds);
        }

        public List<string> Due(double now)
        {
            List<string> due = null;
            foreach (string accountId in _dirty)
            {
                if (_debounceUntil.TryGetValue(accountId, out double deadline) && now >= deadline)
                    (due ??= new List<string>()).Add(accountId);
            }

            return due;
        }

        public List<string> SnapshotDirty() => new List<string>(_dirty);

        public bool HasFinalRecord(string accountId) => _finalRecords.ContainsKey(accountId);

        public bool TryTakeFinalRecord(string accountId, out PlayerSaveRecord record)
        {
            if (!_finalRecords.TryGetValue(accountId, out record))
                return false;

            _finalRecords.Remove(accountId);
            return true;
        }

        public void MarkSaved(string accountId)
        {
            _dirty.Remove(accountId);
            _debounceUntil.Remove(accountId);
        }

        public void RequireRetry(string accountId, double debounceSeconds)
        {
            _dirty.Add(accountId);
            _debounceUntil[accountId] = Time.unscaledTimeAsDouble + debounceSeconds;
        }

        public void BeginSave() => IsSaving = true;
        public void EndSave() => IsSaving = false;
    }
}
