using System.Collections.Generic;
using UnityEngine;

namespace MMO.Net
{
    /// <summary>
    /// A scene-authored marker for "a player may appear here". The scene holds these; the
    /// <em>server</em> decides which one a joining player is placed at via
    /// <see cref="ChooseSpawn"/> — a client never picks its own spawn. FishNet's own
    /// <c>PlayerSpawner</c> is given the same transforms by <see cref="WorldBootstrap"/>, so the
    /// initial server-side spawn and the authoritative restore land in the same places.
    /// </summary>
    public sealed class PlayerSpawnPoint : MonoBehaviour
    {
        [SerializeField]
        private bool _isDefault;

        /// <summary>
        /// True when this is the zone's default spawn — where a player with no saved position
        /// (fresh character, backend unreachable) appears.
        /// </summary>
        public bool IsDefault => _isDefault;

        /// <summary>Sets <see cref="IsDefault"/>; for bakers and tests.</summary>
        public void SetDefault(bool isDefault) => _isDefault = isDefault;

        /// <summary>
        /// Picks the spawn position for a joining player: the point
        /// <em>nearest to their saved position</em> — a zone transfer or relog puts them back
        /// where they left off, and a saved position that falls outside the zone (different
        /// map, resized zone) clamps to the nearest point instead of dropping them through the
        /// floor. With no saved position the default point wins; with no points at all the
        /// origin is returned rather than a null-reference.
        /// </summary>
        /// <param name="points">Every spawn point in the zone; may be empty or null.</param>
        /// <param name="savedPosition">
        /// The position from the loaded save record, or null when there is none (fresh
        /// character, or the backend could not be reached).
        /// </param>
        public static Vector3 ChooseSpawn(IReadOnlyList<PlayerSpawnPoint> points, Vector3? savedPosition)
        {
            if (points == null || points.Count == 0)
                return Vector3.zero;

            PlayerSpawnPoint fallback = null;
            foreach (PlayerSpawnPoint point in points)
            {
                if (point == null)
                    continue;

                if (fallback == null || (point._isDefault && !fallback._isDefault))
                    fallback = point;
            }

            if (fallback == null)
                return Vector3.zero;

            if (savedPosition == null)
                return fallback.transform.position;

            PlayerSpawnPoint nearest = null;
            float nearestDistance = float.MaxValue;

            foreach (PlayerSpawnPoint point in points)
            {
                if (point == null)
                    continue;

                float distance = (point.transform.position - savedPosition.Value).sqrMagnitude;
                bool better = nearest == null
                    || distance < nearestDistance
                    || (distance == nearestDistance && point._isDefault && !nearest._isDefault);

                if (better)
                {
                    nearest = point;
                    nearestDistance = distance;
                }
            }

            return (nearest ?? fallback).transform.position;
        }
    }
}
