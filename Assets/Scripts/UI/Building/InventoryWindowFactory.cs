using MMO.Inventory;
using UnityEngine;
using UnityEngine.UI;

namespace MMO.UI
{
    /// <summary>Builds the bag/equipment windows, their icons and the mount cast bar.</summary>
    internal static class InventoryWindowFactory
    {
        public static InventoryGridView CreateInventoryGrid(Transform parent, out CollapsiblePanelView window)
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
        public static EquipmentPanelView CreateEquipmentPanel(Transform parent, out CollapsiblePanelView window)
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
        public static MountCastBarView CreateMountCastBar(Transform parent)
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
