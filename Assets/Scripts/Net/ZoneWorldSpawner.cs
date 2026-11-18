using System;
using System.Collections.Generic;
using FishNet.Managing;
using FishNet.Managing.Object;
using FishNet.Object;
using MMO.Banking;
using MMO.Crafting;
using MMO.Content;
using MMO.Gameplay;
using UnityEngine;

namespace MMO.Net
{
    /// <summary>Materialises authored zone markers into server-spawned network objects.</summary>
    internal sealed class ZoneWorldSpawner
    {
        private readonly Func<NetworkManager> _manager;
        private readonly MarkerPrefabResolver _prefabs;
        private readonly Action _wireSpawnPoints;
        private readonly Dictionary<Component, NetworkObject> _spawned = new();

        public ZoneWorldSpawner(
            Func<NetworkManager> manager,
            MarkerPrefabResolver prefabs,
            Action wireSpawnPoints)
        {
            _manager = manager;
            _prefabs = prefabs;
            _wireSpawnPoints = wireSpawnPoints;
        }

        public int Count => _spawned.Count;

        public void Reset()
        {
            _spawned.Clear();
            _prefabs.Reset();
        }

        public void SpawnAll()
        {
            ZoneContentRegistryLoader.LoadAll();
            _wireSpawnPoints();

            SpawnFromMarkers(
                UnityEngine.Object.FindObjectsByType<ResourceNodeMarker>(FindObjectsInactive.Exclude, FindObjectsSortMode.None),
                SpawnResourceNode);

            SpawnFromMarkers(
                UnityEngine.Object.FindObjectsByType<CraftingStationMarker>(FindObjectsInactive.Exclude, FindObjectsSortMode.None),
                SpawnCraftingStation);

            SpawnFromMarkers(
                UnityEngine.Object.FindObjectsByType<BankTellerMarker>(FindObjectsInactive.Exclude, FindObjectsSortMode.None),
                SpawnBankTeller);

            ConfigureSpawnControllers(
                UnityEngine.Object.FindObjectsByType<EnemyMarker>(FindObjectsInactive.Exclude, FindObjectsSortMode.None));
        }

        private void SpawnFromMarkers<T>(T[] markers, System.Func<T, NetworkObject> spawn) where T : Component
        {
            foreach (T marker in markers)
            {
                if (marker == null)
                    continue;

                if (_spawned.TryGetValue(marker, out NetworkObject existing) &&
                    existing != null && existing.IsSpawned)
                {
                    continue;
                }

                try
                {
                    NetworkObject spawned = spawn(marker);
                    if (spawned != null)
                        _spawned[marker] = spawned;
                }
                catch (System.Exception e)
                {
                    // One bad marker must not abort the whole world spawn.
                    Debug.LogError($"{nameof(WorldBootstrap)}: '{marker.name}' could not be spawned. {e.Message}", marker);
                }
            }
        }

        private NetworkObject SpawnResourceNode(ResourceNodeMarker marker)
        {
            NetworkObject prefab = _prefabs.Node(marker.NodePrefab);

            if (prefab == null)
            {
                Debug.LogError($"{nameof(WorldBootstrap)}: no ResourceNode prefab for marker '{marker.name}'; skipping.", marker);
                return null;
            }

            return SpawnAt(prefab, marker.transform);
        }

        private NetworkObject SpawnCraftingStation(CraftingStationMarker marker)
        {
            NetworkObject prefab = _prefabs.Station(marker.StationPrefab);

            if (prefab == null)
            {
                Debug.LogError($"{nameof(WorldBootstrap)}: no CraftingStation prefab for marker '{marker.name}'; skipping.", marker);
                return null;
            }

            NetworkObject spawned = SpawnAt(prefab, marker.transform);
            if (spawned != null && marker.StationType != CraftingStationType.None)
            {
                CraftingStation station = spawned.GetComponent<CraftingStation>();
                if (station != null)
                    station.Configure(marker.StationType);

                // Ensure the BoxCollider is not a trigger so the world click raycast finds it and
                // players cannot walk through the crafting station.
                if (spawned.TryGetComponent(out BoxCollider boxCollider))
                {
                    boxCollider.isTrigger = false;
                }
            }

            return spawned;
        }

        private NetworkObject SpawnBankTeller(BankTellerMarker marker)
        {
            NetworkObject prefab = _prefabs.Teller(marker.TellerPrefab);

            if (prefab == null)
            {
                Debug.LogError($"{nameof(WorldBootstrap)}: no BankTeller prefab for marker '{marker.name}'; skipping.", marker);
                return null;
            }

            NetworkObject spawned = SpawnAt(prefab, marker.transform);
            if (spawned != null && spawned.TryGetComponent(out BankTeller teller))
                teller.Configure(marker.Kind);

            return spawned;
        }

        /// <summary>
        /// Server-only: gives every authored enemy marker its own population controller. The
        /// controller owns spawning, density and respawn; the bootstrap only resolves the prefab
        /// and wires the pieces once.
        /// </summary>
        private void ConfigureSpawnControllers(EnemyMarker[] markers)
        {
            foreach (EnemyMarker marker in markers)
            {
                if (marker == null)
                    continue;

                try
                {
                    NetworkObject prefab = _prefabs.Enemy(marker.EnemyPrefab);

                    if (prefab == null)
                    {
                        Debug.LogError(
                            $"{nameof(WorldBootstrap)}: no enemy prefab for marker '{marker.name}'; skipping.", marker);
                        continue;
                    }

                    SpawnController controller = marker.GetComponent<SpawnController>();
                    if (controller == null)
                        controller = marker.gameObject.AddComponent<SpawnController>();

                    controller.Configure(marker, prefab, _manager());
                }
                catch (System.Exception e)
                {
                    Debug.LogError(
                        $"{nameof(WorldBootstrap)}: enemy marker '{marker.name}' could not be configured. {e.Message}",
                        marker);
                }
            }
        }

        private NetworkObject SpawnAt(NetworkObject prefab, Transform marker)
        {
            if (prefab == null)
                return null;

            // A prefab is never itself spawnable: instantiate, activate (a runtime-built source is
            // deactivated), place, then let the server validate and publish the clone.
            GameObject instance = UnityEngine.Object.Instantiate(prefab.gameObject);
            instance.name = prefab.name;
            instance.SetActive(true);
            instance.transform.SetPositionAndRotation(marker.position, marker.rotation);

            _manager().ServerManager.Spawn(instance);
            NetworkObject spawned = instance.GetComponent<NetworkObject>();
            return spawned != null && spawned.IsSpawned ? spawned : null;
        }
    }
}
