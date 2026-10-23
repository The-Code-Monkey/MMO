using System.Collections.Generic;
using FishNet.Managing;
using FishNet.Object;
using MMO.Gameplay;
using MMO.Inventory;
using MMO.Net;
using UnityEngine;
using UnityEngine.AI;
using Random = UnityEngine.Random;

namespace MMO.Content
{
    /// <summary>
    /// Server-only population manager for one <see cref="EnemyMarker"/>. It counts nearby players,
    /// asks <see cref="SpawnRules"/> how many enemies should be alive, spawns/despawns toward that
    /// target, and replaces defeated enemies after their respawn timer.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SpawnController : MonoBehaviour
    {
        [SerializeField, Min(0.05f)] private float _tickInterval = 0.5f;

        private EnemyMarker _marker;
        private NetworkObject _prefab;
        private NetworkManager _manager;
        private readonly List<SpawnEntry> _spawned = new();
        private double _nextTickAt;

        /// <summary>Live enemies this controller currently owns.</summary>
        public int AliveCount => CountAlive();

        /// <summary>Live enemy components this controller currently owns, for tests and diagnostics.</summary>
        public IReadOnlyList<NPCStats> AliveEnemies
        {
            get
            {
                var alive = new List<NPCStats>(_spawned.Count);
                for (int i = 0; i < _spawned.Count; i++)
                {
                    SpawnEntry entry = _spawned[i];
                    if (!entry.Dead && entry.Object != null && entry.Stats != null && entry.Stats.IsAlive)
                        alive.Add(entry.Stats);
                }

                return alive;
            }
        }

        /// <summary>Configures the controller for a marker, enemy prefab and server.</summary>
        public void Configure(EnemyMarker marker, NetworkObject prefab, NetworkManager manager)
        {
            _marker = marker;
            _prefab = prefab;
            _manager = manager;
            _nextTickAt = 0d;
        }

        /// <summary>Changes the population tick rate; for tests and bakers.</summary>
        public void SetTickInterval(float seconds)
        {
            _tickInterval = Mathf.Max(0.05f, seconds);
            _nextTickAt = 0d;
        }

        private void Update()
        {
            if (_manager == null || !_manager.IsServerStarted || _prefab == null || _marker == null)
                return;

            double now = Time.timeAsDouble;
            if (now < _nextTickAt)
                return;

            _nextTickAt = now + Mathf.Max(0.05f, _tickInterval);

            CleanDestroyed();
            RespawnDue(now);

            int target = SpawnRules.TargetAlive(
                _marker.MinAlive, _marker.MaxAlive, CountPlayersNear(), _marker.PerPlayer);

            int alive = CountAlive();
            DespawnDeadWhenNotNeeded(target, alive);

            alive = CountAlive();
            int pending = CountPending(now);

            while (alive > target)
            {
                if (!DespawnOneAlive())
                    break;

                alive--;
            }

            int toSpawn = Mathf.Max(0, target - alive - pending);
            for (int i = 0; i < toSpawn; i++)
            {
                if (SpawnOne() == null)
                    break;
            }
        }

        private int CountPlayersNear()
        {
            float radius = _marker.ActivationRadius;
            if (radius <= 0f)
                return 0;

            PlayerStats[] players = FindObjectsByType<PlayerStats>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);

            int nearby = 0;
            for (int i = 0; i < players.Length; i++)
            {
                PlayerStats player = players[i];
                if (player != null && player.Health > 0 &&
                    Vector3.Distance(player.transform.position, transform.position) <= radius)
                {
                    nearby++;
                }
            }

            return nearby;
        }

        private int CountAlive()
        {
            int alive = 0;
            for (int i = 0; i < _spawned.Count; i++)
            {
                SpawnEntry entry = _spawned[i];
                if (!entry.Dead && entry.Object != null && entry.Stats != null && entry.Stats.IsAlive)
                    alive++;
            }

            return alive;
        }

