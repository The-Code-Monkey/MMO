using System.Collections.Generic;

namespace MMO.Networking
{
    public sealed class JoinHandshakeTracker
    {
        private readonly Dictionary<int, float> _pendingAddedAt = new();

        public float TimeoutSeconds { get; }
        public int PendingCount => _pendingAddedAt.Count;

        public JoinHandshakeTracker(float timeoutSeconds)
        {
            TimeoutSeconds = timeoutSeconds;
        }

        public void AddPending(int connectionKey, float addedAt)
        {
            _pendingAddedAt[connectionKey] = addedAt;
        }

        public bool IsPending(int connectionKey) => _pendingAddedAt.ContainsKey(connectionKey);

        public bool TryRemove(int connectionKey) => _pendingAddedAt.Remove(connectionKey);

        public bool IsExpired(int connectionKey, float now)
        {
            return _pendingAddedAt.TryGetValue(connectionKey, out float addedAt)
                && now - addedAt >= TimeoutSeconds;
        }

        public IReadOnlyList<int> CollectExpired(float now)
        {
            var expired = new List<int>();
            foreach (var entry in _pendingAddedAt)
            {
                if (now - entry.Value >= TimeoutSeconds)
                    expired.Add(entry.Key);
            }
            return expired;
        }
    }
}
