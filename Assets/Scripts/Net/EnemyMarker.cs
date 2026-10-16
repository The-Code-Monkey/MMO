using FishNet.Object;
using UnityEngine;

namespace MMO.Net
{
    /// <summary>
    /// A scene-authored "put an enemy here" marker. The server spawns the referenced prefab at the
    /// marker's transform through <see cref="WorldBootstrap"/>; clients only ever see the spawned
    /// clone. When no prefab is set the bootstrap falls back to the registered target-dummy enemy,
    /// so a zone is hostile end to end before the AI from #60 lands — this issue only places the
    /// object, it does not give it a brain.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("MMO/Net/Enemy Marker")]
    public sealed class EnemyMarker : MonoBehaviour
    {
        [Tooltip("Optional. Leave empty to use the shared enemy (target dummy) prefab the server has registered.")]
        [SerializeField]
        private NetworkObject _enemyPrefab;

        /// <summary>
        /// The prefab to spawn for this marker, or null to fall back to the registered enemy
        /// (<c>TargetDummy</c>) prefab.
        /// </summary>
        public NetworkObject EnemyPrefab => _enemyPrefab;

        /// <summary>Points the marker at a specific enemy prefab; for bakers and tests.</summary>
        public void SetEnemyPrefab(NetworkObject prefab) => _enemyPrefab = prefab;
    }
}
