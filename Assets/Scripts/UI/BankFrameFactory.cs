using MMO.Banking;
using MMO.Inventory;
using UnityEngine;
using UnityEngine.UI;

namespace MMO.UI
{
    /// <summary>
    /// Builds the generic bank window in code: title, close button, optional tab strip, the bank
    /// item grid with its currency strip, the player's backpack grid, and the optional guild rank
    /// permission editor. Like every factory here nothing loads a prefab or a scene.
    ///
    /// The returned view starts concealed and unbound. Gameplay code calls
    /// <c>Reveal(IBankSource, PlayerInventory, PlayerGuild)</c> when a teller accepts an open.
    /// </summary>
    public static class BankFrameFactory
    {
        public const string FrameName = "BankFrame";
        public const float FrameWidth = 780f;
        public const float FrameHeight = 600f;

        private static readonly Color PanelColor = new Color(0f, 0f, 0f, 0.6f);
        private static readonly Color GridPanelColor = new Color(0f, 0f, 0f, 0.25f);

        public static BankFrameView Create(Transform parent)
        {
            GameObject frame = CreateRect(
                parent, FrameName, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(FrameWidth, FrameHeight));
            frame.AddComponent<Image>().color = PanelColor;

            Text title = CreateLabel(frame.transform, "Title", 240f, 26f, new Vector2(16f, -8f), 15);
            title.text = "Bank";

            BankFrameView view = frame.AddComponent<BankFrameView>();

            Button close = CreateButton(frame.transform, "Close", 64f, 26f, new Vector2(-16f, -8f), "X");
            close.onClick.AddListener(() => view.Conceal());

            GameObject stripObject = CreateRect(
                frame.transform, "TabStrip", new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(16f, -40f), new Vector2(500f, 26f));
            RectTransform strip = (RectTransform)stripObject.transform;
            stripObject.SetActive(false);

            float cell = InventoryGridView.CellSize;
            float bankWidth = PersonalBankRules.BankWidth * cell;
            float bankHeight = PersonalBankRules.BankHeight * cell;

            GameObject bankPanel = CreateRect(
                frame.transform, "BankPanel", new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(16f, -76f), new Vector2(bankWidth, bankHeight));
            bankPanel.AddComponent<Image>().color = GridPanelColor;

            InventoryGridView bankGrid = bankPanel.AddComponent<InventoryGridView>();
            RectTransform bankCells = CreateStretch(bankPanel.transform, "Cells");
            bankGrid.SetContainer(bankCells);
            AnchorTopLeft(bankCells, Vector2.zero, new Vector2(bankWidth, bankHeight));

            Text currency = CreateLabel(
                frame.transform, "Currency", bankWidth, 22f,
                new Vector2(16f, -76f - bankHeight - 4f), 13);
            currency.text = "Empty";

            float bagWidth = PlayerInventory.GridWidth * cell;
            float bagHeight = PlayerInventory.VisibleGridHeight * cell;
            GameObject bagPanel = CreateRect(
                frame.transform, "BagPanel", new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(16f + bankWidth + 32f, -76f), new Vector2(bagWidth, bagHeight + 28f));
            bagPanel.AddComponent<Image>().color = GridPanelColor;

            Text bagTitle = CreateLabel(bagPanel.transform, "BagTitle", 240f, 22f, new Vector2(4f, -3f), 12);
            bagTitle.text = "Right-click to deposit";

            InventoryGridView bagGrid = bagPanel.AddComponent<InventoryGridView>();
            RectTransform bagCells = CreateStretch(bagPanel.transform, "Cells");
            bagGrid.SetContainer(bagCells);
            AnchorTopLeft(bagCells, new Vector2(4f, -26f), new Vector2(bagWidth, bagHeight));

            GameObject editorObject = CreateRect(
                frame.transform, "PermissionEditor", new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(16f, 16f), new Vector2(FrameWidth - 32f, 140f));
            GuildRankPermissionEditor editor = editorObject.AddComponent<GuildRankPermissionEditor>();
            editor.SetContainer(CreateStretch(editorObject.transform, "Rows"));
            editorObject.SetActive(false);

            view.SetHosts(title, bankGrid, bagGrid, currency, strip, editor);
            view.Conceal();
            return view;
        }

        private static void AnchorTopLeft(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
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

        private static RectTransform CreateStretch(Transform parent, string name)
        {
            var rectObject = new GameObject(name, typeof(RectTransform));
            RectTransform rect = (RectTransform)rectObject.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return rect;
        }

        private static Text CreateLabel(
            Transform parent, string name, float width, float height, Vector2 position, int size)
        {
            GameObject labelObject = CreateRect(
                parent, name, new Vector2(0f, 1f), new Vector2(0f, 1f), position, new Vector2(width, height));
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
                parent, name, new Vector2(1f, 1f), new Vector2(1f, 1f), position, new Vector2(width, height));
            Image background = buttonObject.AddComponent<Image>();
            background.color = new Color(0.5f, 0.2f, 0.2f, 0.95f);

            Button button = buttonObject.AddComponent<Button>();
            button.targetGraphic = background;

            Text label = CreateStretch(buttonObject.transform, "Text").gameObject.AddComponent<Text>();
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
