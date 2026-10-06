namespace MMO.Gameplay
{
    /// <summary>
    /// What an ability does when the server resolves it.
    ///
    /// This is the "Ability Logic" half of <see cref="AbilityData"/>: an enum rather than a
    /// class hierarchy, so the whole skill stays serializable into a <c>ScriptableObject</c>
    /// asset, diffable in YAML, and testable without spawning a single object. The shipped
    /// skills map onto it directly — Fireball is <see cref="TargetedDamage"/>, Cleave is
    /// <see cref="AreaDamage"/>, and a potion-like self-heal is <see cref="SelfHeal"/>.
    /// </summary>
    public enum AbilityLogic
    {
        /// <summary>
        /// Damage every valid target within <c>EffectRadius</c> of the point the caster aimed at.
        /// The server range-checks the cast against <c>CastRange</c> before this ever runs.
        /// </summary>
        TargetedDamage = 0,

        /// <summary>
        /// Damage every valid target within <c>EffectRadius</c> of the caster itself — the
        /// melee cleave that ignores where the mouse pointed.
        /// </summary>
        AreaDamage = 1,

        /// <summary>
        /// Heal the caster. Self-cast, so it skips the distance check entirely.
        /// </summary>
        SelfHeal = 2,
    }
}
