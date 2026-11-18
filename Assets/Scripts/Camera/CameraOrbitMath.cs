using UnityEngine;

namespace MMO.Client
{
    /// <summary>
    /// Pure arithmetic for the third-person orbit: where the pivot sits on the character, how a drag
    /// becomes yaw and pitch, and where the eye ends up. No scene, no input, no camera — so the feel
    /// is unit-testable.
    /// </summary>
    public static class CameraOrbitMath
    {
        /// <summary>The pivot's height above the character's feet, at the neck rather than the centre.</summary>
        public const float NeckHeightAboveFeet = 1.6f;

        /// <summary>How far the camera may look up from level, in degrees.</summary>
        public const float MinPitch = -35f;

        /// <summary>How far the camera may look down from level, in degrees.</summary>
        public const float MaxPitch = 70f;

        /// <summary>How far behind the pivot the eye sits, in metres.</summary>
        public const float DefaultDistance = 6f;

        /// <summary>Degrees of orbit per pixel of pointer travel.</summary>
        public const float Sensitivity = 0.15f;

        /// <summary>
        /// The pivot in world space. The player root is the centre of the capsule, so the neck is
        /// half a body above it, not a whole one: a literal +1.6 would put the eye above the head.
        /// </summary>
        public static Vector3 Pivot(Vector3 playerPosition, float capsuleHeight)
            => playerPosition + Vector3.up * (NeckHeightAboveFeet - Mathf.Max(0f, capsuleHeight) * 0.5f);

        /// <summary>Yaw after a horizontal drag, wrapped into one turn.</summary>
        public static float NextYaw(float yaw, float deltaX, float sensitivity = Sensitivity)
            => Wrap(yaw + deltaX * sensitivity);

        /// <summary>
        /// Pitch after a vertical drag. Pointer-up looks up, so the camera's downward angle drops.
        /// </summary>
        public static float NextPitch(
            float pitch,
            float deltaY,
            float sensitivity = Sensitivity,
            float min = MinPitch,
            float max = MaxPitch)
            => Mathf.Clamp(pitch - deltaY * sensitivity, Mathf.Min(min, max), Mathf.Max(min, max));

        /// <summary>The same angle expressed in 0..360 degrees.</summary>
        public static float Wrap(float yawDegrees)
        {
            float wrapped = yawDegrees % 360f;
            return wrapped < 0f ? wrapped + 360f : wrapped;
        }

        /// <summary>
        /// The rig's rotation: yaw around up, pitch as a downward tilt. A camera sitting at the
        /// rig's local -Z then looks back at the pivot from above.
        /// </summary>
        public static Quaternion Rotation(float yaw, float pitch)
            => Quaternion.Euler(pitch, yaw, 0f);

        /// <summary>World position of the eye for a pivot, orbit and arm length.</summary>
        public static Vector3 EyePosition(Vector3 pivot, float yaw, float pitch, float distance)
            => pivot + Rotation(yaw, pitch) * (Vector3.back * Mathf.Max(0f, distance));
    }
}
