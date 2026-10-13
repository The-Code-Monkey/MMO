using MMO.Inventory;
using MMO.Social;
using UnityEngine;
using UnityEngine.UI;

namespace MMO.UI
{
    /// <summary>
    /// Builds the guild bank window in code: a screen-space panel carrying the title, the tab
    /// strip, the bank grid, the player's bag grid and the permission editor, all wired to a
    /// <see cref="GuildBankFrameView"/>. Like the rest of the project's factories nothing here
    /// loads a prefab or a scene — the frame a test constructs and the frame a game shows are the
    /// same object.
    ///
    /// <para>
    /// The returned view starts bound to nothing and concealed; gameplay code calls
    /// <c>Reveal(PlayerGuild, PlayerInventory)</c> once the local player has spawned.
    /// </para>
    /// </summary>
    public static class GuildBankFrameFactory
    {
        public const string FrameName = "GuildBankFrame";
        public const float FrameWidth = 700f;
        public const float FrameHeight = 520f;

        private static readonly Color PanelColor = new Color(0f, 0f, 0f, 0.6f);

        public static GuildBankFrameView Create(Transform parent)
        {
            GameObject frame = CreateRect(
                parent, FrameName, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(FrameWidth, FrameHeight));
            frame.AddComponent<Image>().color = PanelColor;

            Text title = CreateLabel(frame.transform, "Title", 200f, 26f, new Vector2(16f, -8f), 15);
            title.text = "Guild Bank";

            GuildBankFrameView view = frame.AddComponent<GuildBankFrameView>();

            Button close = CreateButton(
                frame.transform, "Close", 64f, 26f, new Vector2(-16f, -8f), "X");
            close.onClick.AddListener(() => view.Conceal());

            GameObject stripObject = CreateRect(
                frame.transform, "TabStrip", new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(16f, -40f), new Vector2(650f, 30f));
            RectTransform strip = (RectTransform)stripObject.transform;

            GameObject bankPanel = CreateRect(
                frame.transform, "BankPanel", new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(16f, -78f),
                new Vector2(GuildBankRules.TabWidth * InventoryGridView.CellSize,
                    GuildBankRules.TabHeight * InventoryGridView.CellSize));
            bankPanel.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.25f);

            InventoryGridView bankGrid = bankPanel.AddComponent<InventoryGridView>();
            bankGrid.SetContainer(CreateStretch(bankPanel.transform, "Cells").GetComponent<RectTransform>());

            float bagWidth = PlayerInventory.GridWidth * InventoryGridView.CellSize;
            float bagHeight = PlayerInventory.GridHeight * InventoryGridView.CellSize;
            GameObject bagPanel = CreateRect(
                frame.transform, "BagPanel", new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(340f, -78f),
                new Vector2(bagWidth, bagHeight + 28f));
            bagPanel.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.25f);

            Text bagTitle = CreateLabel(bagPanel.transform, "BagTitle", 140f, 22f, new Vector2(4f, -3f), 12);
            bagTitle.text = "Right-click to deposit";

            InventoryGridView bagGrid = bagPanel.AddComponent<InventoryGridView>();
            bagGrid.SetContainer(CreateStretch(bagPanel.transform, "Cells").GetComponent<RectTransform>());
            RectTransform bagCells = bagGrid.CellContainer;
            bagCells.anchorMin = new Vector2(0f, 1f);
            bagCells.anchorMax = new Vector2(0f, 1f);
            bagCells.pivot = new Vector2(0f, 1f);
            bagCells.anchoredPosition = new Vector2(4f, -26f);
            bagCells.sizeDelta = new Vector2(bagWidth, bagHeight);

            GameObject editorObject = CreateRect(
                frame.transform, "PermissionEditor", new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(16f, 16f), new Vector2(668f, 150f));
            GuildRankPermissionEditor editor = editorObject.AddComponent<GuildRankPermissionEditor>();
            editor.SetContainer(CreateStretch(editorObject.transform, "Rows").GetComponent<RectTransform>());

            GuildBankFrameView hud = frame.AddComponent<GuildBankFrameView>();
            view.SetHosts(strip, bankGrid, bagGrid, editor);
            view.Conceal();
            return view;
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

        private static Text CreateLabel(Transform parent, string name, float width, float height, Vector2 position, int size)
        {
            GameObject labelObject = CreateRect(parent, name, new Vector2(0f, 1f), new Vector2(0f, 1f), position, new Vector2(width, height));
            Text label = labelObject.AddComponent<Text>();
            label.font = UIFont.Default;
            label.fontSize = size;
            label.alignment = TextAnchor.MiddleLeft;
            label.color = Color.white;
            label.raycastTarget = false;
            label.text = string.Empty;
            return label;
        }

        private static Button CreateButton(Transform parent, string name, float width, float height, Vector2 position, string text)
        {
            GameObject buttonObject = CreateRect(parent, name, new Vector2(1f, 1f), new Vector2(1f, 1f), position, new Vector2(width, height));
            Image background = buttonObject.AddComponent<Image>();
            background.color = new Color(0.5f, 0.2f, 0.2f, 0.95f);

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