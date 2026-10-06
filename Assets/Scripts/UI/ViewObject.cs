using UnityEngine;

namespace MMO.UI
{
    /// <summary>
    /// View-layer helpers shared by the code-built screens.
    /// </summary>
    internal static class ViewObject
    {
        /// <summary>
        /// Destroys a view GameObject correctly on either side of the play-mode boundary:
        /// <see cref="Object.DestroyImmediate"/> outside play mode, where nothing would ever
        /// drain Unity's deferred destroy queue, and <see cref="Object.Destroy"/> during play,
        /// where tearing objects down mid-frame is not allowed.
        /// </summary>
        public static void Destroy(GameObject gameObject)
        {
            if (gameObject == null)
                return;

            if (Application.isPlaying)
                Object.Destroy(gameObject);
            else
                Object.DestroyImmediate(gameObject);
        }
    }
}
