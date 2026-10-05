using FishNet.Managing.Object;
using FishNet.Managing.Transporting;
using FishNet.Transporting.Tugboat;
using UnityEngine;

namespace MMO.Networking
{
    public static class NetworkManagerFactory
    {
        public const string ObjectName = "NetworkManager";

        /// <summary>
        /// Builds the NetworkManager object. The GameObject stays inactive until
        /// every component is wired, because AddComponent on an active object
        /// fires Awake immediately and NetworkManager.Awake dereferences
        /// SpawnablePrefabs.
        /// </summary>
        public static FishNet.Managing.NetworkManager Create(string playerName = "Player", PrefabObjects spawnablePrefabs = null)
        {
            var go = new GameObject(ObjectName);
            go.SetActive(false);

            try
            {
                var transport = go.AddComponent<Tugboat>();
                var manager = go.AddComponent<FishNet.Managing.NetworkManager>();
                go.AddComponent<JoinGateAuthenticator>();
                var joinClient = go.AddComponent<JoinRequestClient>();
                joinClient.SetPlayerName(playerName);

                manager.SpawnablePrefabs = spawnablePrefabs != null
                    ? spawnablePrefabs
                    : ScriptableObject.CreateInstance<DefaultPrefabObjects>();

                go.SetActive(true);

                if (manager.TransportManager != null && manager.TransportManager.Transport == null)
                    manager.TransportManager.Transport = transport;

                return manager;
            }
            catch
            {
                Object.DestroyImmediate(go);
                throw;
            }
        }
    }
}
