using System;
using FishNet.Object;
using UnityEngine;

namespace MMO.Net
{
    /// <summary>
    /// A scene-authored enemy spawn point. The server-side <c>SpawnController</c> keeps the
    /// population around this marker, scaling it with nearby players and respawning defeats.
    /// Clients only ever see the objects the server spawns.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("MMO/Net/Enemy Marker")]
    public sealed class EnemyMarker : MonoBehaviour
    {
        [Tooltip("Optional. Leave empty to use the shared enemy (target dummy) prefab the server has registered.")]
        [SerializeField]
        private NetworkObject _enemyPrefab;

        [Header("Population")]
        [SerializeField, Min(0)] private int _minAlive = 1;
        [SerializeField, Min(0)] private int _maxAlive = 1;
        [SerializeField, Min(0)] private int _perPlayer = 1;

        [Header("Timing and space")]
        [SerializeField, Min(0f)] private float _respawnSeconds = 30f;
        [SerializeField, Min(0f)] private float _wanderRadius = 2f;
        [SerializeField, Min(0f)] private float _activationRadius = 60f;

        /// <summary>
        /// The prefab to spawn for this marker, or null to fall back to the registered enemy
        /// (<c>TargetDummy</c>) prefab.
        /// </summary>
        public NetworkObject EnemyPrefab => _enemyPrefab;

        /// <summary>Lowest population while at least one player is nearby.</summary>
        public int MinAlive => Mathf.Max(0, _minAlive);

        /// <summary>Highest population this marker may reach.</summary>
        public int MaxAlive => Mathf.Max(MinAlive, _maxAlive);

        /// <summary>Extra enemies per nearby player.</summary>
        public int PerPlayer => Mathf.Max(0, _perPlayer);

        /// <summary>Seconds a defeated enemy stays gone before it is replaced.</summary>
        public float RespawnSeconds => Mathf.Max(0f, _respawnSeconds);

        /// <summary>Random spawn offset radius around the marker.</summary>
        public float WanderRadius => Mathf.Max(0f, _wanderRadius);

        /// <summary>Players within this radius count toward the population target.</summary>
        public float ActivationRadius => Mathf.Max(0f, _activationRadius);

        /// <summary>Points the marker at a specific enemy prefab; for bakers and tests.</summary>
        public void SetEnemyPrefab(NetworkObject prefab) => _enemyPrefab = prefab;

        /// <summary>Configures population and timing; for bakers and tests.</summary>
        public void SetTuning(
            int minAlive,
            int maxAlive,
            int perPlayer,
            float respawnSeconds,
            float wanderRadius,
            float activationRadius)
        {
            _minAlive = Math.Max(0, minAlive);
            _maxAlive = Math.Max(_minAlive, maxAlive);
            _perPlayer = Math.Max(0, perPlayer);
            _respawnSeconds = Math.Max(0f, respawnSeconds);
            _wanderRadius = Math.Max(0f, wanderRadius);
            _activationRadius = Math.Max(0f, activationRadius);
        }
    }
}
