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
        /// Picks the spawn position for a joining player. A saved position is restored verbatim —
        /// a relog puts the player back exactly where they logged out — but only when it is
        /// walkable, so a save from another map or a resized zone lands on the nearest spawn point
        /// rather than through the floor. With no saved position (fresh character, backend
        /// unreachable, or an invalid saved spot) the zone's default point wins; with no points at
        /// all the origin is returned rather than a null-reference.
        /// </summary>
        /// <param name="points">Every spawn point in the zone; may be empty or null.</param>
        /// <param name="savedPosition">
        /// The position from the loaded save record, or null when there is none (fresh
        /// character, or the backend could not be reached).
        /// </param>
        /// <param name="isWalkable">
        /// Judges whether a restored position is valid to stand at; null means "always accept",
        /// which keeps this rule pure and engine-free for tests. The live caller passes the zone
        /// NavMesh sample so a stale or out-of-bounds save cannot drop the player off the mesh.
        /// </param>
        public static Vector3 ChooseSpawn(
            IReadOnlyList<PlayerSpawnPoint> points,
            Vector3? savedPosition,
            System.Func<Vector3, bool> isWalkable = null)
        {
            if (savedPosition.HasValue && (isWalkable == null || isWalkable(savedPosition.Value)))
                return savedPosition.Value;

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