        private int CountPending(double now)
        {
            int pending = 0;
            for (int i = 0; i < _spawned.Count; i++)
            {
                SpawnEntry entry = _spawned[i];
                if (entry.Dead && entry.Object != null && now < entry.RespawnAt)
                    pending++;
            }

            return pending;
        }

        private void RespawnDue(double now)
        {
            for (int i = _spawned.Count - 1; i >= 0; i--)
            {
                SpawnEntry entry = _spawned[i];
                if (entry.Dead && entry.Object != null && now >= entry.RespawnAt)
                    DespawnEntry(entry);
            }
        }

        private void DespawnDeadWhenNotNeeded(int target, int alive)
        {
            bool noRoomForCorpses = alive >= target || _spawned.Count > target;
            if (!noRoomForCorpses)
                return;

            for (int i = _spawned.Count - 1; i >= 0; i--)
            {
                if (_spawned[i].Dead)
                    DespawnEntry(_spawned[i]);
            }
        }

        private bool DespawnOneAlive()
        {
            for (int i = _spawned.Count - 1; i >= 0; i--)
            {
                SpawnEntry entry = _spawned[i];
                if (!entry.Dead && entry.Object != null)
                {
                    DespawnEntry(entry);
                    return true;
                }
            }

            return false;
        }

        private SpawnEntry SpawnOne()
        {
            if (_prefab == null || _manager == null || _manager.ServerManager == null)
                return null;

            Vector3 position = NextSpawnPosition();
            GameObject instance = Instantiate(_prefab.gameObject, position, Quaternion.identity);
            instance.name = _prefab.name;
            instance.SetActive(true);
            _manager.ServerManager.Spawn(instance);

            if (!instance.TryGetComponent(out NetworkObject networkObject) ||
                !instance.TryGetComponent(out NPCStats stats))
            {
                Debug.LogError($"{nameof(SpawnController)}: '{_prefab.name}' needs a NetworkObject and NPCStats.");

                if (networkObject != null && networkObject.IsSpawned)
                    _manager.ServerManager.Despawn(networkObject);
                else
                    Destroy(instance);

                return null;
            }

            var entry = new SpawnEntry
            {
                Object = networkObject,
                Stats = stats,
            };
            entry.Handler = _ => MarkDead(entry);
            stats.Died += entry.Handler;
            _spawned.Add(entry);
            return entry;
        }

        private Vector3 NextSpawnPosition()
        {
            float radius = _marker.WanderRadius;
            Vector3 candidate = transform.position;

            if (radius > 0f)
            {
                Vector2 offset = Random.insideUnitCircle * radius;
                candidate += new Vector3(offset.x, 0f, offset.y);
            }

            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 2f, NavMesh.AllAreas))
                return hit.position;

            return transform.position;
        }

        private void MarkDead(SpawnEntry entry)
        {
            if (entry.Dead || _marker == null)
                return;

            entry.Dead = true;
            entry.RespawnAt = Time.timeAsDouble + _marker.RespawnSeconds;
        }

        private void CleanDestroyed()
        {
            for (int i = _spawned.Count - 1; i >= 0; i--)
            {
                SpawnEntry entry = _spawned[i];
                if (entry.Object == null || entry.Stats == null)
                    _spawned.RemoveAt(i);
            }
        }

        private void DespawnEntry(SpawnEntry entry)
        {
            if (entry.Stats != null && entry.Handler != null)
                entry.Stats.Died -= entry.Handler;

            _spawned.Remove(entry);

            if (entry.Object != null && entry.Object.IsSpawned &&
                _manager != null && _manager.ServerManager != null)
            {
                _manager.ServerManager.Despawn(entry.Object);
            }
        }

        private void OnDestroy()
        {
            for (int i = _spawned.Count - 1; i >= 0; i--)
                DespawnEntry(_spawned[i]);
        }

        private sealed class SpawnEntry
        {
            public NetworkObject Object;
            public NPCStats Stats;
            public double RespawnAt;
            public bool Dead;
            public System.Action<PlayerInventory> Handler;
        }
    }
}
