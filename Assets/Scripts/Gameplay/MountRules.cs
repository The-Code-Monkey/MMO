using System;

namespace MMO.Gameplay
{
    /// <summary>
    /// The pure carrying rules of a mount: the slots it adds and what happens when a player gets
    /// off while still carrying more than the base bag holds. Items are never dropped, so the
    /// consequence is speed, not loss: every item above the base capacity slows the player by
    /// <see cref="OverloadPenaltyPerItem"/>.
    ///
    /// Time- and Unity-free like the other rules classes, so an EditMode test can pin the boundary
    /// (48 items = no penalty, 49 = 0.9×) without a server.
    /// </summary>
    public static class MountRules
    {
        /// <summary>Speed lost per item carried above the base capacity while unmounted.</summary>
        public const float OverloadPenaltyPerItem = 0.1f;

        /// <summary>A floor, so a badly overloaded player is slow but never frozen.</summary>
        public const float MinimumSpeedMultiplier = 0.1f;

        /// <summary>How many items over capacity the player is carrying; zero when within it.</summary>
        public static int OverloadCount(int itemCount, int carryingCapacity)
        {
            return Math.Max(0, itemCount - Math.Max(0, carryingCapacity));
        }

        /// <summary>
        /// The speed multiplier for a dismounted player: 1 while within capacity, then 0.1 lower
        /// per extra item, never below <see cref="MinimumSpeedMultiplier"/>.
        /// </summary>
        public static float UnmountedSpeedMultiplier(int itemCount, int carryingCapacity)
        {
            int overload = OverloadCount(itemCount, carryingCapacity);
            return Math.Max(MinimumSpeedMultiplier, 1f - OverloadPenaltyPerItem * overload);
        }
    }
}
