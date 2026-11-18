using System;

namespace MMO.Gameplay
{
    /// <summary>
    /// The flag a HUD binds to: whether the local player has opted into PvP, whether the zone
    /// forces it, how long the opt-out cooldown has left, and the one request a client may make.
    ///
    /// <para>
    /// <see cref="PlayerPvP"/> is the networked implementation. Like every other view source in
    /// this project it publishes an event raised from the replicated state rather than exposing a
    /// timer, so a view repaints exactly when the server's value changes and a test can drive it
    /// from a plain fake.
    /// </para>
    /// </summary>
    public interface IPvPSource
    {
        /// <summary>True when this player has opted into PvP.</summary>
        bool IsFlagged { get; }

        /// <summary>True when the zone the player stands in forces PvP, making the flag irrelevant.</summary>
        bool IsForced { get; }

        /// <summary>Whole seconds left on the opt-out cooldown, as replicated by the server.</summary>
        float OptOutCooldown { get; }

        /// <summary>Raised whenever any of the three values above may have changed.</summary>
        event Action FlagChanged;

        /// <summary>
        /// Asks the server to set the flag. The caller decides nothing: the request can be refused
        /// by the cooldown or by a forced zone, and the display only moves when the server's value
        /// does.
        /// </summary>
        void RequestFlag(bool flagged);
    }
}
