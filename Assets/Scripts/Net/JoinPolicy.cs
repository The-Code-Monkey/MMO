using System;
using System.Collections.Generic;

namespace MMO.Net
{
    public readonly struct JoinRequest
    {
        public string PlayerName { get; }
        public int ConnectedClients { get; }
        public int MaxClients { get; }
        public float SecondsSinceConnected { get; }

        public JoinRequest(string playerName, int connectedClients, int maxClients, float secondsSinceConnected = 0f)
        {
            PlayerName = playerName;
            ConnectedClients = connectedClients;
            MaxClients = maxClients;
            SecondsSinceConnected = secondsSinceConnected;
        }
    }

    public readonly struct JoinDecision
    {
        public bool Approved { get; }
        public string Reason { get; }
        private JoinDecision(bool approved, string reason) { Approved = approved; Reason = reason; }
        public static JoinDecision Approve() => new(true, string.Empty);
        public static JoinDecision Reject(string reason) => new(false, reason ?? string.Empty);
    }

    public static class JoinPolicy
    {
        public const string ReasonMaintenance = "Server is in maintenance mode.";
        public const string ReasonInvalidCapacity = "Server capacity is misconfigured.";
        public const string ReasonTimedOut = "Handshake timed out.";
        public const string ReasonBlocked = "Player is blocked.";
        public const string ReasonMissingName = "A player name is required.";
        public const string ReasonNotAllowed = "Player is not on the allow list.";
        public const string ReasonFull = "Server is full.";

        public static JoinDecision Evaluate(
            in JoinRequest request,
            bool maintenanceMode,
            IReadOnlyList<string> allowList,
            IReadOnlyList<string> blockList,
            float handshakeTimeoutSeconds)
        {
            if (maintenanceMode)
                return JoinDecision.Reject(ReasonMaintenance);

            if (request.MaxClients < 1)
                return JoinDecision.Reject(ReasonInvalidCapacity);

            if (request.SecondsSinceConnected > handshakeTimeoutSeconds)
                return JoinDecision.Reject(ReasonTimedOut);

            if (Contains(blockList, request.PlayerName))
                return JoinDecision.Reject(ReasonBlocked);

            if (string.IsNullOrWhiteSpace(request.PlayerName))
                return JoinDecision.Reject(ReasonMissingName);

            if (allowList != null && allowList.Count > 0 && !Contains(allowList, request.PlayerName))
                return JoinDecision.Reject(ReasonNotAllowed);

            if (request.ConnectedClients >= request.MaxClients)
                return JoinDecision.Reject(ReasonFull);

            return JoinDecision.Approve();
        }

        public static bool Contains(IReadOnlyList<string> list, string value)
        {
            if (list == null || value == null)
                return false;

            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i], value, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
    }
}
