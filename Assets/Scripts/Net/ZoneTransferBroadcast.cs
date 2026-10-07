using FishNet.Broadcast;

namespace MMO.Net
{
    /// <summary>
    /// Server → client instruction to leave the current zone server and reconnect to another one.
    /// Sent by a <see cref="Gameplay.ZoneTransferTrigger"/> after it has flushed the player's save
    /// and minted a short-lived <see cref="TransferToken"/>; the client's
    /// <see cref="ZoneTransferClient"/> acts on it and the target server's
    /// <see cref="MMOConnectionGate"/> consumes that token during the redirected join.
    /// </summary>
    public struct ZoneTransferBroadcast : IBroadcast
    {
        /// <summary>Address of the zone server to reconnect to.</summary>
        public string TargetHost;

        /// <summary>Port the target zone server listens on.</summary>
        public ushort TargetPort;

        /// <summary>Stable id of the destination server, for logging and token binding.</summary>
        public string TargetServerId;

        /// <summary>Short-lived, single-use authorization to join the target as this player.</summary>
        public string TransferToken;

        /// <summary>Player-facing reason, e.g. "Entering Westtown".</summary>
        public string Reason;

        /// <summary>True when every field the client needs to act on is present.</summary>
        public bool IsValid
            => !string.IsNullOrWhiteSpace(TargetHost)
               && TargetPort > 0
               && !string.IsNullOrEmpty(TransferToken);
    }
}
