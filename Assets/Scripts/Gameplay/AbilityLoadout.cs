using System;
using System.Collections.Generic;

namespace MMO.Gameplay
{
    /// <summary>
    /// The wearer's active ability bar: a fixed set of slots that the server fills from equipped
    /// gear. This is the runtime container the issue's "you are what you wear" mechanic injects
    /// into — a <see cref="WeaponData"/> and an <see cref="ArmorData"/> each hand over their
    /// <c>Abilities</c> arrays and the loadout decides what actually lands in a slot.
    ///
    /// Pure C# on purpose. No FishNet types, no scene lookups, no clock: everything about
    /// "which ability is in slot N" is decided here so an EditMode test can exercise the whole
    /// injection path with three ScriptableObjects and no server.
    ///
    /// The loadout is a snapshot of gear, not a log of it: re-equipping rebuilds the bar from
    /// scratch, weapon abilities first (the weapon is the primary skill source) then armor,
    /// dropping anything that will not fit and collapsing duplicates so wearing a sword and a
    /// plate that both grant Cleave does not waste two slots on one skill.
    /// </summary>
    public sealed class AbilityLoadout
    {
        /// <summary>Slots on the bar. Six is the whole action bar; gear beyond it is dropped.</summary>
        public const int MaxSlots = 6;

        private readonly AbilityData[] _slots = new AbilityData[MaxSlots];

        /// <summary>Fixed-length view of the bar; empty slots are null entries.</summary>
        public IReadOnlyList<AbilityData> Slots => _slots;

        /// <summary>The ability in <paramref name="slot"/>, or null when the slot is empty or invalid.</summary>
        public AbilityData GetSlot(int slot)
        {
            return slot >= 0 && slot < _slots.Length ? _slots[slot] : null;
        }

        /// <summary>Empties the bar. Used by <see cref="RebuildFromGear"/> before refilling it.</summary>
        public void Clear()
        {
            Array.Clear(_slots, 0, _slots.Length);
        }

        /// <summary>
        /// Rebuilds the whole bar from a piece of gear of each type. Either piece may be null
        /// (a bare-handed fighter wears no armor), and abilities the gear does not grant are
        /// simply not there afterwards.
        /// </summary>
        public void RebuildFromGear(WeaponData weapon, ArmorData armor)
        {
            Clear();

            int next = Fill(weapon != null ? weapon.Abilities : null, 0);
            Fill(armor != null ? armor.Abilities : null, next);
        }

        /// <summary>
        /// Copies <paramref name="granted"/> into the bar starting at <paramref name="start"/>,
        /// skipping nulls and abilities already equipped, and returns the next free slot.
        /// </summary>
        private int Fill(IReadOnlyList<AbilityData> granted, int start)
        {
            if (granted == null)
                return start;

            int slot = start;
            for (int i = 0; i < granted.Count && slot < _slots.Length; i++)
            {
                AbilityData ability = granted[i];
                if (ability == null || Contains(ability))
                    continue;

                _slots[slot++] = ability;
            }

            return slot;
        }

        private bool Contains(AbilityData ability)
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                if (ReferenceEquals(_slots[i], ability))
                    return true;
            }

            return false;
        }
    }
}
