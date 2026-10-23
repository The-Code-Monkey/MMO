using System.Collections.Generic;
using FishNet.Managing;
using FishNet.Managing.Object;
using FishNet.Object;
using MMO.Gameplay;
using MMO.Inventory;
using UnityEngine;

namespace MMO.Net
{
    /// <summary>
    /// The one server-side path that turns a list of item instances into a networked
    /// <see cref="LootBag"/>. Player death, mob loot and chest overflow all use it, so the prefab
    /// resolution and spawn order cannot drift apart.
    /// </summary>
    public static class LootBagSpawner
    {
        /// <summary>
        /// Server: instantiate, spawn and fill a loot bag. A null prefab resolves the registered
        /// bag from the manager's spawnables. Returns null when nothing could be spawned, leaving
        /// the caller's items untouched.
        /// </summary>
        public static LootBag Spawn(
            NetworkManager manager,
            NetworkObject prefab,
            Vector3 position,
            IReadOnlyList<ItemInstance> items)
        {
            if (manager == null || manager.ServerManager == null || !manager.IsServerStarted)
                return null;

            if (items == null || items.Count == 0)
                return null;

            NetworkObject resolved = prefab != null ? prefab : ResolveLootBagPrefab(manager);
            if (resolved == null)
                return null;

            GameObject instance = Object.Instantiate(resolved.gameObject, position, Quaternion.identity);
            instance.name = resolved.name;
            instance.SetActive(true);
            manager.ServerManager.Spawn(instance);

            if (!instance.TryGetComponent(out LootBag bag))
            {
                Debug.LogError($"{nameof(LootBagSpawner)}: '{resolved.name}' has no {nameof(LootBag)}; the drop cannot be stored.");

                if (instance.TryGetComponent(out NetworkObject spawned) && spawned.IsSpawned)
                    manager.ServerManager.Despawn(spawned);
                else
                    Object.Destroy(instance);

                return null;
            }

            bag.Fill(items);
            return bag;
        }

        /// <summary>The loot-bag prefab registered in the manager's spawnable collection, or null.</summary>
        public static NetworkObject ResolveLootBagPrefab(NetworkManager manager)
        {
            if (manager == null || manager.SpawnablePrefabs is not SinglePrefabObjects single)
                return null;

            IReadOnlyList<NetworkObject> prefabs = single.Prefabs;
            for (int i = 0; i < prefabs.Count; i++)
            {
                NetworkObject candidate = prefabs[i];
                if (candidate != null && candidate.TryGetComponent(out LootBag _))
                    return candidate;
            }

            return null;
        }
    }
}
