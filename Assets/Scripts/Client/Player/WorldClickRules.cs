namespace MMO.Client
{
    /// <summary>
    /// The one decision a world click makes before anything else: was that a click or a drag? A
    /// press that travelled further than the threshold is steering the camera and must not also
    /// activate whatever is under the pointer.
    /// </summary>
    public static class WorldClickRules
    {
        /// <summary>How far the pointer may travel, in pixels, and still count as a click.</summary>
        public const float DragThresholdPixels = 6f;

        /// <summary>True when a release with <paramref name="travelledPixels"/> of drag is a click.</summary>
        public static bool IsClick(float travelledPixels)
            => travelledPixels <= DragThresholdPixels;
    }
}
