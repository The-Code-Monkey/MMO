using System;

namespace MMO.Gameplay
{
    /// <summary>
    /// What a mount-progress bar needs to draw: the current mount state and how far through the
    /// current mount or dismount cast the player is.
    ///
    /// <see cref="PlayerMount"/> is the networked implementation; the interface exists so the bar
    /// can be driven by a plain fake in an EditMode test, like every other HUD source.
    /// </summary>
    public interface IMountSource
    {
        /// <summary>The replicated mount state.</summary>
        MountState State { get; }

        /// <summary>0 at the start of a cast, 1 when it completes; 0 when no cast is running.</summary>
        float CastProgress { get; }

        /// <summary>Raised when the state or the cast progress changes.</summary>
        event Action MountChanged;
    }
}
