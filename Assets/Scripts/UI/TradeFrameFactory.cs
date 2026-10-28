using MMO.Inventory;
using UnityEngine;
using UnityEngine.UI;

namespace MMO.UI
{
    /// <summary>
    /// Builds the direct-trade window in code: two offer grids, the player's bag, a partner/state
    /// line, and Lock / Cancel. The returned view starts concealed and unbound.
    /// </summary>
    public static class TradeFrameFactory
    {
        public const string FrameName = "TradeFrame";
        public const float FrameWidth = 760f;
        public const float FrameHeight = 760f;

        private static readonly Color PanelColor = new Color(0f, 0f, 0f, 0.6f);
        private static readonly Color GridPanelColor = new Color(0f, 0f, 0f, 0.25f);

        public static TradeFrameView Create(Transform parent)
        {
            GameObject frame = CreateRect(
                parent, FrameName, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(FrameWidth, FrameHeight));
            frame.AddComponent<Image>().color = PanelColor;

            Text title = CreateLabel(frame.transform, "Title", 180f, 26f, new Vector2(16f, -8f), 15);
            title.text = "Trade";

            TradeFrameView view = frame.AddComponent<TradeFrameView>();

            Button close = CreateButton(
                frame.transform, "Close", 64f, 26f, new Vector2(-16f, -8f), "X");
            close.onClick.AddListener(() => view.Conceal());

            Text partner = CreateLabel(
                frame.transform, "Partner", 300f, 22f, new Vector2(16f, -36f), 13);

            InventoryGridView myGrid = CreateOfferGrid(
                frame.transform, "MyOffer", new Vector2(16f, -64f), "Your offer");
            InventoryGridView theirGrid = CreateOfferGrid(
                frame.transform, "TheirOffer", new Vector2(384f, -64f), "Their offer");

            GameObject bagPanel = CreateRect(
                frame.transform, "BagPanel", new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(16f, -400f),
                new Vector2(PlayerInventory.GridWidth * InventoryGridView.CellSize,
                    PlayerInventory.VisibleGridHeight * InventoryGridView.CellSize + 28f));
            bagPanel.AddComponent<Image>().color = GridPanelColor;

            Text bagTitle = CreateLabel(
                bagPanel.transform, "BagTitle", 240f, 22f, new Vector2(4f, -3f), 12);
            bagTitle.text = "Right-click to offer";

            InventoryGridView bagGrid = bagPanel.AddComponent<InventoryGridView>();
            bagGrid.SetContainer(CreateStretch(bagPanel.transform, "Cells").GetComponent<RectTransform>());
            RectTransform bagCells = bagGrid.CellContainer;
            bagCells.anchorMin = new Vector2(0f, 1f);
            bagCells.anchorMax = new Vector2(0f, 1f);
            bagCells.pivot = new Vector2(0f, 1f);
            bagCells.anchoredPosition = new Vector2(4f, -26f);

            Text state = CreateLabel(
                frame.transform, "State", 320f, 24f, new Vector2(370f, -640f), 13);

            Button lockButton = CreateButton(
                frame.transform, "Lock", 120f, 30f, new Vector2(-260f, -720f), "Lock");
            lockButton.onClick.AddListener(() => view.Lock());

            Button cancelButton = CreateButton(
                frame.transform, "Cancel", 120f, 30f, new Vector2(-130f, -720f), "Cancel");
            cancelButton.onClick.AddListener(() => view.CancelTrade());

            view.SetHosts(myGrid, theirGrid, bagGrid, partner, state);
            view.Conceal();
            return view;
        }

        private static InventoryGridView CreateOfferGrid(
            Transform parent, string name, Vector2 position, string caption)
        {
            GameObject panel = CreateRect(
                parent, name, new Vector2(0f, 1f), new Vector2(0f, 1f), position,
                new Vector2(PlayerInventory.GridWidth * InventoryGridView.CellSize,
                    PlayerInventory.VisibleGridHeight * InventoryGridView.CellSize + 28f));
            panel.AddComponent<Image>().color = GridPanelColor;

            Text title = CreateLabel(panel.transform, "Title", 240f, 22f, new Vector2(4f, -3f), 12);
            title.text = caption;

            InventoryGridView grid = panel.AddComponent<InventoryGridView>();
            grid.SetContainer(CreateStretch(panel.transform, "Cells").GetComponent<RectTransform>());

            RectTransform cells = grid.CellContainer;
            cells.anchorMin = new Vector2(0f, 1f);
            cells.anchorMax = new Vector2(0f, 1f);
            cells.pivot = new Vector2(0f, 1f);
            cells.anchoredPosition = new Vector2(4f, -26f);

            return grid;
        }

        private static GameObject CreateRect(
            Transform parent, string name, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            var rectObject = new GameObject(name, typeof(RectTransform));
            RectTransform rect = (RectTransform)rectObject.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
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

        private static Text CreateLabel(
            Transform parent, string name, float width, float height, Vector2 position, int size)
        {
            GameObject labelObject = CreateRect(
                parent, name, new Vector2(0f, 1f), new Vector2(0f, 1f), position,
                new Vector2(width, height));
            Text label = labelObject.AddComponent<Text>();
            label.font = UIFont.Default;
            label.fontSize = size;
            label.alignment = TextAnchor.MiddleLeft;
            label.color = Color.white;
            label.raycastTarget = false;
            label.text = string.Empty;
            return label;
        }

        private static Button CreateButton(
            Transform parent, string name, float width, float height, Vector2 position, string text)
        {
            GameObject buttonObject = CreateRect(
                parent, name, new Vector2(1f, 1f), new Vector2(1f, 1f), position,
                new Vector2(width, height));
            Image background = buttonObject.AddComponent<Image>();
            background.color = new Color(0.3f, 0.45f, 0.3f, 0.95f);

            Button button = buttonObject.AddComponent<Button>();
            button.targetGraphic = background;

            Text label = CreateStretch(buttonObject.transform, "Text").AddComponent<Text>();
            label.font = UIFont.Default;
            label.fontSize = 13;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.white;
            label.raycastTarget = false;
            label.text = text;

            return button;
        }
    }
}
