using System;

namespace MMO.Content
{
    /// <summary>
    /// Pure spawn-controller arithmetic: how many enemies a point should keep alive for the nearby
    /// player count, and when a defeated enemy is due back.
    /// </summary>
    public static class SpawnRules
    {
        /// <summary>
        /// Nearby players scale the target population, clamped to the authored range. No players
        /// means no population; the minimum only applies once at least one player is present.
        /// </summary>
        public static int TargetAlive(int minAlive, int maxAlive, int playersNear, int perPlayer)
        {
            if (playersNear <= 0)
                return 0;

            int min = Math.Max(0, minAlive);
            int max = Math.Max(min, maxAlive);
            int per = Math.Max(0, perPlayer);
            long target = (long)playersNear * per;
            return (int)Math.Min(max, Math.Max(min, target));
        }

        /// <summary>True when the respawn delay has elapsed on the server's clock.</summary>
        public static bool IsRespawnDue(double lastDeathAt, double respawnSeconds, double now)
        {
            return now >= lastDeathAt + Math.Max(0d, respawnSeconds);
        }
    }
}
