using UnityEngine;

namespace MMO.UI
{
    /// <summary>
    /// The default font the code-built screens label themselves with.
    ///
    /// <para>
    /// The project has no committed font asset — it has no committed UI assets at all — so the
    /// views fall back to the font Unity ships in the player. A missing builtin is swallowed
    /// rather than thrown: labels degrading to invisible is bad, but a factory that takes down
    /// the screen it was building is worse, and callers can always assign their own font
    /// afterwards.
    /// </para>
    /// </summary>
    public static class UIFont
    {
        private static Font _default;
        private static bool _resolved;

        /// <summary>The engine's legacy runtime font, or null when it cannot be resolved.</summary>
        public static Font Default
        {
            get
            {
                if (!_resolved)
                {
                    _resolved = true;

                    // Unity 6 renamed/shipped built-in fonts differently across versions; try the
                    // known names rather than assuming one exists. A missing builtin is swallowed
                    // rather than thrown: labels degrading to invisible is bad, but a factory that
                    // takes down the screen it was building is worse.
                    _default = TryLoad("LegacyRuntime.ttf")
                               ?? TryLoad("Arial.ttf")
                               ?? TryLoad("Inter-Regular.ttf");
                }

                return _default;
            }
        }

        private static Font TryLoad(string resourceName)
        {
            try
            {
                return Resources.GetBuiltinResource<Font>(resourceName);
            }
            catch (System.Exception)
            {
                return null;
            }
        }
    }
}
