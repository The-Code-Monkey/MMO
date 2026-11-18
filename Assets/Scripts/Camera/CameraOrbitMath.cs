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

        /// <summary>Closest the arm may come to the pivot, in metres.</summary>
        public const float MinDistance = 0.3f;

        /// <summary>Furthest the arm may extend, in metres.</summary>
        public const float MaxDistance = 15f;

        /// <summary>Metres of arm added per scroll notch.</summary>
        public const float ZoomStep = 1.5f;

        /// <summary>SmoothDamp time for the arm, in seconds.</summary>
        public const float ZoomSmoothTime = 0.12f;

        /// <summary>Radius of the sphere swept back from the pivot against geometry.</summary>
        public const float ClipRadius = 0.25f;

        /// <summary>Closer than this, the local avatar is hidden rather than seen from inside.</summary>
        public const float AvatarHideDistance = 1.2f;

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

        /// <summary>Zoom target after a scroll, clamped to the legible range.</summary>
        public static float ApplyZoom(
            float currentTarget, float scrollNotches, float step = ZoomStep)
            => Mathf.Clamp(
                currentTarget + scrollNotches * step,
                MinDistance,
                MaxDistance);

        /// <summary>
        /// One damped step of the arm toward its target. Damping, not a snap: a wheel notch is a
        /// request, and the camera eases into it.
        /// </summary>
        public static float DampDistance(
            float current,
            float target,
            ref float velocity,
            float deltaSeconds,
            float smoothTime = ZoomSmoothTime)
        {
            if (deltaSeconds <= 0f)
                return current;

            return Mathf.SmoothDamp(
                current,
                Mathf.Clamp(target, MinDistance, MaxDistance),
                ref velocity,
                Mathf.Max(0.0001f, smoothTime),
                Mathf.Infinity,
                deltaSeconds);
        }

        /// <summary>
        /// The arm length that keeps the eye out of the wall the spherecast found: the hit minus the
        /// sphere's radius, never below the minimum.
        /// </summary>
        public static float ClipDistance(float desired, float hitDistance, float radius = ClipRadius)
        {
            float clear = hitDistance - Mathf.Max(0f, radius);
            return Mathf.Clamp(Mathf.Min(desired, clear), MinDistance, MaxDistance);
        }

        /// <summary>True when the eye is close enough to be inside the local avatar.</summary>
        public static bool HidesAvatar(float distance) => distance < AvatarHideDistance;

        /// <summary>World position of the eye for a pivot, orbit and arm length.</summary>
        public static Vector3 EyePosition(Vector3 pivot, float yaw, float pitch, float distance)
            => pivot + Rotation(yaw, pitch) * (Vector3.back * Mathf.Max(0f, distance));
    }
}
