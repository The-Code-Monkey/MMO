using System.Collections.Generic;
using FishNet.Managing;
using FishNet.Object;

namespace MMO.Inventory
{
    /// <summary>Finds the locally-owned player inventory through FishNet.</summary>
    public static class PlayerInventoryLocator
    {
        public static PlayerInventory FindLocal(NetworkManager networkManager)
        {
            if (networkManager == null || networkManager.ServerManager == null || networkManager.ClientManager == null)
                return null;

            IReadOnlyDictionary<int, NetworkObject> spawned = networkManager.IsHostStarted
                ? networkManager.ServerManager.Objects.Spawned
                : networkManager.ClientManager.Objects.Spawned;

            foreach (NetworkObject nob in spawned.Values)
            {
                if (nob != null && nob.IsSpawned && nob.IsOwner && nob.TryGetComponent(out PlayerInventory inventory))
                    return inventory;
            }

            return null;
        }
    }
}
