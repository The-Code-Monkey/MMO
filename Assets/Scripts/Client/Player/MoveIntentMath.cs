using UnityEngine;

namespace MMO.Client
{
    /// <summary>
    /// Pure arithmetic for the walking intent: the keyboard's local axes become a camera-relative
    /// direction the server can integrate. Backward and strafe are deliberately slower than a
    /// forward walk, so a player cannot circle-strafe at full speed.
    ///
    /// <para>
    /// The output is a world-space direction on the X/Z plane, already scaled — the mover only has
    /// to normalise it and multiply by its own speed, which is what keeps the client unable to
    /// move faster than the server allows.
    /// </para>
    /// </summary>
    public static class MoveIntentMath
    {
        /// <summary>How much of forward speed a backward step keeps.</summary>
        public const float BackwardMultiplier = 0.55f;

        /// <summary>How much of forward speed a strafe keeps.</summary>
        public const float StrafeMultiplier = 0.8f;

        /// <summary>
        /// Scales local input (x = strafe, y = forward) and clamps it to one unit, so a diagonal is
        /// never faster than a straight line.
        /// </summary>
        public static Vector2 ApplyMultipliers(Vector2 input)
        {
            float strafe = Mathf.Clamp(input.x, -1f, 1f) * StrafeMultiplier;
            float forward = Mathf.Clamp(input.y, -1f, 1f);
            if (forward < 0f)
                forward *= BackwardMultiplier;

            var scaled = new Vector2(strafe, forward);
            return scaled.sqrMagnitude > 1f ? scaled.normalized : scaled;
        }

        /// <summary>
        /// Rotates local input into world X/Z for a camera yaw. Local +y is the camera's forward and
        /// local +x is its right, so the result follows wherever the player is looking.
        /// </summary>
        public static Vector2 ToWorld(Vector2 local, float yawDegrees)
        {
            float yaw = yawDegrees * Mathf.Deg2Rad;
            float sin = Mathf.Sin(yaw);
            float cos = Mathf.Cos(yaw);

            return new Vector2(
                local.x * cos + local.y * sin,
                -local.x * sin + local.y * cos);
        }
    }
}
