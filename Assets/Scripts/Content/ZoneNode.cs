using System;
using System.Collections.Generic;
using MMO.Gameplay;
using UnityEngine;

namespace MMO.Content
{
    /// <summary>
    /// One zone on the macro map: the world's shape as data. The Inspector (and the asset YAML) is
    /// the only writer; everything else is a reader.
    /// </summary>
    [Serializable]
    public sealed class ZoneNode
    {
        [SerializeField] private string _zoneId = string.Empty;

        [Tooltip("Project-relative scene path. Empty means planned and not yet baked.")]
        [SerializeField] private string _sceneName = string.Empty;

        [SerializeField] private ZoneRiskTier _tier = ZoneRiskTier.Yellow;

        [Tooltip("False while the tier is still a draft; unsettled nodes are skipped by the geometry rules.")]
        [SerializeField] private bool _tierAssigned;

        [Tooltip("Zones on the other end of this zone's portals. Both sides must list each other.")]
        [SerializeField] private string[] _neighbours = Array.Empty<string>();

        [SerializeField] private bool _isChokePoint;

        public string ZoneId => _zoneId;

        public string SceneName => _sceneName;

        public bool HasScene => !string.IsNullOrWhiteSpace(_sceneName);

        public ZoneRiskTier Tier => _tier;

        public bool TierAssigned => _tierAssigned;

        public IReadOnlyList<string> Neighbours => _neighbours ?? Array.Empty<string>();

        public bool IsChokePoint => _isChokePoint;
    }
}
