using System.Collections;
using FishNet.Managing;
using FishNet.Transporting.Tugboat;
using MMO.Net;

namespace MMO.Tests
{
    /// <summary>
    /// FishNet allows exactly one NetworkManager per process
    /// (<c>PersistenceType.DestroyNewest</c>), so every PlayMode test shares one. Creating
    /// a second would silently destroy the first and fail in confusing ways.
    /// </summary>
    internal static class NetworkManagerTestHelper
    {
        /// <summary>
        /// A port away from the shipped default so a developer's running server cannot
        /// collide with a test run.
        /// </summary>
        public const ushort TestPort = 7979;

        /// <summary>
        /// A generous handshake timeout: CI machines are slow, and these tests are about
        /// wiring, not about timing the gate out.
        /// </summary>
        public const float TestHandshakeTimeoutSeconds = 30f;

        public static NetworkManager GetOrCreate()
        {
            if (NetworkManager.Instances.Count > 0)
                return NetworkManager.Instances[0];

            var settings = new MMOConnectionSettings(
                TestPort, MMOConnectionSettings.DefaultMaxClients, TestHandshakeTimeoutSeconds);

            return NetworkManagerFactory.Create(settings, out _);
        }

        public static MMOConnectionGate Gate(NetworkManager manager)
            => manager.ServerManager.GetAuthenticator() as MMOConnectionGate;

        public static Tugboat Transport(NetworkManager manager)
            => manager.TransportManager.Transport as Tugboat;

        public static void StartHost(NetworkManager manager)
        {
            if (manager.IsHostStarted)
                return;

            manager.ServerManager.StartConnection();
            manager.ClientManager.StartConnection();
        }

        public static void StopIfListening(NetworkManager manager)
        {
            if (manager == null || manager.IsOffline)
                return;

            manager.ServerManager.StopConnection(true);
            manager.ClientManager.StopConnection();
        }

        /// <summary>
        /// Waits for the join handshake to finish, i.e. until the server has authenticated
        /// the local client. The gate needs a broadcast round trip, so poll rather than
        /// guess a frame count.
        /// </summary>
        public static IEnumerator WaitForHostAuthenticated(NetworkManager manager, int maxFrames = 300)
        {
            for (int i = 0; i < maxFrames && !manager.IsClientStarted; i++)
                yield return null;
        }
    }
}