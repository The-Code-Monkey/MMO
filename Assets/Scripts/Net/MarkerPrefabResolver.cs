using System;
using FishNet.Managing;
using FishNet.Managing.Object;
using FishNet.Object;
using UnityEngine;

namespace MMO.Net
{
    /// <summary>Resolves and registers the prefabs authored markers spawn from.</summary>
    internal sealed class MarkerPrefabResolver
    {
        private readonly Func<NetworkManager> _manager;
        private NetworkObject _fallbackNodePrefab;

        public MarkerPrefabResolver(Func<NetworkManager> manager)
        {
            _manager = manager;
        }

        public void Reset() => _fallbackNodePrefab = null;

        public NetworkObject Node(NetworkObject explicitPrefab)
            => explicitPrefab != null ? EnsureNodeRegistered(explicitPrefab) : ResolveDefaultNodePrefab();

        public NetworkObject Station(NetworkObject explicitPrefab)
            => explicitPrefab != null
                ? EnsureStationRegistered(explicitPrefab)
                : ResolveRegisteredPrefab(CraftingStationPrefabFactory.PrefabName);

        public NetworkObject Teller(NetworkObject explicitPrefab)
            => explicitPrefab != null
                ? EnsureTellerRegistered(explicitPrefab)
                : ResolveRegisteredPrefab(BankTellerPrefabFactory.PrefabName);

        public NetworkObject Enemy(NetworkObject explicitPrefab)
            => explicitPrefab != null
                ? EnsureEnemyRegistered(explicitPrefab)
                : ResolveRegisteredPrefab(TargetDummyPrefabFactory.PrefabName);

        private NetworkObject ResolveDefaultNodePrefab()
        {
            NetworkObject registered = ResolveRegisteredPrefab(ResourceNodePrefabFactory.PrefabName);
            if (registered != null)
                return registered;

            if (_fallbackNodePrefab == null)
            {
                _fallbackNodePrefab = ResourceNodePrefabFactory.Create();
                _fallbackNodePrefab.gameObject.SetActive(false);
            }

            return EnsureNodeRegistered(_fallbackNodePrefab);
        }

        private NetworkObject EnsureNodeRegistered(NetworkObject prefab)
        {
            NetworkManager manager = _manager();
            if (prefab == null || manager == null || manager.SpawnablePrefabs == null)
                return prefab;

            ResourceNodePrefabFactory.Register(manager.SpawnablePrefabs, prefab, initializeAdded: true);
            return prefab;
        }

        private NetworkObject EnsureStationRegistered(NetworkObject prefab)
        {
            NetworkManager manager = _manager();
            if (prefab == null || manager == null || manager.SpawnablePrefabs == null)
                return prefab;

            CraftingStationPrefabFactory.Register(manager.SpawnablePrefabs, prefab, initializeAdded: true);
            return prefab;
        }

        private NetworkObject EnsureTellerRegistered(NetworkObject prefab)
        {
            NetworkManager manager = _manager();
            if (prefab == null || manager == null || manager.SpawnablePrefabs == null)
                return prefab;

            BankTellerPrefabFactory.Register(manager.SpawnablePrefabs, prefab, initializeAdded: true);
            return prefab;
        }

        private NetworkObject EnsureEnemyRegistered(NetworkObject prefab)
        {
            NetworkManager manager = _manager();
            if (prefab == null || manager == null || manager.SpawnablePrefabs == null)
                return prefab;

            TargetDummyPrefabFactory.Register(manager.SpawnablePrefabs, prefab, initializeAdded: true);
            return prefab;
        }

        private NetworkObject ResolveRegisteredPrefab(string prefabName)
        {
            NetworkManager manager = _manager();
            if (manager == null || manager.SpawnablePrefabs is not SinglePrefabObjects single)
                return null;

            foreach (NetworkObject prefab in single.Prefabs)
            {
                if (prefab != null && prefab.name == prefabName)
                    return prefab;
            }

            return null;
        }
    }
}
