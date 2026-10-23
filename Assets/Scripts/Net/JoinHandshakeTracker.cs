using System.Collections.Generic;
using System.Threading;

namespace MMO.Net
{
    public sealed class JoinHandshakeTracker
    {
        private static readonly List<int> _empty = new();
        private readonly Dictionary<int, double> _startedAt = new();
        private readonly Dictionary<int, CancellationTokenSource> _cts = new();

        public int Count => _startedAt.Count;
        public bool IsPending(int clientId) => _startedAt.ContainsKey(clientId);

        public void Begin(int clientId, double now)
        {
            _startedAt[clientId] = now;

            if (!_cts.TryGetValue(clientId, out CancellationTokenSource cts) || cts.IsCancellationRequested)
                _cts[clientId] = new CancellationTokenSource();
        }

        public bool End(int clientId)
        {
            bool removed = _startedAt.Remove(clientId);

            if (_cts.TryGetValue(clientId, out CancellationTokenSource cts))
            {
                _cts.Remove(clientId);
                cts.Cancel();
                cts.Dispose();
            }

            return removed;
        }

        /// <summary>
        /// A token that fires when this handshake ends (timed out, disconnected or already decided).
        /// An async join-decision task should treat it as "stop chasing this client".
        /// </summary>
        public CancellationToken TokenFor(int clientId)
            => _cts.TryGetValue(clientId, out CancellationTokenSource cts) ? cts.Token : CancellationToken.None;

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

            return expired ?? _empty;
        }
    }
}
