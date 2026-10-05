using FishNet.Managing;
using FishNet.Managing.Object;
using FishNet.Transporting.Tugboat;
using UnityEngine;

namespace MMO.Net
{
    /// <summary>
    /// Single source of truth for the "NetworkManager" GameObject. Both the editor baker
    /// and the tests build the object through here so a scene and a fresh process end up
    /// wired identically.
    /// </summary>
    public static class NetworkManagerFactory
    {
        public const string GameObjectName = "NetworkManager";
        public const string SpawnablePrefabsName = "MMOSpawnablePrefabs";

        public static NetworkManager Create(MMOConnectionSettings settings, out MMOConnectionGate gate)
        {
            settings ??= new MMOConnectionSettings();

            var go = new GameObject(GameObjectName);

            // Awake() runs the instant a component lands on an *active* object, and
            // NetworkManager.Awake() dereferences SpawnablePrefabs and looks up the
            // Authenticator. Build inactive, wire everything, then activate last.
            go.SetActive(false);

            try
            {
                Tugboat transport = go.AddComponent<Tugboat>();
                transport.SetPort(settings.Port);
                transport.SetMaximumClients(settings.MaxClients);

                var prefabs = ScriptableObject.CreateInstance<SinglePrefabObjects>();
                prefabs.name = SpawnablePrefabsName;

                NetworkManager manager = go.AddComponent<NetworkManager>();
                manager.SpawnablePrefabs = prefabs;

                gate = go.AddComponent<MMOConnectionGate>();
                gate.SetSettings(settings);

                // Tells the server who we are as soon as we connect, which is what
                // lets the gate let us in.
                go.AddComponent<JoinRequestClient>();

                go.SetActive(true);
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