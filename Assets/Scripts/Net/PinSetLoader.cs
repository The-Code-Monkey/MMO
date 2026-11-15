using UnityEngine;

namespace MMO.Net
{
    /// <summary>
    /// Reads the pin set shipped with the build. The document lives at
    /// <c>Assets/Resources/Net/PinSet.json</c>, which Unity imports as a <c>TextAsset</c>, so a
    /// platform build can swap it without a code change and the release pipeline (#85) can attach
    /// the environment's set.
    ///
    /// A missing document returns null. That is not an error on its own — a development build with
    /// pinning off ships no pins — but a build that requires pinning must include a well-formed
    /// one, which <c>PinningBuildGuard</c> enforces before the player ever runs.
    /// </summary>
    public static class PinSetLoader
    {
        /// <summary>Resources path of the pin document, without its <c>.json</c> extension.</summary>
        public const string ResourcePath = "Net/PinSet";

        /// <summary>The shipped pin set, or null when the document is absent or malformed.</summary>
        public static PinSet Load()
        {
            TextAsset asset = Resources.Load<TextAsset>(ResourcePath);
            return asset != null ? PinSet.FromJson(asset.text) : null;
        }
    }
}
