using UnityEngine;

namespace MMO.Gameplay
{
    /// <summary>
    /// The live implementation of <see cref="IServerSight"/>: one raycast from the caster's eye
    /// toward the target. This is the only file that touches <see cref="Physics"/>, which is what
    /// keeps <see cref="SightRules"/> free of the engine and every other LoS decision testable
    /// without a scene.
    ///
    /// <para>
    /// A hit on the target itself is a clear line, not cover: the ray is aimed at the target's
    /// centre, so its own collider is the last thing it meets. Anything hit before that — a wall,
    /// a crate, another player — blocks. A zero-length line is trivially clear.
    /// </para>
    /// </summary>
    public sealed class PhysicsSight : IServerSight
    {
        /// <summary>
        /// Height above a capsule's pivot that stands in for its eyes. The capsule
        /// (<see cref="MMO.Net.PlayerPrefabFactory.DefaultHeight"/>, two metres) puts the eye at
        /// the top of the head, clear of the caster's own collider so a ray never starts inside it.
        /// </summary>
        public const float DefaultEyeHeight = 1.5f;

        private readonly int _layerMask;

        /// <param name="layerMask">
        /// What counts as cover. Everything by default; a project that puts players on their own
        /// layer can narrow it to the world.
        /// </param>
        public PhysicsSight(int layerMask = ~0)
        {
            _layerMask = layerMask;
        }

        /// <inheritdoc />
        public bool HasLineOfSight(Vector3 eyePosition, Vector3 targetPosition, GameObject target)
        {
            Vector3 delta = targetPosition - eyePosition;
            float distance = delta.magnitude;

            // A target at the eye has no direction to be blocked along.
            if (distance <= Mathf.Epsilon)
                return true;

            if (!Physics.Raycast(eyePosition, delta / distance, out RaycastHit hit, distance,
                    _layerMask, QueryTriggerInteraction.Ignore))
            {
                return true;
            }

            // The ray ended on the thing we aimed at, which is the target, not cover in front of it.
            return target != null && hit.collider != null && hit.collider.transform.IsChildOf(target.transform);
        }
    }
}
