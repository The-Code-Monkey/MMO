using System;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using MMO.Content;
using MMO.Gameplay;
using MMO.Inventory;
using MMO.Progression;
using MMO.UI;
using UnityEngine;

namespace MMO.Crafting
{
    /// <summary>
    /// The player's crafting: replicated XP, the authoritative craft, and the server-side answer
    /// to "what does this station have for me".
    ///
    /// <para>
    /// As an <see cref="ICraftingSource"/> this component is also what the crafting panel binds
    /// to, and the data flow is deliberately one-way. A click on a station sends
    /// <see cref="RpcRequestOpen"/>; the server checks ownership and proximity, then answers that
    /// one client with a <c>[TargetRpc]</c> carrying the filtered <see cref="RecipeOffer"/>
    /// payload. The client never enumerates the recipe registry, and it never decides what is
    /// craftable — <see cref="TryCraft"/> re-runs <see cref="CraftingValidator"/> before anything
    /// moves, so a forged or stale offer name fails safely.
    /// </para>
    /// </summary>
    public sealed class PlayerCrafting : NetworkBehaviour, ICraftingSource
    {
        public const float MaxCraftDistance = 6f;

        private readonly SyncVar<int> _craftingXp = new();

        /// <summary>
        /// Whether a player standing at <paramref name="playerPosition"/> is close enough to a
        /// station at <paramref name="stationPosition"/> to use it.
        ///
        /// <para>
        /// One rule, shared: the server enforces it when it answers an open request and when it
        /// accepts a craft, and the client uses it to decide whether a station needs a walk before
        /// it can be opened. If the two ends measured reach differently, the client would ask for a
        /// station the server refuses — the walk would end and no panel would ever appear.
        /// </para>
        /// </summary>
        public static bool IsWithinReach(Vector3 playerPosition, Vector3 stationPosition)
            => Vector3.Distance(playerPosition, stationPosition) <= MaxCraftDistance;

        private PlayerInventory _inventory;
        private PlayerQuestLog _quests;
        private PlayerAchievements _achievements;
        private IReadOnlyList<RecipeOffer> _availableRecipes = Array.Empty<RecipeOffer>();

        public int CraftingXp => _craftingXp.Value;
        public int CraftingLevel => MMO.Crafting.CraftingXpTable.LevelForXp(_craftingXp.Value);

        /// <summary>The station the last payload came from, or null.</summary>
        public CraftingStation OpenedStation { get; private set; }

        /// <summary>The offers carried by the last payload; the panel's whole recipe list.</summary>
        public IReadOnlyList<RecipeOffer> AvailableRecipes => _availableRecipes;

        /// <summary>Raised when crafting XP changes, which can change what a station would offer.</summary>
        public event Action RecipesChanged;

        /// <summary>Raised client-side when the server answers an open request with a payload.</summary>
        public event Action<CraftingStation, IReadOnlyList<RecipeOffer>> StationOpened;

        private PlayerInventory Inventory =>
            _inventory != null ? _inventory : _inventory = GetComponent<PlayerInventory>();

        private PlayerQuestLog Quests =>
            _quests != null ? _quests : _quests = GetComponent<PlayerQuestLog>();

        public override void OnStartNetwork()
        {
            _craftingXp.OnChange += OnCraftingXpChanged;
        }

        public override void OnStopNetwork()
        {
            _craftingXp.OnChange -= OnCraftingXpChanged;
        }

        /// <summary>
        /// Asks the server to open <paramref name="station"/>; the panel populates when the
        /// server's payload arrives, not when this returns.
        /// </summary>
        public void RequestOpenStation(CraftingStation station)
        {
            if (!IsOwner || station == null)
                return;

            RpcRequestOpen(station.NetworkObject);
        }

        /// <summary>
        /// The server side of an open request: ownership was checked by the RPC, proximity is
        /// checked here, and a station that fails either check simply produces no payload.
        /// </summary>
        [ServerRpc]
        private void RpcRequestOpen(NetworkObject stationObject, NetworkConnection sender = null)
        {
            if (sender == null || !OwnerMatches(sender))
                return;

            TrySendOffers(sender, stationObject);
        }

        /// <summary>
        /// Builds and sends the offer payload for <paramref name="stationObject"/> to
        /// <paramref name="target"/>. Returns false (and sends nothing) when the station is gone
        /// or the player is too far away — the same reach <see cref="TryCraft"/> enforces, so a
        /// client cannot read a station it could not use.
        /// </summary>
        [Server]
        public bool TrySendOffers(NetworkConnection target, NetworkObject stationObject)
        {
            if (target == null ||
                stationObject == null ||
                !stationObject.TryGetComponent(out CraftingStation station))
                return false;

            if (!IsWithinReach(transform.position, station.transform.position))
                return false;

            Quests?.NotifyInteract(station.StationType.ToString());
            RpcOpenStation(target, stationObject, BuildOffers(station));
            return true;
        }

        [TargetRpc]
        private void RpcOpenStation(NetworkConnection target, NetworkObject stationObject, RecipeOffer[] offers)
        {
            if (!IsOwner || stationObject == null)
                return;

            CraftingStation station = stationObject.GetComponent<CraftingStation>();
            if (station == null)
                return;

            OpenedStation = station;
            _availableRecipes = offers ?? Array.Empty<RecipeOffer>();
            StationOpened?.Invoke(station, _availableRecipes);
        }

        [Server]
        private RecipeOffer[] BuildOffers(CraftingStation station)
        {
            IReadOnlyList<RecipeOffer> offers = CraftingOfferRules.Available(
                station.StationType, CraftingLevel, Inventory != null ? Inventory.Items : null);

            var payload = new RecipeOffer[offers.Count];
            for (int i = 0; i < offers.Count; i++)
                payload[i] = offers[i];

            return payload;
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

            // A successful craft changes what the bag can afford, so the panel is handed a fresh
            // payload instead of a stale one it would have to guess at.
            if (TryCraft(stationObject, recipeName))
                TrySendOffers(sender, stationObject);
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

            if (!IsWithinReach(transform.position, station.transform.position))
                return false;

            PlayerInventory inventory = Inventory;
            if (inventory == null ||
                !CraftingValidator.CanCraft(inventory.Items, recipe, station.StationType, CraftingLevel))
                return false;

            if (!inventory.TryCraft(recipe))
                return false;

            AwardCraftingXp(recipe.CraftingXp);
            (_achievements ??= GetComponent<PlayerAchievements>())?.Record(AchievementTrigger.Craft, 1);
            return true;
        }

        [Server]
        public void AwardCraftingXp(int amount)
        {
            _craftingXp.Value = Mathf.Max(0, _craftingXp.Value + Mathf.Max(0, amount));
        }

        public static PlayerCrafting FindLocal(FishNet.Managing.NetworkManager networkManager)
            => PlayerCombat.FindLocal(networkManager) is { } combat
                ? combat.GetComponent<PlayerCrafting>()
                : null;

        private void OnCraftingXpChanged(int previous, int current, bool asServer)
        {
            if (asServer)
                return;

            // A level change can only widen what a station offers, but the panel is looking at
            // the last payload; repainting at least keeps its level readout honest.
            RecipesChanged?.Invoke();
        }

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
