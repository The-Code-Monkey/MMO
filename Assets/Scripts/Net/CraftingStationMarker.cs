using FishNet.Object;
using MMO.Crafting;
using UnityEngine;

namespace MMO.Net
{
    /// <summary>
    /// A scene-authored "put a crafting station here" marker. The server materialises it through
    /// <see cref="WorldBootstrap"/>; clients carry the marker but never spawn from it. Beyond
    /// placement it carries the station's <see cref="CraftingStationType"/>, which the bootstrap
    /// applies with <see cref="CraftingStation.Configure"/> once the object is spawned — so a
    /// furnace and an anvil are the same prefab wearing different configuration, exactly as the
    /// ability system intends.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("MMO/Net/Crafting Station Marker")]
    public sealed class CraftingStationMarker : MonoBehaviour
    {
        [Tooltip("Optional. Leave empty to use the shared CraftingStation prefab the server has registered.")]
        [SerializeField]
        private NetworkObject _stationPrefab;

        [SerializeField]
        private CraftingStationType _stationType = CraftingStationType.Furnace;

        /// <summary>
        /// The prefab to spawn for this marker, or null to fall back to the registered
        /// <c>CraftingStation</c> prefab.
        /// </summary>
        public NetworkObject StationPrefab => _stationPrefab;

        /// <summary>The station type the spawned station is configured to.</summary>
        public CraftingStationType StationType => _stationType;

        /// <summary>Points the marker at a specific station prefab; for bakers and tests.</summary>
        public void SetStationPrefab(NetworkObject prefab) => _stationPrefab = prefab;

        /// <summary>Sets the station type; for bakers and tests.</summary>
        public void SetStationType(CraftingStationType stationType) => _stationType = stationType;
    }
}
