namespace MMO.Gameplay
{
    /// <summary>
    /// Boss phase enumeration. Mirrors WoW-style progression: melee, adds, enrage, defeated.
    /// </summary>
    public enum BossPhase
    {
        Melee = 0,
        Adds = 1,
        Enrage = 2,
        Defeated = 3,
    }

    /// <summary>
    /// Pure boss state machine: transitions on abstract triggers (health fraction, add count, time)
    /// with no Unity or clock dependency. Every transition is testable in EditMode.
    /// </summary>
    public sealed class BossStateMachine
    {
        /// <summary>Health fraction at which the boss transitions from Melee to Adds phase (60%).</summary>
        public const float AddPhaseAt = 0.60f;

        /// <summary>Health fraction at which the boss transitions from Adds to Enrage phase (25%).</summary>
        public const float EnrageAt = 0.25f;

        public BossPhase Phase { get; private set; } = BossPhase.Melee;

        /// <summary>
        /// Pure transition: called with the boss health fraction and live add count.
        /// Returns the new phase after evaluating transitions.
        /// </summary>
        /// <param name="healthFraction">Current health as a fraction of max (0.0 to 1.0).</param>
        /// <param name="liveAdds">Number of living adds currently spawned.</param>
        /// <param name="secondsInPhase">Seconds elapsed since entering the current phase.</param>
        /// <returns>The new phase after evaluation.</returns>
        public BossPhase Evaluate(float healthFraction, int liveAdds, double secondsInPhase)
        {
            return Phase switch
            {
                BossPhase.Melee when healthFraction <= AddPhaseAt => TransitionTo(BossPhase.Adds),
                BossPhase.Adds when liveAdds == 0 && healthFraction <= EnrageAt => TransitionTo(BossPhase.Enrage),
                BossPhase.Enrage when healthFraction <= 0f => TransitionTo(BossPhase.Defeated),
                _ => Phase,
            };
        }

        private BossPhase TransitionTo(BossPhase newPhase)
        {
            Phase = newPhase;
            return Phase;
        }

        /// <summary>Resets the state machine to the initial Melee phase.</summary>
        public void Reset()
        {
            Phase = BossPhase.Melee;
        }
    }
}