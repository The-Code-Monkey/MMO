using System;
using FishNet.Connection;
using FishNet.Managing;
using UnityEngine;

namespace MMO.Net
{
    /// <summary>Commits admission decisions and reports the authenticated-client count.</summary>
    internal sealed class JoinGateVerdicts
    {
        private readonly JoinHandshakeTracker _tracker;
        private readonly Func<NetworkManager> _manager;
        private readonly Action<NetworkConnection, bool> _authenticationResult;

        public JoinGateVerdicts(
            JoinHandshakeTracker tracker,
            Func<NetworkManager> manager,
            Action<NetworkConnection, bool> authenticationResult)
        {
            _tracker = tracker;
            _manager = manager;
            _authenticationResult = authenticationResult;
        }

        public void Apply(NetworkConnection connection, JoinDecision decision)
        {
            if (connection == null)
                return;

            int applyClientId = connection.ClientId;
            if (decision.Approved)
            {
                Debug.Log($"[JoinGate] Admitted client {applyClientId}.");
            }
            else
            {
                Debug.Log($"[JoinGate] Refused client {applyClientId}: {decision.Reason}");
                SendRejection(connection, decision.Reason);
            }

            _tracker.End(connection.ClientId);
            _authenticationResult?.Invoke(connection, decision.Approved);
        }

        public int CountAuthenticatedClients()
        {
            NetworkManager manager = _manager();
            if (manager == null || manager.ServerManager == null)
                return 0;

            int count = 0;
            foreach (NetworkConnection conn in manager.ServerManager.Clients.Values)
            {
                if (conn.IsAuthenticated)
                    count++;
            }

            return count;
        }

        private void SendRejection(NetworkConnection connection, string reason)
        {
            NetworkManager manager = _manager();
            if (manager == null || manager.ServerManager == null)
                return;

            manager.ServerManager.Broadcast(connection, new JoinResultBroadcast
            {
                Approved = false,
                Reason = reason
            }, requireAuthenticated: false);
        }
    }
}
