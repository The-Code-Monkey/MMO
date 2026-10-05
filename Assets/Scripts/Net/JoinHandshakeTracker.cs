using System.Collections.Generic;

namespace MMO.Net
{
    public sealed class JoinHandshakeTracker
    {
        private readonly Dictionary<int, double> _startedAt = new();

        public int Count => _startedAt.Count;
        public bool IsPending(int clientId) => _startedAt.ContainsKey(clientId);

        public void Begin(int clientId, double now) => _startedAt[clientId] = now;

        public bool End(int clientId) => _startedAt.Remove(clientId);

        public float Elapsed(int clientId, double now)
            => _startedAt.TryGetValue(clientId, out double started) ? (float)(now - started) : 0f;

        public List<int> Expired(double now, double timeoutSeconds)
        {
            List<int> expired = null;
            foreach (KeyValuePair<int, double> kvp in _startedAt)
            {
                if (now - kvp.Value <= timeoutSeconds)
                    continue;

                (expired ??= new List<int>()).Add(kvp.Key);
            }

            return expired ?? new List<int>();
        }
    }
}
