using UnityEngine;

namespace MMO.Client
{
    /// <summary>
    /// Pure math for the orthographic camera's zoom and orbit. No Unity dependencies beyond
    /// <see cref="Mathf"/>, so it is fully testable in EditMode.
    ///
    /// The constants are the camera's tuning limits. They live here rather than on the scene's
    /// <c>CameraRig</c> because the numbers constrain the maths itself: a clamp below zero or a
    /// max below the min is a bug no amount of scene authoring can fix.
    /// </summary>
    public static class CameraZoomMath
    {
        /// <summary>Orthographic size a freshly created rig starts at.</summary>
        public const float DefaultOrthographicSize = 5f;

        /// <summary>Closest the camera may zoom in (smallest orthographic size).</summary>
        public const float MinZoomSize = 2f;

        /// <summary>Furthest the camera may zoom out (largest orthographic size).</summary>
        public const float MaxZoomSize = 15f;

        /// <summary>How much one full scroll-wheel notch moves the orthographic size.</summary>
        public const float ZoomSpeed = 1f;

        /// <summary>How far behind the pivot the camera sits, in world units.</summary>
        public const float DefaultFollowDistance = 6f;

        /// <summary>
        /// The angle above the horizon the camera looks down from. A constant rather than a user
        /// constant: the angle is baked into a fixed world-space offset rather than tracked.
        /// </summary>
        public const float DefaultFollowPitchDegrees = 70f;

        /// <summary>
        /// Applies scroll wheel delta to the current zoom size, clamping to min/max.
        /// </summary>
        public static float ApplyZoom(float currentSize, float scrollDelta)
        {
            if (Mathf.Approximately(scrollDelta, 0f))
                return currentSize;

            float newSize = currentSize - scrollDelta * ZoomSpeed;
            return Mathf.Clamp(newSize, MinZoomSize, MaxZoomSize);
        }

        /// <summary>
        /// Computes the follow offset for a given yaw angle (in degrees), using the default
        /// pitch and distance.
        /// </summary>
        public static Vector3 ComputeFollowOffset(float yawDegrees)
        {
            float pitchRad = DefaultFollowPitchDegrees * Mathf.Deg2Rad;
            float yawRad = yawDegrees * Mathf.Deg2Rad;
            float distance = DefaultFollowDistance;

            float cosPitch = Mathf.Cos(pitchRad);
            return new Vector3(
                distance * cosPitch * Mathf.Sin(yawRad),
                distance * Mathf.Sin(pitchRad),
                -distance * cosPitch * Mathf.Cos(yawRad));
        }
    }
}
