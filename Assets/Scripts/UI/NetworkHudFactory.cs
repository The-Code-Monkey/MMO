using System;
using System.Reflection;
using MMO.Content;
using MMO.Crafting;
using MMO.Gameplay;
using MMO.Inventory;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MMO.UI
{
    /// <summary>
    /// Builds the whole client HUD in code: a screen-space canvas carrying the health/mana pair,
    /// the target frame, the collapsible bag and equipment windows, the action bar and the unified
    /// crafting panel, all wired to whatever sources the caller passes.
    ///
    /// <para>
    /// Nothing here loads a prefab or a scene — the project commits neither, and the existing
    /// factories (<see cref="MMO.Net.PlayerPrefabFactory"/> and friends) all build in code for the
    /// same reason: a scene, a fresh process and a test must produce identical objects.
    /// </para>
    ///
    /// <para>
    /// The returned <see cref="NetworkHud"/> is already bound, so a caller with a spawned player
    /// simply does <c>NetworkHudFactory.Create(stats, inventory, abilities)</c> and every bar,
    /// label and cell on screen is live from that moment on — repainting only when FishNet's
    /// change callbacks say the data changed.
    /// </para>
    /// </summary>
    public static class NetworkHudFactory
    {
        /// <summary>Name of the root canvas object.</summary>
        public const string HudName = "NetworkHud";

        /// <summary>Name of the input router the HUD needs to receive clicks.</summary>
        public const string EventSystemName = "EventSystem";

        /// <summary>
        /// Builds the HUD and binds it. Every source is optional: an argument left null binds
        /// nothing and its screen stays quiet until gameplay code calls
        /// <see cref="NetworkHud.BindPlayer"/> or <see cref="NetworkHud.BindTarget"/> later.
        /// </summary>
        /// <param name="vitals">The player's health/mana, e.g. a spawned <c>PlayerStats</c>.</param>
        /// <param name="inventory">The player's bag, e.g. a spawned <c>PlayerInventory</c>.</param>
        /// <param name="abilities">The player's action bar, e.g. a spawned <c>PlayerAbilities</c>.</param>
        /// <param name="target">Whatever the target frame should show; null starts it hidden.</param>
        /// <param name="createEventSystem">
        /// True to ensure a clickable event router exists (one is created only when the scene
        /// has none). Tests that never dispatch a pointer pass false.
        /// </param>
        /// <param name="createBankFrame">True to also build the generic bank window.</param>
        /// <param name="createTradeFrame">True to also build the direct-trade window.</param>
        /// <param name="crafting">
        /// The player's crafting, e.g. a spawned <c>PlayerCrafting</c>. The panel is built
        /// concealed regardless; it shows when the server sends that source an offer payload.
        /// </param>
        /// <param name="mount">
        /// The player's mount state, e.g. a spawned <c>PlayerMount</c>. The cast bar is built hidden
        /// and appears while a mount or dismount cast is running.
        /// </param>
        /// <param name="dialogue">
        /// The player's dialogue shell, e.g. a spawned <c>PlayerDialogue</c>. The panel is built
        /// concealed and appears when the server sends a node payload.
        /// </param>
        public static NetworkHud Create(
            IVitalsSource vitals = null,
            IInventorySource inventory = null,
            IAbilityBarSource abilities = null,
            ITargetSource target = null,
            bool createEventSystem = true,
            bool createBankFrame = false,
            ICraftingSource crafting = null,
            IMountSource mount = null,
            PlayerDialogue dialogue = null,
            bool createTradeFrame = false)
        {
            GameObject root = new GameObject(HudName, typeof(RectTransform));

            Canvas canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            CanvasScaler scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            root.AddComponent<GraphicRaycaster>();

            InventoryGridView inventoryGrid = InventoryWindowFactory.CreateInventoryGrid(root.transform, out CollapsiblePanelView inventoryWindow);
            EquipmentPanelView equipmentPanel = InventoryWindowFactory.CreateEquipmentPanel(root.transform, out CollapsiblePanelView equipmentWindow);
            MountCastBarView mountCastBar = InventoryWindowFactory.CreateMountCastBar(root.transform);
            ZoneTransferPromptView zoneTransferPrompt = ZoneTransferPromptView.Create(root.transform);

            NetworkHud hud = root.AddComponent<NetworkHud>();
            hud.SetViews(
                PlayerVitalsFactory.CreateHud(root.transform),
                TargetFrameFactory.Create(root.transform),
                inventoryGrid,
                ActionBarFactory.Create(root.transform),
                ChatFeed.Create(root.transform),
                inventoryWindow,
                equipmentWindow,
                equipmentPanel,
                mountCastBar,
                zoneTransferPrompt);
            hud.SetCraftingPanel(CraftingPanelFactory.Create(root.transform));
            hud.SetDialogueView(DialogueView.Create(root.transform));

            if (createBankFrame)
                hud.SetBankFrame(BankFrameFactory.Create(root.transform));

            if (createTradeFrame)
                hud.SetTradeFrame(TradeFrameFactory.Create(root.transform));

            hud.BindPlayer(vitals, inventory, abilities, crafting, mount);
            hud.BindDialogue(dialogue);
            hud.BindTarget(target);

            if (createEventSystem)
                EnsureEventSystem();

            return hud;
        }

        /// <summary>
        /// Returns the scene's <see cref="EventSystem"/>, creating one when there is none —
        /// without it uGUI dispatches no clicks, and every input path in this namespace goes
        /// dead. Reuses an existing router rather than fighting it for focus.
        /// </summary>
        public static EventSystem EnsureEventSystem() => HudEventSystem.Ensure();

    }
}
