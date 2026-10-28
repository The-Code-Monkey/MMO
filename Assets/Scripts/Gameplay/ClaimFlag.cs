using MMO.Social;
using UnityEngine;

namespace MMO.Gameplay
{
    /// <summary>
    /// A scene claim flag: zone geometry, not a spawned object. The server owns
    /// <see cref="OwnerGuildId"/>; clients receive it through a broadcast and only paint/route it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ClaimFlag : MonoBehaviour
    {
        [SerializeField] private string _territoryId = "wilderness-keep";
        [SerializeField] private VulnerabilityWindow _vulnerabilityWindow;
        [SerializeField] private string _ownerGuildId;

        public string TerritoryId
        {
            get => _territoryId;
            set => _territoryId = value;
        }

        public VulnerabilityWindow VulnerabilityWindow
        {
            get => _vulnerabilityWindow;
            set => _vulnerabilityWindow = value;
        }

        /// <summary>The owning guild, or null when the flag is unowned.</summary>
        public string OwnerGuildId
        {
            get => _ownerGuildId;
            set => _ownerGuildId = value;
        }

        /// <summary>Authors the flag's id and window (the editor baker, or a test).</summary>
        public void Configure(string territoryId, VulnerabilityWindow window)
        {
            _territoryId = territoryId;
            _vulnerabilityWindow = window;
        }

        /// <summary>Server/client: applies the authoritative owner.</summary>
        public void SetOwner(string guildId) => _ownerGuildId = guildId;

        /// <summary>
        /// True when this flag governs <paramref name="worldPosition"/>: its collider contains the
        /// point, or, with no collider, the flag is treated as covering its whole scene.
        /// </summary>
        public bool Contains(Vector3 worldPosition)
        {
            if (!TryGetComponent(out Collider collider) || collider == null)
                return true;

            return collider.bounds.Contains(worldPosition);
        }
    }
}
