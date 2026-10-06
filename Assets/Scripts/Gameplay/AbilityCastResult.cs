namespace MMO.Gameplay
{
    /// <summary>
    /// Why a cast was accepted or refused, decided solely by the server.
    ///
    /// Every rejection reason the issue asks for has its own value so the server log (and,
    /// later, the combat log UI fed from it) can say *why* a skill did nothing instead of the
    /// client guessing. The order the reasons are produced in is pinned by
    /// <c>AbilityCastRulesTests</c> so a refactor cannot quietly change which complaint wins
    /// when a cast breaks more than one rule at once.
    /// </summary>
    public enum AbilityCastResult : byte
    {
        /// <summary>All checks passed; mana and cooldowns have been booked.</summary>
        Success = 0,

        /// <summary>The slot index is outside the loadout entirely.</summary>
        InvalidSlot = 1,

        /// <summary>The slot exists but no equipped gear injected an ability into it.</summary>
        EmptySlot = 2,

        /// <summary>Another ability with a cast time is still resolving.</summary>
        AlreadyCasting = 3,

        /// <summary>The Global Cooldown started by the previous cast has not elapsed.</summary>
        GlobalCooldownActive = 4,

        /// <summary>This particular slot is still on its own cooldown.</summary>
        SlotOnCooldown = 5,

        /// <summary>The caster does not have the mana the ability costs.</summary>
        NotEnoughMana = 6,

        /// <summary>The target is further away than the ability's cast range allows.</summary>
        OutOfRange = 7,
    }
}
