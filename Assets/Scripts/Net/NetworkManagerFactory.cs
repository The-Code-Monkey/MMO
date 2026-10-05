using FishNet.Managing;
using FishNet.Managing.Object;
using FishNet.Transporting.Tugboat;
using UnityEngine;

namespace MMO.Net
{
    public static class NetworkManagerFactory
    {
        public const string GameObjectName = "NetworkManager";

        public static NetworkManager Create(MMOConnectionSettings settings, out MMOConnectionGate gate)
        {
            settings ??= new MMOConnectionSettings();

            var go = new GameObject(GameObjectName);

            // Awake() runs the instant a component lands on an *active* object, and
            // NetworkManager.Awake() dereferences SpawnablePrefabs + looks for the
            // Authenticator. Build inactive, configure, then activate.
            go.SetActive(false);

            Tugboat transport = go.AddComponent<Tugboat>();
            transport.SetPort(settings.Port);
            transport.SetMaximumClients(settings.MaxClients);

            var prefabs = ScriptableObject.CreateInstance<SinglePrefabObjects>();
            prefabs.name = "MMOSpawnablePrefabs";

            NetworkManager manager = go.AddComponent<NetworkManager>();
            manager.SpawnablePrefabs = prefabs;

            gate = go.AddComponent<MMOConnectionGate>();
            gate.SetSettings(settings);

            go.SetActive(true);
            return manager;
        }
    }
}
