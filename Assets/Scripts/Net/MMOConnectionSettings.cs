using System;
using UnityEngine;

namespace MMO.Net
{
    [Serializable]
    public sealed class MMOConnectionSettings
    {
        public const ushort DefaultPort = 7770;
        public const int DefaultMaxClients = 64;
        public const float DefaultHandshakeTimeoutSeconds = 5f;

        [SerializeField] private ushort _port = DefaultPort;
        [SerializeField, Range(1, 9999)] private int _maxClients = DefaultMaxClients;
        [SerializeField, Min(0.5f)] private float _handshakeTimeoutSeconds = DefaultHandshakeTimeoutSeconds;
        [SerializeField] private bool _maintenanceMode;
        [SerializeField] private string[] _allowedPlayers = Array.Empty<string>();
        [SerializeField] private string[] _blockedPlayers = Array.Empty<string>();

        public ushort Port => _port;
        public int MaxClients => _maxClients;
        public float HandshakeTimeoutSeconds => _handshakeTimeoutSeconds;
        public bool MaintenanceMode => _maintenanceMode;
        public string[] AllowedPlayers => _allowedPlayers;
        public string[] BlockedPlayers => _blockedPlayers;
    }
}
