namespace MMO.UI
{
    /// <summary>
    /// Which vital a <see cref="VitalBarView"/> shows — the bar reads the matching pair of
    /// current/max values from its bound <c>IVitalsSource</c>.
    /// </summary>
    public enum VitalKind
    {
        /// <summary>Health: current against <c>EffectiveMaxHealth</c>.</summary>
        Health,

        /// <summary>Mana: current against <c>EffectiveMaxMana</c>.</summary>
        Mana,
    }
}
