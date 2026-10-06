using UnityEngine;

namespace MMO.Gameplay
{
    /// <summary>
    /// One cast the server has accepted and not yet resolved: which ability was started, where
    /// the caster aimed, and the exact time the effect lands.
    ///
    /// Produced by <see cref="AbilityCaster.TakeDueEffects"/> and consumed by the networked
    /// component that applies it to the world. Keeping it a plain immutable struct means the
    /// scheduling half of casting can be tested without a scene, a server, or a FishNet object.
    /// </summary>
    public readonly struct AbilityCastEffect
    {
        public AbilityCastEffect(int slot, AbilityData ability, Vector3 targetPosition)
        {
            Slot = slot;
            Ability = ability;
            TargetPosition = targetPosition;
        }

        /// <summary>Bar slot the cast came from — enough to log or animate the right button.</summary>
        public int Slot { get; }

        public AbilityData Ability { get; }

        /// <summary>
        /// Where the caster aimed when they started the cast, captured at request time so a
        /// caster who moved during the cast time still resolves against the original target.
        /// </summary>
        public Vector3 TargetPosition { get; }
    }
}
