using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace MMO.Crafting
{
    public sealed class CraftingStation : NetworkBehaviour
    {
        /// <summary>
        /// How far in front of the station the player stands to use it.
        ///
        /// <para>
        /// It has to be inside <see cref="PlayerCrafting.MaxCraftDistance"/>, and with room to
        /// spare: the player and the station do not share a pivot height, so a stand-off equal to
        /// the reach measures further than the reach once the player is standing on it — the
        /// server refuses the open and no panel appears. Two metres leaves the player well inside
        /// the reach, and clear of the station's own body.
        /// </para>
        /// </summary>
        public const float StandOffDistance = 2f;

        private readonly SyncVar<int> _stationType = new();

        public CraftingStationType StationType => (CraftingStationType)_stationType.Value;

        /// <summary>
        /// Where the player stands to use this station: in front of it, inside the crafting reach.
        /// </summary>
        public Vector3 StandOffPosition => transform.position - transform.forward * StandOffDistance;

        [Server]
        public void Configure(CraftingStationType stationType) => _stationType.Value = (int)stationType;

        private void OnMouseDown() => RequestOpenFromLocalPlayer();

        public void RequestOpenFromLocalPlayer()
        {
            NetworkObject self = NetworkObject;
            if (self == null || self.NetworkManager == null)
                return;

            PlayerCrafting crafting = PlayerCrafting.FindLocal(self.NetworkManager);
            if (crafting == null)
                return;

            crafting.RequestOpenStation(this);
        }
    }
}
