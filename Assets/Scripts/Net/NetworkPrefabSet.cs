using System.Collections.Generic;
using FishNet.Managing.Object;
using FishNet.Object;
using UnityEngine;

namespace MMO.Net
{
    /// <summary>Resolves (or builds) the canonical spawnable prefabs and registers their order.</summary>
    internal sealed class NetworkPrefabSet
    {
        private readonly List<NetworkObject> _built = new();

        public NetworkObject Player;
        public NetworkObject TargetDummy;
        public NetworkObject CraftingStation;
        public NetworkObject LootBag;
        public NetworkObject ResourceNode;
        public NetworkObject BankTeller;

        public static NetworkPrefabSet Resolve(
            NetworkObject playerPrefab,
            NetworkObject targetDummyPrefab,
            NetworkObject craftingStationPrefab,
            NetworkObject lootBagPrefab,
            NetworkObject resourceNodePrefab)
        {
            var set = new NetworkPrefabSet();
            set.Player = playerPrefab ?? set.Build(PlayerPrefabFactory.Create());
            set.TargetDummy = targetDummyPrefab ?? set.Build(TargetDummyPrefabFactory.Create());
            set.CraftingStation = craftingStationPrefab ?? set.Build(CraftingStationPrefabFactory.Create());
            set.LootBag = lootBagPrefab ?? set.Build(LootBagPrefabFactory.Create());
            set.ResourceNode = resourceNodePrefab ?? set.Build(ResourceNodePrefabFactory.Create());
            // The server spawns a teller per authored marker; only the id must be shared.
            set.BankTeller = set.Build(BankTellerPrefabFactory.Create());
            return set;
        }

        /// <summary>
        /// Registers before NetworkManager activation, so both ends derive the same prefab ids
        /// from the same list order.
        /// </summary>
        public void Register(PrefabObjects prefabs)
        {
            PlayerPrefabFactory.Register(prefabs, Player);
            TargetDummyPrefabFactory.Register(prefabs, TargetDummy);
            CraftingStationPrefabFactory.Register(prefabs, CraftingStation);
            LootBagPrefabFactory.Register(prefabs, LootBag);
            ResourceNodePrefabFactory.Register(prefabs, ResourceNode);
            BankTellerPrefabFactory.Register(prefabs, BankTeller);
        }

        public void DestroyBuilt()
        {
            for (int i = 0; i < _built.Count; i++)
            {
                if (_built[i] != null)
                    Object.DestroyImmediate(_built[i].gameObject);
            }

            _built.Clear();
        }

        private NetworkObject Build(NetworkObject prefab)
        {
            prefab.gameObject.SetActive(false);
            _built.Add(prefab);
            return prefab;
        }
    }
}
