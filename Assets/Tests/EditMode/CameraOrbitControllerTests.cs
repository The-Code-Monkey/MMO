using MMO.Client;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MMO.Tests
{
    /// <summary>
    /// Tests for the orthographic camera's zoom and orbit math.
    ///
    /// The pure math in <see cref="CameraZoomMath"/> is tested here. The scene's
    /// <c>PersistentCameraController</c> reads <c>Input</c> directly in its <c>Update</c> loop and
    /// cannot be tested in EditMode, but the underlying calculations are fully covered.
    /// </summary>
    public sealed class CameraOrbitControllerTests
    {
        #region Zoom.

        [Test]
        public void ApplyZoom_ScrollUpZoomsIn()
        {
            float result = CameraZoomMath.ApplyZoom(5f, 1f);
            Assert.Less(result, 5f, "Scrolling up (positive delta) should zoom in (smaller orthographic size).");
        }

        [Test]
        public void ApplyZoom_ScrollDownZoomsOut()
        {
            float result = CameraZoomMath.ApplyZoom(5f, -1f);
            Assert.Greater(result, 5f, "Scrolling down (negative delta) should zoom out (larger orthographic size).");
        }

        [Test]
        public void ApplyZoom_NoScrollKeepsSize()
        {
            float result = CameraZoomMath.ApplyZoom(5f, 0f);
            Assert.AreEqual(5f, result, 0.001f);
        }

        [Test]
        public void ApplyZoom_ClampsToMinZoom()
        {
            float result = CameraZoomMath.ApplyZoom(2.5f, 10f);
            Assert.AreEqual(CameraZoomMath.MinZoomSize, result, 0.001f,
                "Zoom must not go below MinZoomSize.");
        }

        [Test]
        public void ApplyZoom_ClampsToMaxZoom()
        {
            float result = CameraZoomMath.ApplyZoom(14f, -10f);
            Assert.AreEqual(CameraZoomMath.MaxZoomSize, result, 0.001f,
                "Zoom must not exceed MaxZoomSize.");
        }

        [Test]
        public void ApplyZoom_RespectsZoomSpeed()
        {
            float result = CameraZoomMath.ApplyZoom(5f, 2f);
            Assert.AreEqual(5f - 2f * CameraZoomMath.ZoomSpeed, result, 0.001f);
        }

        #endregion

        #region Follow offset (orbit).

        [Test]
        public void ComputeFollowOffset_YawZero_PutsCameraBehindAndAbove()
        {
            Vector3 offset = CameraZoomMath.ComputeFollowOffset(0f);

            Assert.AreEqual(0f, offset.x, 0.001f, "At yaw 0 the camera should be directly behind (x=0).");
            Assert.Greater(offset.y, 0f, "The camera should be above the player.");
            Assert.Less(offset.z, 0f, "The camera should be behind the player (negative z).");
        }

        [Test]
        public void ComputeFollowOffset_MaintainsDefaultDistance()
        {
            Vector3 offset = CameraZoomMath.ComputeFollowOffset(0f);
            Assert.AreEqual(CameraZoomMath.DefaultFollowDistance, offset.magnitude, 0.01f,
                "Yaw rotation must not change the camera-to-player distance.");
        }

        [Test]
        public void ComputeFollowOffset_Yaw90_PutsCameraToTheSide()
        {
            Vector3 offset = CameraZoomMath.ComputeFollowOffset(90f);

            Assert.Greater(offset.x, 0f, "At yaw 90 the camera should be to the right of the player.");
            Assert.AreEqual(0f, offset.z, 0.001f, "At yaw 90 the camera should have no z offset.");
        }

        [Test]
        public void ComputeFollowOffset_Yaw180_PutsCameraInFront()
        {
            Vector3 offset = CameraZoomMath.ComputeFollowOffset(180f);

            Assert.AreEqual(0f, offset.x, 0.001f, "At yaw 180 the camera should be directly in front (x=0).");
            Assert.Greater(offset.y, 0f, "The camera should still be above the player.");
            Assert.Greater(offset.z, 0f, "At yaw 180 the camera should be in front (positive z).");
        }

        [Test]
        public void ComputeFollowOffset_YawMinus90_PutsCameraToTheLeft()
        {
            Vector3 offset = CameraZoomMath.ComputeFollowOffset(-90f);

            Assert.Less(offset.x, 0f, "At yaw -90 the camera should be to the left of the player.");
            Assert.AreEqual(0f, offset.z, 0.001f, "At yaw -90 the camera should have no z offset.");
        }

        [Test]
        public void ComputeFollowOffset_AllYawValuesMaintainDistance()
        {
            for (float yaw = -180f; yaw <= 180f; yaw += 15f)
            {
                Vector3 offset = CameraZoomMath.ComputeFollowOffset(yaw);
                Assert.AreEqual(CameraZoomMath.DefaultFollowDistance, offset.magnitude, 0.01f,
                    $"Distance must be maintained at yaw={yaw}");
            }
        }

        #endregion

        #region Camera tuning constants.

        [Test]
        public void MinZoomSize_IsLessThanDefault()
        {
            Assert.Less(CameraZoomMath.MinZoomSize, CameraZoomMath.DefaultOrthographicSize);
        }

        [Test]
        public void MaxZoomSize_IsGreaterThanDefault()
        {
            Assert.Greater(CameraZoomMath.MaxZoomSize, CameraZoomMath.DefaultOrthographicSize);
        }

        [Test]
        public void MinZoomSize_IsPositive()
        {
            Assert.Greater(CameraZoomMath.MinZoomSize, 0f,
                "A non-positive orthographic size would break rendering.");
        }

        #endregion
    }
}
