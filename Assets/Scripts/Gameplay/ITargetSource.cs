using System;

namespace MMO.Gameplay
{
    /// <summary>
    /// The vitals of whatever the target frame is currently showing: a name to label it with,
    /// a health pool to fill against, and the change event that repaints the frame.
    ///
    /// <para>
    /// <see cref="NPCStats"/> is the networked implementation — the target dummy's health is a
    /// <c>SyncVar</c>, and the frame's repaint rides the same <c>OnChange</c> callback the
    /// networked component already subscribes to. Nothing polls: a frame showing an untouched
    /// target costs nothing after the initial paint.
    /// </para>
    /// </summary>
    public interface ITargetSource
    {
        /// <summary>Display name for the frame's title, typically the target's GameObject name.</summary>
        string TargetName { get; }

        /// <summary>Current health, as last replicated by the server.</summary>
        int Health { get; }

        /// <summary>The health cap this target is measured against.</summary>
        int MaxHealth { get; }

        /// <summary>Raised whenever <see cref="Health"/> changes on this target.</summary>
        event Action Changed;
    }
}
