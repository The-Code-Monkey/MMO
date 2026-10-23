using MMO.Inventory;

namespace MMO.Content
{
    /// <summary>
    /// One active aura on a target. Plain state with no Unity object beyond the definition and the
    /// server-side source, so the rules can be exercised without a spawned server object.
    /// </summary>
    public struct AuraState
    {
        public AuraData Data;
        public double ExpiresAt;
        public double NextTickAt;
        public int Stacks;

        /// <summary>Who applied the aura; a damage-over-time kill credits this player.</summary>
        public PlayerInventory Source;

        public bool IsActive => Data != null && Stacks > 0;
    }

    /// <summary>The outcome of one <see cref="AuraRules.Tick"/> call.</summary>
    public readonly struct AuraTickResult
    {
        public AuraTickResult(bool ticked, int damage, int heal, bool expired, AuraState state)
        {
            Ticked = ticked;
            Damage = damage;
            Heal = heal;
            Expired = expired;
            State = state;
        }

        public bool Ticked { get; }
        public int Damage { get; }
        public int Heal { get; }
        public bool Expired { get; }
        public AuraState State { get; }
    }
}
