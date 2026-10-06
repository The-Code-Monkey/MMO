using System;
using System.Collections.Generic;

namespace MMO.Gameplay
{
    /// <summary>
    /// The server's bouncer: one pure function that decides whether a cast request is legal.
    ///
    /// Nothing here mutates anything and nothing reads a clock — mana, the bar, the cooldown
    /// tracker and the measured distance are all arguments — which is what lets the issue's
    /// validation requirements (mana cost, distance, Global Cooldown, per-slot cooldown) be
    /// pinned one rejection at a time in EditMode tests rather than being reachable only through
    /// a live connection.
    ///
    /// The checks run outside-in: structural problems with the request first, then timing, then
    /// the caster's own resources, and finally the world. A request that breaks several rules
    /// always reports the earliest one, so the reason a cast failed never depends on which
    /// validator a refactor happens to check first.
    /// </summary>
    public static class AbilityCastRules
    {
        /// <summary>
        /// Validates casting <c>slots[slot]</c> for a caster with <paramref name="currentMana"/>
        /// standing <paramref name="distanceToTarget"/> from where they aimed.
        /// </summary>
        /// <exception cref="ArgumentNullException">Slots or cooldowns missing — a server bug, not a cast.</exception>
        public static AbilityCastResult Validate(
            int slot,
            IReadOnlyList<AbilityData> slots,
            AbilityCooldownTracker cooldowns,
            int currentMana,
            float distanceToTarget,
            double now)
        {
            if (slots == null)
                throw new ArgumentNullException(nameof(slots));
            if (cooldowns == null)
                throw new ArgumentNullException(nameof(cooldowns));

            if (slot < 0 || slot >= slots.Count)
                return AbilityCastResult.InvalidSlot;

            AbilityData ability = slots[slot];
            if (ability == null)
                return AbilityCastResult.EmptySlot;

            if (cooldowns.IsCasting(now))
                return AbilityCastResult.AlreadyCasting;

            if (!cooldowns.IsGlobalCooldownReady(now))
                return AbilityCastResult.GlobalCooldownActive;

            if (!cooldowns.IsReady(slot, now))
                return AbilityCastResult.SlotOnCooldown;

            if (currentMana < ability.ManaCost)
                return AbilityCastResult.NotEnoughMana;

            // A self-heal is cast on oneself, so there is no aimed-at point to be out of range of.
            if (ability.Logic != AbilityLogic.SelfHeal && distanceToTarget > ability.CastRange)
                return AbilityCastResult.OutOfRange;

            return AbilityCastResult.Success;
        }
    }
}
