using FishNet.Connection;
using FishNet.Object;
using UnityEngine;

namespace MMO.Client
{
    /// <summary>
    /// Binds the persistent camera to <em>this</em> client's player and nothing else. Every client
    /// spawns every player's prefab, so a camera that looks up "some object tagged Player" can
    /// latch onto a remote avatar — the local player then walks while the camera watches a
    /// standing stranger. Ownership, not a tag search, is the only correct answer for "which one
    /// do I control".
    ///
    /// <para>
    /// The component registers itself with <see cref="PersistentCameraController"/> when it becomes
    /// the local owner and clears that registration when ownership leaves or the object stops, so
    /// the camera follows the local player through zone loads and re-joins.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class LocalPlayerCameraTarget : NetworkBehaviour
    {
        public override void OnStartNetwork()
        {
            // FishNet forbids IsOwner inside OnStartNetwork (ownership is not resolved yet); the
            // recommended check for "does this client own it" is Owner.IsLocalClient.
            if (Owner != null && Owner.IsLocalClient)
                BindCamera();
        }

        public override void OnOwnershipClient(NetworkConnection prevOwner)
        {
            if (IsOwner)
                BindCamera();
            else
                UnbindCamera();
        }

        public override void OnStopNetwork() => UnbindCamera();

        private void BindCamera()
        {
            // A dedicated server is not a client: it owns no local player and has no screen, so it
            // must not hijack the camera rig a client might share the process with.
            if (IsServerStarted && !IsClientStarted)
                return;

            PersistentCameraController.Instance?.SetLocalTarget(transform);
        }

        private static void UnbindCamera()
        {
            PersistentCameraController.Instance?.ClearLocalTarget();
        }
    }
}
