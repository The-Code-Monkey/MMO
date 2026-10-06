using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace MMO.Crafting
{
    public sealed class CraftingStation : NetworkBehaviour
    {
        private readonly SyncVar<int> _stationType = new();

        public CraftingStationType StationType => (CraftingStationType)_stationType.Value;

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
