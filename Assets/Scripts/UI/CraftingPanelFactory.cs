using UnityEngine;
using UnityEngine.UI;

namespace MMO.UI
{
    /// <summary>
    /// Builds the crafting window in code: a screen-space panel carrying the title, a close
    /// button and the scroll view of recipe rows, wired to a <see cref="CraftingPanelView"/>.
    /// Like the rest of the project's factories nothing here loads a prefab or a scene — the
    /// panel a test constructs and the panel a game shows are the same object.
    ///
    /// <para>
    /// The returned view starts concealed: it has no station and therefore no recipes until the
    /// server answers an open request, so showing an empty box would be a lie.
    /// </para>
    /// </summary>
    public static class CraftingPanelFactory
    {
        public const string PanelName = "CraftingPanel";
        public const float PanelWidth = 380f;
        public const float PanelHeight = 320f;

        private static readonly Color PanelColor = new(0f, 0f, 0f, 0.7f);

        public static CraftingPanelView Create(Transform parent)
        {
            GameObject panel = CreateRect(
                parent, PanelName, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(PanelWidth, PanelHeight));
            panel.AddComponent<Image>().color = PanelColor;

            CraftingPanelView view = panel.AddComponent<CraftingPanelView>();

            Text title = CreateLabel(panel.transform, "Title", new Vector2(16f, -8f), new Vector2(200f, 26f), 15);
            title.text = "Crafting";
            view.SetTitle(title);

            Button close = CreateButton(panel.transform, "Close", new Vector2(-16f, -8f), new Vector2(64f, 26f), "X");
            close.onClick.AddListener(() => view.Conceal());

            Text status = CreateLabel(
                panel.transform, "Status", new Vector2(16f, -32f), new Vector2(PanelWidth - 32f, 20f), 12);
            status.color = new Color(1f, 0.82f, 0.45f);
            view.SetStatus(status);

            view.SetContainer(CreateScrollView(panel.transform));
            view.Conceal();
            return view;
        }

        /// <summary>A vertical scroll view whose content stacks fixed-height rows from the top.</summary>
        private static RectTransform CreateScrollView(Transform parent)
        {
            GameObject scrollObject = CreateRect(
                parent, "ScrollView", new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(16f, -56f), new Vector2(PanelWidth - 32f, PanelHeight - 72f));

            GameObject viewportObject = CreateStretch(scrollObject.transform, "Viewport");
            RectTransform viewport = (RectTransform)viewportObject.transform;
            viewportObject.AddComponent<RectMask2D>();

            GameObject contentObject = new GameObject(
                "Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            RectTransform content = (RectTransform)contentObject.transform;
            content.SetParent(viewport, false);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = Vector2.zero;

            VerticalLayoutGroup layout = contentObject.GetComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            layout.spacing = 2f;
            layout.padding = new RectOffset(4, 4, 4, 4);

            ContentSizeFitter fitter = contentObject.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            ScrollRect scroll = scrollObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 20f;

            return content;
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
            Transform parent, string name, Vector2 position, Vector2 size, int fontSize)
        {
            GameObject labelObject = CreateRect(
                parent, name, new Vector2(0f, 1f), new Vector2(0f, 1f), position, size);
            Text label = labelObject.AddComponent<Text>();
            label.font = UIFont.Default;
            label.fontSize = fontSize;
            label.alignment = TextAnchor.MiddleLeft;
            label.color = Color.white;
            label.raycastTarget = false;
            label.text = string.Empty;
            return label;
        }

        private static Button CreateButton(
            Transform parent, string name, Vector2 position, Vector2 size, string text)
        {
            GameObject buttonObject = CreateRect(
                parent, name, new Vector2(1f, 1f), new Vector2(1f, 1f), position, size);
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
