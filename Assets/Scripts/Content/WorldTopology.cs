using System;
using System.Collections.Generic;
using UnityEngine;

namespace MMO.Content
{
    /// <summary>
    /// The macro map as data: every zone, every portal and every risk tier. Editor-consumed only —
    /// it is not under <c>Resources</c>, and the tier a player dies under still comes from the
    /// scene's <c>ZoneRiskConfig</c>.
    /// </summary>
    [CreateAssetMenu(fileName = "New World Topology", menuName = "MMO/World Topology")]
    public sealed class WorldTopology : ScriptableObject
    {
        /// <summary>Where the one authored topology lives. Not Resources-loadable by design.</summary>
        public const string AssetPath = "Assets/Data/World/WorldTopology.asset";

        [SerializeField] private ZoneNode[] _nodes = Array.Empty<ZoneNode>();

        public IReadOnlyList<ZoneNode> Nodes => _nodes ?? Array.Empty<ZoneNode>();
    }
}
