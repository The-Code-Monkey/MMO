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

                    try
                    {
                        _default = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                    }
                    catch (System.Exception)
                    {
                        _default = null;
                    }
                }

                return _default;
            }
        }
    }
}
