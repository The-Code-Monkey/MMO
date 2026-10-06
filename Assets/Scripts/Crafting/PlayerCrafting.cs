using System;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using MMO.Gameplay;
using MMO.Inventory;
using UnityEngine;

namespace MMO.Crafting
{
    public sealed class PlayerCrafting : NetworkBehaviour
    {
        public const float MaxCraftDistance = 3f;

        private readonly SyncVar<int> _craftingXp = new();

        private PlayerInventory _inventory;

        public int CraftingXp => _craftingXp.Value;
        public int CraftingLevel => MMO.Crafting.CraftingXpTable.LevelForXp(_craftingXp.Value);

        public event Action<CraftingStation> StationOpened;

        private PlayerInventory Inventory =>
            _inventory != null ? _inventory : _inventory = GetComponent<PlayerInventory>();

        public override void OnStartNetwork()
        {
            _craftingXp.OnChange += OnCraftingXpChanged;
        }

        public override void OnStopNetwork()
        {
            _craftingXp.OnChange -= OnCraftingXpChanged;
        }

        public void RequestOpenStation(CraftingStation station)
        {
            if (!IsOwner || station == null)
                return;

            StationOpened?.Invoke(station);
        }

        public void RequestCraft(CraftingStation station, string recipeName)
        {
            if (!IsOwner || station == null || string.IsNullOrEmpty(recipeName))
                return;

            RpcRequestCraft(station.NetworkObject, recipeName);
        }

        [ServerRpc]
        private void RpcRequestCraft(NetworkObject stationObject, string recipeName,
            NetworkConnection sender = null)
        {
            if (sender == null || !OwnerMatches(sender))
                return;

            TryCraft(stationObject, recipeName);
        }

        [Server]
        public bool TryCraft(NetworkObject stationObject, string recipeName)
        {
            if (stationObject == null ||
                !stationObject.TryGetComponent(out CraftingStation station))
                return false;

            RecipeData recipe = RecipeRegistry.Resolve(recipeName);
            if (recipe == null)
            {
                Debug.LogError($"{nameof(PlayerCrafting)}: unknown recipe '{recipeName}'.");
                return false;
            }

            if (Vector3.Distance(transform.position, station.transform.position) > MaxCraftDistance)
                return false;

            PlayerInventory inventory = Inventory;
            if (inventory == null ||
                !CraftingValidator.CanCraft(inventory.Items, recipe, station.StationType, CraftingLevel))
                return false;

            if (!inventory.TryCraft(recipe))
                return false;

            AwardCraftingXp(recipe.CraftingXp);
            return true;
        }

        [Server]
        [Server]
        public void AwardCraftingXp(int amount)
        {
            _craftingXp.Value = Mathf.Max(0, _craftingXp.Value + Mathf.Max(0, amount));
        }

        public static PlayerCrafting FindLocal(FishNet.Managing.NetworkManager networkManager)
            => PlayerCombat.FindLocal(networkManager) is { } combat
                ? combat.GetComponent<PlayerCrafting>()
                : null;

        private void OnCraftingXpChanged(int previous, int current, bool asServer) { }

        public static class CraftingXpTableImpl
        {
            public static int XpForLevel(int level)
            {
                int l = Mathf.Max(1, level);
                return 100 * (l - 1) * (l - 1);
            }

            public static int LevelForXp(int xp)
            {
                int x = Mathf.Max(0, xp);
                return Mathf.FloorToInt(Mathf.Sqrt(x / 100f)) + 1;
            }
        }
    }
}
