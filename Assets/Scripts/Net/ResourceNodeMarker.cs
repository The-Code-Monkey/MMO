using FishNet.Object;
using UnityEngine;

namespace MMO.Net
{
    /// <summary>
    /// A scene-authored "put a resource node here" marker. Like every marker in this namespace it
    /// is inert: a pure-client process carries it but never acts on it, and only
    /// <see cref="WorldBootstrap"/> — running on the server — turns it into a networked
    /// <see cref="Gameplay.ResourceNode"/> through <c>ServerManager.Spawn</c>. The marker holds
    /// only configuration (which prefab, where), never node state, so depletion stays with the
    /// spawned object where the server owns it.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("MMO/Net/Resource Node Marker")]
    public sealed class ResourceNodeMarker : MonoBehaviour
    {
        [Tooltip("Optional. Leave empty to use the shared ResourceNode prefab the server has registered.")]
        [SerializeField]
        private NetworkObject _nodePrefab;

        /// <summary>
        /// The prefab to spawn for this marker, or null to fall back to the registered
        /// <c>ResourceNode</c> prefab (built in memory when the zone has none).
        /// </summary>
        public NetworkObject NodePrefab => _nodePrefab;

        /// <summary>Points the marker at a specific node prefab; for bakers and tests.</summary>
        public void SetNodePrefab(NetworkObject prefab) => _nodePrefab = prefab;
    }
}
