using System;
using UnityEngine;

namespace MMO.Gameplay
{
    /// <summary>
    /// The ability bar an action-bar view binds to: how many slots exist, what each one is
    /// called, the change event that repaints the labels, and the owner-side cast request a
    /// slot click sends.
    ///
    /// <para>
    /// <see cref="PlayerStats"/>'s neighbour <see cref="PlayerAbilities"/> is the networked
    /// implementation: slot names are replicated one <c>SyncVar</c> each and the view repaints
    /// from their <c>OnChange</c> callbacks. Cooldowns are deliberately not part of this
    /// interface — the authoritative tracker lives server-side and is not replicated, so a bar
    /// that guessed at readiness would be showing invented state.
    /// </para>
    /// </summary>
    public interface IAbilityBarSource
    {
        /// <summary>How many slots the bar has; the view builds exactly this many.</summary>
        int SlotCount { get; }

        /// <summary>The name replicated into <paramref name="slot"/>, or empty when the slot is empty or out of range.</summary>
        string GetSlotName(int slot);

        /// <summary>
        /// Owner-side intent: cast whatever sits in <paramref name="slot"/> at
        /// <paramref name="targetPosition"/>. The source validates and the server decides; the
        /// view never assumes a cast succeeded.
        /// </summary>
        void RequestCast(int slot, Vector3 targetPosition);

        /// <summary>Raised whenever a replicated slot name changes, e.g. after equipment reshapes the bar.</summary>
        event Action SlotsChanged;
    }
}
