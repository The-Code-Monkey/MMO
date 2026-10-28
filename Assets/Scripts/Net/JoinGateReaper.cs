using System;
using FishNet.Connection;
using FishNet.Managing;

namespace MMO.Net
{
    /// <summary>Expires handshakes that connected but never identified themselves.</summary>
    internal sealed class JoinGateReaper
    {
        private readonly JoinHandshakeTracker _tracker;
        private readonly Func<NetworkManager> _manager;
        private readonly Func<float> _timeoutSeconds;
        private readonly Action<NetworkConnection, JoinDecision> _apply;

        public JoinGateReaper(
            JoinHandshakeTracker tracker,
            Func<NetworkManager> manager,
            Func<float> timeoutSeconds,
            Action<NetworkConnection, JoinDecision> apply)
        {
            _tracker = tracker;
            _manager = manager;
            _timeoutSeconds = timeoutSeconds;
            _apply = apply;
        }

        public int Reap(double now)
        {
            var expired = _tracker.Expired(now, _timeoutSeconds());

            NetworkManager manager = _manager();
            if (manager != null && manager.ServerManager != null)
            {
                for (int i = 0; i < expired.Count; i++)
                {
                    if (manager.ServerManager.Clients.TryGetValue(expired[i], out NetworkConnection conn))
                        _apply(conn, JoinDecision.Reject(JoinPolicy.ReasonTimedOut));
                }
            }

            return expired.Count;
        }
    }
}
