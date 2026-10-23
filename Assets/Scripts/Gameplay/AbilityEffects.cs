using System.Collections.Generic;
using UnityEngine;

namespace MMO.Gameplay
{
    /// <summary>
    /// The geometry and arithmetic of resolving an accepted cast: where an effect originates,
    /// whether a point is inside it, how hard it hits, and who gets hit.
    ///
    /// The networked component does the only impure part — asking the scene for
    /// candidates, the <see cref="IServerSight"/> raycast behind a seam, and pushing the result
    /// into <c>PlayerStats</c> — while every decision that
    /// could be wrong (wrong origin for a cleave, a negative power sneaking in from hand-edited
    /// YAML, the caster being hit by their own AreaDamage) is decided here and covered by
    /// EditMode tests.
    /// </summary>
    public static class AbilityEffects
    {
        /// <summary>
        /// The point an effect measures from. Targeted damage explodes where the caster aimed;
        /// anything centred on the caster (cleaves, self-heals, taunt) uses the caster's own position,
        /// so a client cannot choose an origin for them. Heal uses the target position like targeted damage.
        /// </summary>
        public static Vector3 ResolveOrigin(AbilityLogic logic, Vector3 casterPosition, Vector3 targetPosition)
        {
            switch (logic)
            {
                case AbilityLogic.AreaDamage:
                case AbilityLogic.SelfHeal:
                case AbilityLogic.Taunt:
                    return casterPosition;
                default:
                    // Unknown enum values are hand-edited assets: treat them like the ranged
                    // case, which is the one that was range-checked at cast time.
                    return targetPosition;
            }
        }

        /// <summary>
        /// True when <paramref name="position"/> lies inside the effect centred on
        /// <paramref name="origin"/>. A negative radius is malformed authoring, not a bigger
        /// hole, so it clamps to zero rather than inverting the test.
        /// </summary>
        public static bool Hits(Vector3 origin, Vector3 position, float radius)
        {
            return Vector3.Distance(origin, position) <= Mathf.Max(0f, radius);
        }

        /// <summary>Damage or healing the effect applies, floored at zero.</summary>
        public static int ComputePower(AbilityData ability)
        {
            return ability == null ? 0 : Mathf.Max(0, ability.EffectPower);
        }

        /// <summary>
        /// Who an effect landing at <paramref name="origin"/> touches: every non-null candidate
        /// inside the radius except <paramref name="self"/>. Excluding the caster is what keeps
        /// <see cref="AbilityLogic.AreaDamage"/> from being a suicide button, and doing it here
        /// (rather than at each call site) means the rule cannot be forgotten.
        /// </summary>
        public static List<PlayerStats> SelectVictims(
            Vector3 origin,
            float radius,
            PlayerStats self,
            IReadOnlyList<PlayerStats> candidates)
        {
            var victims = new List<PlayerStats>();
            if (candidates == null)
                return victims;

            for (int i = 0; i < candidates.Count; i++)
            {
                PlayerStats candidate = candidates[i];
                if (candidate == null || candidate == self)
                    continue;

                if (Hits(origin, candidate.transform.position, radius))
                    victims.Add(candidate);
            }

            return victims;
        }

        /// <summary>
        /// The subset of <paramref name="victims"/> the caster can actually see from
        /// <paramref name="eyePosition"/>: a fireball centred just past a wall damages only what
        /// the wall did not hide. A null <paramref name="sight"/> is treated as a clear line, so a
        /// caller with no sight wired keeps the old behaviour rather than silently hitting nobody.
        /// </summary>
        public static List<PlayerStats> SelectVisibleVictims(
            Vector3 eyePosition,
            IReadOnlyList<PlayerStats> victims,
            IServerSight sight)
        {
            var visible = new List<PlayerStats>();
            if (victims == null)
                return visible;

            for (int i = 0; i < victims.Count; i++)
            {
                PlayerStats victim = victims[i];
                if (victim == null)
                    continue;

                if (sight == null || sight.HasLineOfSight(eyePosition, victim.transform.position, victim.gameObject))
                    visible.Add(victim);
            }

            return visible;
        }

        /// <summary>
        /// The NPC half of <see cref="SelectVictims"/>: every non-null enemy inside the effect.
        /// The caster is a player, so there is no self to exclude here.
        /// </summary>
        public static List<NPCStats> SelectNpcVictims(
            Vector3 origin,
            float radius,
            IReadOnlyList<NPCStats> candidates)
        {
            var victims = new List<NPCStats>();
            if (candidates == null)
                return victims;

            for (int i = 0; i < candidates.Count; i++)
            {
                NPCStats candidate = candidates[i];
                if (candidate != null && Hits(origin, candidate.transform.position, radius))
                    victims.Add(candidate);
            }

            return victims;
        }

        /// <summary>
        /// The NPC half of <see cref="SelectVisibleVictims"/>: an enemy behind cover takes no
        /// damage. A null sight is treated as clear, matching the player path.
        /// </summary>
        public static List<NPCStats> SelectVisibleNpcVictims(
            Vector3 eyePosition,
            IReadOnlyList<NPCStats> victims,
            IServerSight sight)
        {
            var visible = new List<NPCStats>();
            if (victims == null)
                return visible;

            for (int i = 0; i < victims.Count; i++)
            {
                NPCStats victim = victims[i];
                if (victim == null)
                    continue;

                if (sight == null || sight.HasLineOfSight(eyePosition, victim.transform.position, victim.gameObject))
                    visible.Add(victim);
            }

            return visible;
        }
    }
}
