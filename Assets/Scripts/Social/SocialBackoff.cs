using System.Threading.Tasks;

namespace MMO.Social
{
    /// <summary>Retry delays for the social CAS loops.</summary>
    internal static class SocialBackoff
    {
        public static Task DelayAsync(int attempt)
            => Task.Delay(50 * (1 << attempt));
    }
}
