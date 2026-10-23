using UnityEngine;

namespace MMO.Gameplay
{
    /// <summary>
    /// Marks a stretch of world as a risk zone and says how dangerous it is. Drop one on a volume
    /// (with a <see cref="Collider"/> defining its bounds) or on a scene-root object for a
    /// whole-map flag; <see cref="PlayerDeath"/> then asks
    /// <see cref="ResolveTierAt(Vector3)"/> which zone the dying player was standing in.
    ///
    /// The component is deliberately data-only and never authoritative: it carries no health, no
    /// inventory and no loot, so there is nothing on it for a client to spoof. It only answers
    /// "how risky is this position", and the answer is read on the server by
    /// <see cref="RiskRules"/>, which owns every rule that turns a tier into a consequence.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ZoneRiskConfig : MonoBehaviour
    {
        [SerializeField]
        private ZoneRiskTier _tier = ZoneRiskTier.Safe;

        [SerializeField]
        private bool _allowsFlying;

        /// <summary>The tier this zone carries. Undeclared values are normalised to Safe on read.</summary>
        public ZoneRiskTier Tier => Normalize((int)_tier);

        /// <summary>
        /// Whether mounts may take off in this zone. False unless authored true, so flight fails
        /// closed: a new zone is a no-fly zone until someone deliberately says otherwise.
        /// </summary>
        public bool AllowsFlying => _allowsFlying;

        /// <summary>Reconfigures the zone's tier (an editor baker or a test).</summary>
        public void SetTier(ZoneRiskTier tier)
        {
            _tier = tier;
        }

        /// <summary>Reconfigures whether this zone allows mounts to fly (an editor baker or a test).</summary>
        public void SetAllowsFlying(bool allowsFlying)
        {
            _allowsFlying = allowsFlying;
        }

        /// <summary>
        /// Clamps a raw tier value to one of the declared tiers. Anything outside the range —
        /// a misauthored asset, a cast from a stray integer — fails toward
        /// <see cref="ZoneRiskTier.Safe"/> so a mistake can only ever make a zone safer.
        /// </summary>
        public static ZoneRiskTier Normalize(int raw)
        {
            ZoneRiskTier tier = (ZoneRiskTier)raw;
            return RiskRules.IsValidTier(tier) ? tier : ZoneRiskTier.Safe;
        }

        /// <summary>
        /// True when this zone governs <paramref name="worldPosition"/>: the attached collider
        /// contains it, or, with no collider, the zone is treated as covering its whole scene.
        /// </summary>
        public bool Contains(Vector3 worldPosition)
        {
            if (!TryGetComponent(out Collider collider) || collider == null)
                return true;

            return collider.bounds.Contains(worldPosition);
        }

        /// <summary>
        /// The tier in force at <paramref name="worldPosition"/>, scanning every
        /// <see cref="ZoneRiskConfig"/> in the scene. When zones overlap the most dangerous one
        /// wins — a safe pocket inside a black zone is still a black zone — and when none applies
        /// the world is Safe.
        /// </summary>
        public static ZoneRiskTier ResolveTierAt(Vector3 worldPosition)
        {
            ZoneRiskConfig[] zones = FindObjectsByType<ZoneRiskConfig>(FindObjectsSortMode.None);
            ZoneRiskTier resolved = ZoneRiskTier.Safe;
            for (int i = 0; i < zones.Length; i++)
            {
                if (zones[i] == null || !zones[i].Contains(worldPosition))
                    continue;

                ZoneRiskTier tier = zones[i].Tier;
                if (tier > resolved)
                    resolved = tier;
            }

            return resolved;
        }

        /// <summary>
        /// True when any zone governing <paramref name="worldPosition"/> allows flying. Mirrors
        /// <see cref="ResolveTierAt"/>: a zone covers its collider, or the whole scene when it has
        /// no collider, and flight is allowed if at least one covering zone opts in. With no zone
        /// at all the world is a no-fly zone, so a takeoff is refused rather than assumed safe.
        /// </summary>
        public static bool ResolveAllowsFlyingAt(Vector3 worldPosition)
        {
            ZoneRiskConfig[] zones = FindObjectsByType<ZoneRiskConfig>(FindObjectsSortMode.None);
            for (int i = 0; i < zones.Length; i++)
            {
                if (zones[i] != null && zones[i].Contains(worldPosition) && zones[i].AllowsFlying)
                    return true;
            }

            return false;
        }
    }
}
