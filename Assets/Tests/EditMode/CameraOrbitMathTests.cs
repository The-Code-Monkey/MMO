using MMO.Client;
using NUnit.Framework;
using UnityEngine;

namespace MMO.Tests
{
    /// <summary>The orbit's arithmetic: the neck pivot, the drag response and the eye's placement.</summary>
    public sealed class CameraOrbitMathTests
    {
        [Test]
        public void Pivot_SitsAtTheNeckAboveTheFeet()
        {
            Vector3 pivot = CameraOrbitMath.Pivot(Vector3.zero, 2f);

            Assert.AreEqual(0.6f, pivot.y, 0.0001f,
                "The root is the capsule's centre, so the neck is half a body above it.");
            Assert.AreEqual(CameraOrbitMath.NeckHeightAboveFeet,
                CameraOrbitMath.Pivot(Vector3.zero, 0f).y, 0.0001f);
        }

        [Test]
        public void NextPitch_Clamps()
        {
            Assert.AreEqual(CameraOrbitMath.MaxPitch,
                CameraOrbitMath.NextPitch(0f, -1000f), 0.0001f);
            Assert.AreEqual(CameraOrbitMath.MinPitch,
                CameraOrbitMath.NextPitch(0f, 1000f), 0.0001f);
        }

        [Test]
        public void NextPitch_PointerUpLooksUp()
        {
            float pitch = CameraOrbitMath.NextPitch(0f, 10f);

            Assert.Less(pitch, 0f, "Mouse-up must raise the view, which is a smaller downward angle.");
        }

        [Test]
        public void NextYaw_Wraps()
        {
            // 1000 px at 0.15 degrees per pixel is 150 degrees, not a fraction of a turn.
            Assert.AreEqual(150f, CameraOrbitMath.NextYaw(0f, 1000f), 0.0001f);
            Assert.AreEqual(210f, CameraOrbitMath.NextYaw(0f, -1000f), 0.0001f);

            // Across the seam in both directions.
            Assert.AreEqual(5f, CameraOrbitMath.NextYaw(350f, 100f), 0.0001f);
            Assert.AreEqual(350f, CameraOrbitMath.NextYaw(5f, -100f), 0.0001f);
        }

        [Test]
        public void EyePosition_SitsBehindThePivotAtLevel()
        {
            Vector3 eye = CameraOrbitMath.EyePosition(Vector3.zero, 0f, 0f, 6f);

            Assert.AreEqual(0f, eye.x, 0.0001f);
            Assert.AreEqual(0f, eye.y, 0.0001f);
            Assert.AreEqual(-6f, eye.z, 0.0001f);
        }

        [Test]
        public void EyePosition_RisesWhenTheCameraLooksDown()
        {
            Vector3 eye = CameraOrbitMath.EyePosition(Vector3.zero, 0f, 45f, 6f);

            Assert.Greater(eye.y, 0f);
            Assert.Less(eye.z, 0f);
        }
    }
}
