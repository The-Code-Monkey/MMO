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

        /// <summary>Panel background shared by every screen.</summary>
        public static readonly Color PanelColor = new(0f, 0f, 0f, 0.45f);

        /// <summary>Bar trough behind the health and mana fills.</summary>
        public static readonly Color TroughColor = new(0f, 0f, 0f, 0.6f);

        /// <summary>Health fill colour.</summary>
        public static readonly Color HealthColor = new(0.75f, 0.15f, 0.15f, 0.95f);

        /// <summary>Mana fill colour.</summary>
        public static readonly Color ManaColor = new(0.2f, 0.4f, 0.9f, 0.95f);

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

            NetworkHud hud = root.AddComponent<NetworkHud>();
            hud.SetViews(
                CreatePlayerHud(root.transform),
                CreateTargetFrame(root.transform),
                inventoryGrid,
                CreateActionBar(root.transform),
                ChatFeed.Create(root.transform),
                inventoryWindow,
                equipmentWindow,
                equipmentPanel,
                mountCastBar);
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
        public static EventSystem EnsureEventSystem()
        {
            EventSystem existing = UnityEngine.Object.FindFirstObjectByType<EventSystem>();
            if (existing != null)
                return existing;

            GameObject eventSystemObject = new GameObject(EventSystemName);
            EventSystem eventSystem = eventSystemObject.AddComponent<EventSystem>();

            // This assembly references neither input backend by name, so the choice is made at
            // runtime: the Input System package's module when the project has it (it does), the
            // legacy module otherwise.
            Type moduleType = FindInputSystemModule() ?? typeof(StandaloneInputModule);
            eventSystemObject.AddComponent(moduleType);

            return eventSystem;
        }

        private static PlayerHudView CreatePlayerHud(Transform parent)
        {
            GameObject panel = CreateRect(
                parent, "PlayerHud", new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(12f, -12f), new Vector2(300f, 84f));
            panel.AddComponent<Image>().color = PanelColor;

            PlayerHudView hud = panel.AddComponent<PlayerHudView>();
            hud.SetBars(
                CreateVitalBar(panel.transform, "HealthBar", VitalKind.Health,
                    new Vector2(10f, -10f), HealthColor),
                CreateVitalBar(panel.transform, "ManaBar", VitalKind.Mana,
                    new Vector2(10f, -46f), ManaColor));

            return hud;
        }

        private static VitalBarView CreateVitalBar(
            Transform parent, string name, VitalKind kind, Vector2 position, Color fillColor)
        {
            GameObject barObject = CreateRect(
                parent, name, new Vector2(0f, 1f), new Vector2(0f, 1f),
                position, new Vector2(280f, 28f));
            barObject.AddComponent<Image>().color = TroughColor;

            GameObject fillObject = CreateStretch(barObject.transform, "Fill");
            Image fill = fillObject.AddComponent<Image>();
            fill.color = fillColor;
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;

            GameObject valueObject = CreateStretch(barObject.transform, "Value");
            Text value = valueObject.AddComponent<Text>();
            ApplyLabel(value, 13, TextAnchor.MiddleCenter);

            VitalBarView bar = barObject.AddComponent<VitalBarView>();
            bar.SetKind(kind);
            bar.SetVisuals(fill, value);
            return bar;
        }

        private static TargetFrameView CreateTargetFrame(Transform parent)
        {
            GameObject frame = CreateRect(
                parent, "TargetFrame", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -12f), new Vector2(260f, 64f));
            frame.AddComponent<Image>().color = PanelColor;

            GameObject nameRow = CreateRow(
                frame.transform, "Name", 1f, new Vector2(0.5f, 1f),
                new Vector2(0f, -4f), new Vector2(-20f, 26f));
            Text nameText = CreateLabel(nameRow.transform);
            ApplyLabel(nameText, 14, TextAnchor.MiddleCenter);

            GameObject fillRow = CreateRow(
                frame.transform, "HealthFill", 0f, new Vector2(0.5f, 0f),
                new Vector2(0f, 6f), new Vector2(-20f, 26f));
            Image healthFill = fillRow.GetComponent<Image>();
            healthFill.color = HealthColor;
            healthFill.type = Image.Type.Filled;
            healthFill.fillMethod = Image.FillMethod.Horizontal;

            GameObject valueRow = CreateRow(
                frame.transform, "HealthValue", 0f, new Vector2(0.5f, 0f),
                new Vector2(0f, 6f), new Vector2(-20f, 26f));
            Text healthText = CreateLabel(valueRow.transform);
            ApplyLabel(healthText, 13, TextAnchor.MiddleCenter);

            TargetFrameView view = frame.AddComponent<TargetFrameView>();
            view.SetVisuals(frame, nameText, healthFill, healthText);
            return view;
        }

        private static InventoryGridView CreateInventoryGrid(Transform parent, out CollapsiblePanelView window)
        {
            GameObject panel = CreateRect(
                parent, "InventoryWindow", new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-12f, IconRowHeight + 24f),
                new Vector2(
                    PlayerInventory.GridWidth * InventoryGridView.CellSize,
                    PlayerInventory.VisibleGridHeight * InventoryGridView.CellSize));
            panel.AddComponent<Image>().color = PanelColor;

            InventoryGridView grid = panel.AddComponent<InventoryGridView>();
            grid.SetContainer(CreateStretch(panel.transform, "Cells").GetComponent<RectTransform>());

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

            GameObject panel = CreateRect(
                parent, "EquipmentWindow", new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-12f - bagWidth - 12f, IconRowHeight + 24f),
                new Vector2(width, 240f));
            panel.AddComponent<Image>().color = PanelColor;

            GameObject titleObject = CreateRect(
                panel.transform, "Title", new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(10f, -6f), new Vector2(width - 20f, 24f));
            Text title = titleObject.AddComponent<Text>();
            ApplyLabel(title, 14, TextAnchor.MiddleLeft);
            title.text = "Equipment";

            GameObject rows = CreateStretch(panel.transform, "Rows");
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
            GameObject panel = CreateRect(
                parent, "MountCastBar", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(360f, 44f));
            panel.AddComponent<Image>().color = TroughColor;

            GameObject fillObject = CreateStretch(panel.transform, "Fill");
            Image fill = fillObject.AddComponent<Image>();
            fill.color = new Color(0.85f, 0.65f, 0.15f, 0.95f);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;

            GameObject labelObject = CreateStretch(panel.transform, "Label");
            Text label = labelObject.AddComponent<Text>();
            ApplyLabel(label, 16, TextAnchor.MiddleCenter);

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
            GameObject iconObject = CreateRect(
                parent, name, new Vector2(1f, 0f), new Vector2(1f, 0f),
                position, new Vector2(IconRowHeight, IconRowHeight));

            Image background = iconObject.AddComponent<Image>();
            background.color = new Color(0f, 0f, 0f, 0.65f);

            Button button = iconObject.AddComponent<Button>();
            button.targetGraphic = background;

            GameObject labelObject = CreateStretch(iconObject.transform, "Label");
            Text text = labelObject.AddComponent<Text>();
            ApplyLabel(text, 12, TextAnchor.MiddleCenter);
            text.text = label;

            return button;
        }

        private static ActionBarView CreateActionBar(Transform parent)
        {
            GameObject panel = CreateRect(
                parent, "ActionBar", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 16f),
                new Vector2(AbilityLoadout.MaxSlots * ActionBarView.SlotSize, ActionBarView.SlotSize));
            panel.AddComponent<Image>().color = PanelColor;

            ActionBarView bar = panel.AddComponent<ActionBarView>();
            bar.SetContainer(CreateStretch(panel.transform, "Slots").GetComponent<RectTransform>());
            return bar;
        }

        private static GameObject CreateRect(
            Transform parent,
            string name,
            Vector2 anchor,
            Vector2 pivot,
            Vector2 anchoredPosition,
            Vector2 sizeDelta)
        {
            var rectObject = new GameObject(name, typeof(RectTransform));
            RectTransform rect = (RectTransform)rectObject.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = sizeDelta;
            return rectObject;
        }

        private static GameObject CreateStretch(Transform parent, string name)
        {
            var rectObject = new GameObject(name, typeof(RectTransform));
            RectTransform rect = (RectTransform)rectObject.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return rectObject;
        }

        /// <summary>
        /// A horizontally stretched, vertically fixed row inset by half of its sizeDelta.x —
        /// the shape the target frame's name and health strips use. The background is present
        /// but clear, so callers that only need a layout anchor (the name) show nothing behind
        /// the text and callers that need a bar (the health fill) just assign a colour.
        /// </summary>
        private static GameObject CreateRow(
            Transform parent,
            string name,
            float anchorY,
            Vector2 pivot,
            Vector2 anchoredPosition,
            Vector2 sizeDelta)
        {
            var rowObject = new GameObject(name, typeof(RectTransform), typeof(Image));
            RectTransform rect = (RectTransform)rowObject.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0f, anchorY);
            rect.anchorMax = new Vector2(1f, anchorY);
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = sizeDelta;

            Image background = rowObject.GetComponent<Image>();
            background.color = Color.clear;
            background.raycastTarget = false;

            return rowObject;
        }

        /// <summary>
        /// A stretched text child of a row. A GameObject can carry only one Graphic, so a row
        /// that already holds its background Image cannot hold the label as well — the label
        /// needs a child of its own, which draws on top of that background.
        /// </summary>
        private static Text CreateLabel(Transform parent)
        {
            var labelObject = new GameObject("Label", typeof(RectTransform));
            RectTransform rect = (RectTransform)labelObject.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return labelObject.AddComponent<Text>();
        }

        private static void ApplyLabel(Text label, int fontSize, TextAnchor alignment)
        {
            if (label == null)
                return;

            label.font = UIFont.Default;
            label.fontSize = fontSize;
            label.alignment = alignment;
            label.color = Color.white;
            label.raycastTarget = false;
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 7;
            label.resizeTextMaxSize = fontSize;
            label.text = string.Empty;
        }

        private static Type FindInputSystemModule()
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.GetName().Name != "Unity.InputSystem")
                    continue;

                return assembly.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule");
            }

            return null;
        }
    }
}
