using System;
using System.Collections.Generic;
using UnityEngine;

namespace MMO.Gameplay
{
    /// <summary>
    /// The server-side brain of an active ability bar: which gear injected which skill, when
    /// each slot and the Global Cooldown unlock, and what is still channelling.
    ///
    /// This is a plain C# object rather than a <c>NetworkBehaviour</c> on purpose. Everything the
    /// issue calls "server-tracked" — cooldown bookkeeping, mana and distance validation, the
    /// cast queue — lives here with no FishNet dependency, so the entire pipeline can be driven
    /// from an EditMode test with a fake mana pool and a chosen <c>now</c>. The networked
    /// component (<see cref="PlayerAbilities"/>) only supplies the real mana pool, the real clock
    /// and the world; it never makes a decision of its own.
    ///
    /// Ownership: one instance belongs to one player, written only from the server's side of a
    /// validated request. Nothing on this type is reachable from a client.
    /// </summary>
    public sealed class AbilityCaster
    {
        private readonly AbilityLoadout _loadout = new();
        private readonly AbilityCooldownTracker _cooldowns = new();
        private readonly List<PendingCast> _pending = new();

        /// <summary>The bar as the last equip left it. Authoritative only on the server.</summary>
        public IReadOnlyList<AbilityData> Slots => _loadout.Slots;

        public AbilityCooldownTracker Cooldowns => _cooldowns;

        /// <summary>Casts accepted but not yet resolved. Zero-length casts leave this at once.</summary>
        public int PendingCastCount => _pending.Count;

        /// <summary>
        /// Rebuilds the bar from a weapon and a piece of armor. Casts still in flight are
        /// dropped: they were started by gear the player is no longer wearing, and resolving
        /// them afterwards would fire an ability the bar no longer contains. Cooldowns are
        /// deliberately kept — they belong to the slot, not to the gear that filled it.
        /// </summary>
        public void EquipGear(WeaponData weapon, ArmorData armor)
        {
            _loadout.RebuildFromGear(weapon, armor);
            _pending.Clear();
        }

        /// <summary>
        /// Validates and books a cast. On success the caller must take
        /// <paramref name="manaCost"/> from the caster's mana pool; the cast itself is queued
        /// for <c>TakeDueEffects</c> and every cooldown involved is already running.
        /// </summary>
        /// <param name="currentMana">The server's current view of the caster's mana.</param>
        /// <param name="casterPosition">Where the caster stands — measured against where they aimed.</param>
        /// <param name="targetPosition">Where the caster aimed at the moment they pressed the button.</param>
        /// <param name="now">The server's clock; never read from inside this type.</param>
        /// <param name="manaCost">What to charge. Zero whenever the cast is refused.</param>
        public AbilityCastResult TryCast(
            int slot,
            int currentMana,
            Vector3 casterPosition,
            Vector3 targetPosition,
            double now,
            out int manaCost)
        {
            manaCost = 0;

            float distance = Vector3.Distance(casterPosition, targetPosition);
            AbilityCastResult result = AbilityCastRules.Validate(
                slot, _loadout.Slots, _cooldowns, currentMana, distance, now);

            if (result != AbilityCastResult.Success)
                return result;

            AbilityData ability = _loadout.GetSlot(slot);
            manaCost = ability.ManaCost;

            // Everything below is booking, not deciding: the rules already said yes.
            double resolveAt = now + Math.Max(0d, ability.CastTime);
            _cooldowns.BeginCastUntil(resolveAt);
            _cooldowns.StartGlobalCooldown(now);

            // The slot's own cooldown runs from the moment the effect lands, so a long cast
            // time does not silently eat into the recovery the asset promised.
            _cooldowns.StartCooldown(slot, ability.Cooldown, resolveAt);
            _pending.Add(new PendingCast(slot, ability, targetPosition, resolveAt));

            return AbilityCastResult.Success;
        }

        /// <summary>
        /// Removes and returns every cast whose resolve time has arrived, oldest first. Callers
        /// poll this from their update loop; casts with a zero cast time are returned by the very
        /// next call after <see cref="TryCast"/>.
        /// </summary>
        public List<AbilityCastEffect> TakeDueEffects(double now)
        {
            var due = new List<AbilityCastEffect>();

            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                if (_pending[i].ResolveAt > now)
                    continue;

                PendingCast cast = _pending[i];
                _pending.RemoveAt(i);
                due.Add(new AbilityCastEffect(cast.Slot, cast.Ability, cast.TargetPosition));
            }

            // Walked newest-to-oldest for cheap removal, so flip back to start order.
            due.Reverse();
            return due;
        }

        private readonly struct PendingCast
        {
            public PendingCast(int slot, AbilityData ability, Vector3 targetPosition, double resolveAt)
            {
                Slot = slot;
                Ability = ability;
                TargetPosition = targetPosition;
                ResolveAt = resolveAt;
            }

            public int Slot { get; }
            public AbilityData Ability { get; }
            public Vector3 TargetPosition { get; }
            public double ResolveAt { get; }
        }
    }
}
