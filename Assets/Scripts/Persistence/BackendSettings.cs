using System;
using UnityEngine;

namespace MMO.Persistence
{
    /// <summary>
    /// Connection details for the Nakama backend, serialized alongside the other server settings.
    /// The same file feeds the local <c>docker-compose</c> (defaults match it exactly) and a real
    /// deployment's host/port/key.
    ///
    /// <para>
    /// <see cref="ServerDeviceId"/> is deliberately a single value shared by every game-server
    /// instance: Nakama's storage permissions are owner-scoped, so a process that authenticates as
    /// a <em>different</em> service account cannot read the players another instance saved. One
    /// shared service account is what lets N servers read and write one source of truth, with the
    /// storage-object version check arbitrating races between them.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class BackendSettings
    {
        /// <summary>The Nakama HTTP gateway is plain HTTP in the shipped docker-compose.</summary>
        public const string DefaultScheme = "http";

        /// <summary>docker-compose publishes the API gateway on this address.</summary>
        public const string DefaultHost = "127.0.0.1";

        /// <summary>Nakama's HTTP gateway port (gRPC is 7349, console 7351).</summary>
        public const ushort DefaultPort = 7350;

        /// <summary>The server key Nakama ships for local development.</summary>
        public const string DefaultServerKey = "defaultkey";

        /// <summary>
        /// The shared device id the game server authenticates with. Change this per deployment,
        /// but keep it identical across every server instance in that deployment.
        /// </summary>
        public const string DefaultServerDeviceId = "mmo-game-server";

        /// <summary>Account username the server registers under (affects leaderboards, etc.).</summary>
        public const string DefaultServerUsername = "game-server";

        [SerializeField] private string _scheme = DefaultScheme;
        [SerializeField] private string _host = DefaultHost;
        [SerializeField] private ushort _port = DefaultPort;
        [SerializeField] private string _serverKey = DefaultServerKey;
        [SerializeField] private string _serverDeviceId = DefaultServerDeviceId;
        [SerializeField] private string _serverUsername = DefaultServerUsername;

        public BackendSettings()
        {
        }

        public BackendSettings(
            string scheme,
            string host,
            ushort port,
            string serverKey,
            string serverDeviceId = null,
            string serverUsername = null)
        {
            _scheme = scheme ?? DefaultScheme;
            _host = host ?? DefaultHost;
            _port = port;
            _serverKey = serverKey ?? DefaultServerKey;
            _serverDeviceId = string.IsNullOrEmpty(serverDeviceId) ? DefaultServerDeviceId : serverDeviceId;
            _serverUsername = string.IsNullOrEmpty(serverUsername) ? DefaultServerUsername : serverUsername;
        }

        public string Scheme => _scheme;
        public string Host => _host;
        public ushort Port => _port;
        public string ServerKey => _serverKey;
        public string ServerDeviceId => _serverDeviceId;
        public string ServerUsername => _serverUsername;
    }
}