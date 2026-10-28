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

            InventoryGridView inventoryGrid = CreateInventoryGrid(root.transform, out CollapsiblePanelView inventoryWindow);
            EquipmentPanelView equipmentPanel = CreateEquipmentPanel(root.transform, out CollapsiblePanelView equipmentWindow);
            MountCastBarView mountCastBar = CreateMountCastBar(root.transform);
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

        private static InventoryGridView CreateInventoryGrid(Transform parent, out CollapsiblePanelView window)
        {
            GameObject panel = UIBuilder.Rect(
                parent, "InventoryWindow", new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-12f, IconRowHeight + 24f),
                new Vector2(
                    PlayerInventory.GridWidth * InventoryGridView.CellSize,
                    PlayerInventory.VisibleGridHeight * InventoryGridView.CellSize));
            panel.AddComponent<Image>().color = HudTheme.PanelColor;

            InventoryGridView grid = panel.AddComponent<InventoryGridView>();
            grid.SetContainer(UIBuilder.Stretch(panel.transform, "Cells").GetComponent<RectTransform>());

            Button icon = CreateIconButton(parent, "InventoryIcon", "Bag", new Vector2(-12f, 12f));
            window = icon.gameObject.AddComponent<CollapsiblePanelView>();
            window.Configure(panel, icon);
            return grid;
        }

        /// <summary>
        /// The second window: one row per worn slot, opened by the equipment icon. It sits to the
        /// left of the bag window so both can be open at once without overlapping.
        /// </summary>
        private static EquipmentPanelView CreateEquipmentPanel(Transform parent, out CollapsiblePanelView window)
        {
            const float width = 260f;
            float bagWidth = PlayerInventory.GridWidth * InventoryGridView.CellSize;

            GameObject panel = UIBuilder.Rect(
                parent, "EquipmentWindow", new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-12f - bagWidth - 12f, IconRowHeight + 24f),
                new Vector2(width, 240f));
            panel.AddComponent<Image>().color = HudTheme.PanelColor;

            GameObject titleObject = UIBuilder.Rect(
                panel.transform, "Title", new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(10f, -6f), new Vector2(width - 20f, 24f));
            Text title = titleObject.AddComponent<Text>();
            UIBuilder.ApplyLabel(title, 14, TextAnchor.MiddleLeft);
            title.text = "Equipment";

            GameObject rows = UIBuilder.Stretch(panel.transform, "Rows");
            RectTransform rowsRect = (RectTransform)rows.transform;
            rowsRect.offsetMin = new Vector2(8f, 8f);
            rowsRect.offsetMax = new Vector2(-8f, -34f);

            EquipmentPanelView view = panel.AddComponent<EquipmentPanelView>();
            view.SetContainer(rowsRect);

            Button icon = CreateIconButton(parent, "EquipmentIcon", "Gear", new Vector2(-12f - 56f, 12f));
            window = icon.gameObject.AddComponent<CollapsiblePanelView>();
            window.Configure(panel, icon);
            return view;
        }

        /// <summary>The centre-screen bar that fills while the player mounts or dismounts.</summary>
        private static MountCastBarView CreateMountCastBar(Transform parent)
        {
            GameObject panel = UIBuilder.Rect(
                parent, "MountCastBar", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(360f, 44f));
            panel.AddComponent<Image>().color = HudTheme.TroughColor;

            GameObject fillObject = UIBuilder.Stretch(panel.transform, "Fill");
            Image fill = fillObject.AddComponent<Image>();
            fill.color = new Color(0.85f, 0.65f, 0.15f, 0.95f);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;

            GameObject labelObject = UIBuilder.Stretch(panel.transform, "Label");
            Text label = labelObject.AddComponent<Text>();
            UIBuilder.ApplyLabel(label, 16, TextAnchor.MiddleCenter);

            MountCastBarView view = panel.AddComponent<MountCastBarView>();
            view.SetVisuals(fill, label);
            return view;
        }

        /// <summary>Size of the collapsed bag/equipment buttons, in canvas units.</summary>
        private const float IconRowHeight = 44f;

        /// <summary>
        /// A small always-visible button that toggles a window. The project ships no icons, so the
        /// button is a labelled square; the label opts out of raycasts so the button's background
        /// receives the click.
        /// </summary>
        private static Button CreateIconButton(Transform parent, string name, string label, Vector2 position)
        {
            GameObject iconObject = UIBuilder.Rect(
                parent, name, new Vector2(1f, 0f), new Vector2(1f, 0f),
                position, new Vector2(IconRowHeight, IconRowHeight));

            Image background = iconObject.AddComponent<Image>();
            background.color = new Color(0f, 0f, 0f, 0.65f);

            Button button = iconObject.AddComponent<Button>();
            button.targetGraphic = background;

            GameObject labelObject = UIBuilder.Stretch(iconObject.transform, "Label");
            Text text = labelObject.AddComponent<Text>();
            UIBuilder.ApplyLabel(text, 12, TextAnchor.MiddleCenter);
            text.text = label;

            return button;
        }

    }
}
