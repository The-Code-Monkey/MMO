using System;
using System.Collections.Generic;

namespace MMO.Gameplay
{
    /// <summary>
    /// Pure threat table owned by each enemy: maps entity IDs to accumulated threat.
    /// Deterministic ordering keeps targeting stable — ties break by insertion order.
    /// </summary>
    public sealed class ThreatTable
    {
        private readonly Dictionary<string, float> _threat = new();
        private readonly List<string> _insertionOrder = new();

        /// <summary>Highest-threat living entity, or null if table is empty.</summary>
        public string CurrentTarget
        {
            get
            {
                if (_threat.Count == 0)
                    return null;

                float maxThreat = float.MinValue;
                string best = null;

                for (int i = 0; i < _insertionOrder.Count; i++)
                {
                    string id = _insertionOrder[i];
                    if (_threat.TryGetValue(id, out float value) && value > maxThreat)
                    {
                        maxThreat = value;
                        best = id;
                    }
                }

                return best;
            }
        }

        /// <summary>Adds threat for <paramref name="entityId"/>. Amounts below zero are clamped to zero.</summary>
        public void Add(string entityId, float amount)
        {
            if (string.IsNullOrEmpty(entityId))
                throw new ArgumentException("Entity ID cannot be null or empty", nameof(entityId));

            float clamped = Math.Max(0f, amount);
            if (!_threat.ContainsKey(entityId))
            {
                _insertionOrder.Add(entityId);
            }
            _threat[entityId] = _threat.GetValueOrDefault(entityId, 0f) + clamped;
        }

        /// <summary>
        /// Sets threat for <paramref name="entityId"/> to an explicit value (taunt floor).
        /// Used by taunt abilities to force the entity to the top of the table.
        /// </summary>
        public void Set(string entityId, float amount)
        {
            if (string.IsNullOrEmpty(entityId))
                throw new ArgumentException("Entity ID cannot be null or empty", nameof(entityId));

            if (!_threat.ContainsKey(entityId))
            {
                _insertionOrder.Add(entityId);
            }
            _threat[entityId] = Math.Max(0f, amount);
        }

        /// <summary>Removes <paramref name="entityId"/> from the table (death/despawn).</summary>
        public void Remove(string entityId)
        {
            if (string.IsNullOrEmpty(entityId))
                throw new ArgumentException("Entity ID cannot be null or empty", nameof(entityId));

            _threat.Remove(entityId);
            _insertionOrder.Remove(entityId);
        }

        /// <summary>Returns the threat value for <paramref name="entityId"/>, or 0 if absent.</summary>
        public float Get(string entityId)
        {
            if (string.IsNullOrEmpty(entityId))
                throw new ArgumentException("Entity ID cannot be null or empty", nameof(entityId));

            return _threat.GetValueOrDefault(entityId, 0f);
        }

        /// <summary>Clears all entries. Used when the enemy resets (e.g. returns to spawn).</summary>
        public void Clear()
        {
            _threat.Clear();
            _insertionOrder.Clear();
        }
    }
}