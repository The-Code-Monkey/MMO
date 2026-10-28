using System.Collections.Generic;
using FishNet.Object;
using MMO.Gameplay;
using UnityEngine;
using UnityEngine.AI;

namespace MMO.Net
{
    /// <summary>Chooses and applies a joining player's authoritative spawn position.</summary>
    public static class PlayerJoinPlacement
    {
        /// <summary>
        /// How far a saved position may be from the zone NavMesh and still count as the same
        /// place. Wider than the movement gate because a saved capsule pose is not a mesh point.
        /// </summary>
        private const float RestoreSampleDistance = 2f;

        public static void Place(
            NetworkObject player,
            Vector3? savedPosition,
            IReadOnlyList<PlayerSpawnPoint> points)
        {
            Vector3? restored = NormaliseSavedPosition(points, savedPosition);
            bool restoring = restored.HasValue && IsOnZoneNavMesh(restored.Value);
            Vector3 position = PlayerSpawnPoint.ChooseSpawn(points, restored, IsOnZoneNavMesh);

            PlayerMover mover = player.GetComponent<PlayerMover>();
            if (mover != null)
                mover.SetAuthoritativePosition(position);
            else
                player.transform.position = position;

            Debug.Log(restoring
                ? $"[WorldBootstrap] Restored saved position {position} for {player.name}."
                : $"[WorldBootstrap] Placed {player.name} at spawn point {position}.");
        }

        /// <summary>
        /// Server-only: whether a restored position is close enough to the zone NavMesh to stand at.
        /// The 1 m tolerance matches <see cref="PlayerMover"/>'s own click gate.
        /// </summary>
        private static bool IsOnZoneNavMesh(Vector3 position) =>
            NavMesh.SamplePosition(position, out _, RestoreSampleDistance, NavMesh.AllAreas);

        /// <summary>
        /// Snaps a saved capsule pose onto the zone NavMesh and lifts it back to the capsule's
        /// standing centre. The NavMesh sits slightly above the floor.
        /// </summary>
        private static Vector3? NormaliseSavedPosition(
            IReadOnlyList<PlayerSpawnPoint> points, Vector3? savedPosition)
        {
            if (!savedPosition.HasValue)
                return null;

            if (!NavMesh.SamplePosition(
                    savedPosition.Value, out NavMeshHit restored, RestoreSampleDistance, NavMesh.AllAreas))
            {
                return savedPosition;
            }

            float standingOffset = StandingOffset(points);
            return new Vector3(restored.position.x, restored.position.y + standingOffset, restored.position.z);
        }

        /// <summary>
        /// Distance from the zone NavMesh to a standing capsule centre, taken from an authored
        /// spawn point so flat and raised floors both use the scene's own convention.
        /// </summary>
        private static float StandingOffset(IReadOnlyList<PlayerSpawnPoint> points)
        {
            if (points == null || points.Count == 0)
                return 1f;

            PlayerSpawnPoint reference = null;
            foreach (PlayerSpawnPoint point in points)
            {
                if (point == null)
                    continue;

                if (reference == null || point.IsDefault)
                    reference = point;
            }

            if (reference == null ||
                !NavMesh.SamplePosition(
                    reference.transform.position, out NavMeshHit spawn, RestoreSampleDistance, NavMesh.AllAreas))
            {
                return 1f;
            }

            return Mathf.Max(0.5f, reference.transform.position.y - spawn.position.y);
        }
    }
}
