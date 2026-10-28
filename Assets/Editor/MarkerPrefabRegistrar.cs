using FishNet.Managing;
using FishNet.Managing.Object;
using FishNet.Object;
using MMO.Net;
using UnityEngine;

namespace MMO.Editor
{
    /// <summary>Registers every prefab referenced by a scene's markers into its NetworkManager.</summary>
    public static class MarkerPrefabRegistrar
    {
        public static int Register(NetworkManager manager)
        {
            if (manager == null)
                return 0;

            if (manager.SpawnablePrefabs == null)
            {
                var prefabs = ScriptableObject.CreateInstance<SinglePrefabObjects>();
                prefabs.name = NetworkManagerFactory.SpawnablePrefabsName;
                manager.SpawnablePrefabs = prefabs;
            }

            int registered = 0;

            NetworkObject nodePrefab = ResourceNodeBaker.EnsureResourceNodePrefab();
            if (nodePrefab != null && ResourceNodePrefabFactory.Register(manager.SpawnablePrefabs, nodePrefab))
                registered++;

            foreach (ResourceNodeMarker marker in Object.FindObjectsByType<ResourceNodeMarker>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (marker.NodePrefab != null && ResourceNodePrefabFactory.Register(manager.SpawnablePrefabs, marker.NodePrefab))
                    registered++;
            }

            foreach (CraftingStationMarker marker in Object.FindObjectsByType<CraftingStationMarker>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (marker.StationPrefab != null && CraftingStationPrefabFactory.Register(manager.SpawnablePrefabs, marker.StationPrefab))
                    registered++;
            }

            foreach (EnemyMarker marker in Object.FindObjectsByType<EnemyMarker>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (marker.EnemyPrefab != null && TargetDummyPrefabFactory.Register(manager.SpawnablePrefabs, marker.EnemyPrefab))
                    registered++;
            }

            NetworkObject tellerPrefab = BankTellerBaker.EnsureBankTellerPrefab();
            if (tellerPrefab != null && BankTellerPrefabFactory.Register(manager.SpawnablePrefabs, tellerPrefab))
                registered++;

            foreach (BankTellerMarker marker in Object.FindObjectsByType<BankTellerMarker>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (marker.TellerPrefab != null && BankTellerPrefabFactory.Register(manager.SpawnablePrefabs, marker.TellerPrefab))
                    registered++;
            }

            return registered;
        }
    }
}
