using System;
using System.Threading.Tasks;

namespace MMO.Social
{
    /// <summary>Retry delays for the raid CAS loops.</summary>
    internal static class RaidBackoff
    {
        public static Task DelayAsync(int attempt)
            => Task.Delay(Math.Min(8, 1 << attempt) * 10);
    }
}
