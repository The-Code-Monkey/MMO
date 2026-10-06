using FishNet.Connection;
using FishNet.Object;
using UnityEngine;

namespace MMO.Client
{
    /// <summary>
    /// The script that gives a client something to look through. Sits on the player prefab and,
    /// when the object it is on belongs to this client, points
    /// <see cref="LocalCameraRigFactory.Ensure"/>'s rig at its own transform.
    ///
    /// Every client receives every player, so this component exists on other people's capsules too.
    /// The <c>if (!IsOwner) return;</c> in <see cref="TryClaimCamera"/> is the entire MMO logic of
    /// the feature: drop the check and the second player to spawn starts pushing the first player's
    /// view around, because both prefabs are reaching for the same single camera.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class LocalPlayerCamera : NetworkBehaviour
    {
        /// <summary>
        /// Claims the camera when the object starts and is ours. Runs on every client, for every
        /// player, which is exactly why the ownership check inside matters.
        /// </summary>
        public override void OnStartNetwork() => TryClaimCamera();

        /// <summary>
        /// Re-evaluates the claim when ownership moves. FishNet raises this on the client that gains
        /// ownership as well as on the ones that lose it, so one handler covers both directions:
        /// ownership arriving means the camera is ours to take, and ownership leaving means it is
        /// ours to hand back.
        /// </summary>
        public override void OnOwnershipClient(NetworkConnection prevOwner)
        {
            if (IsOwner)
                TryClaimCamera();
            else
                ReleaseCamera();
        }

        /// <summary>
        /// Hands the camera back on the way out. Without this a disconnect would leave the rig
        /// following a capsule that is being destroyed.
        /// </summary>
        public override void OnStopNetwork() => ReleaseCamera();

        /// <summary>
        /// Takes control of the camera for this player, if this player is ours to control.
        /// </summary>
        /// <returns>True when the rig was pointed at this object, false when it was left alone.</returns>
        public bool TryClaimCamera()
        {
            // THE guard. Every client spawns every player's prefab, so this component runs for
            // every player in the world. Only the object this client owns may move the camera;
            // without the check, joining as a second player hijacks the first player's view.
            if (!IsOwner)
                return false;

            // A dedicated server is a server, not a client: it owns nothing local and has no screen
            // to look through, so it must not build a camera rig for itself.
            if (!IsClientStarted)
                return false;

            LocalCameraRigFactory.Ensure().Claim(transform);
            return true;
        }

        /// <summary>
        /// Releases the camera, but only if this object is the one holding it. A player that never
        /// claimed the rig must not switch it off for the player that did.
        /// </summary>
        private void ReleaseCamera()
        {
            LocalCameraRig rig = LocalCameraRigFactory.Find();
            if (rig != null && rig.FollowTarget == transform)
                rig.Release();
        }
    }
}
