using UnityEngine;
using UnityEngine.UI;

namespace MMO.UI
{
    /// <summary>
    /// Builds the mailbox window in code: title with the unread badge, a scrolling message list, a
    /// detail pane with claim actions, and a close button. Nothing here loads a prefab, so the frame
    /// a test builds is the frame a player sees.
    ///
    /// <para>
    /// The returned view starts concealed and unbound. <c>LocalPlayerHud</c> binds it to the
    /// player's <c>PlayerMail</c> shell; the server answers every read and claim with a fresh inbox.
    /// </para>
    /// </summary>
    public static class MailFrameFactory
    {
        public const string FrameName = MailFrameView.GameObjectName;
        public const float FrameWidth = 640f;
        public const float FrameHeight = 480f;

        private static readonly Color PanelColor = new Color(0f, 0f, 0f, 0.72f);
        private static readonly Color RowPanelColor = new Color(0f, 0f, 0f, 0.25f);

        public static MailFrameView Create(Transform parent)
        {
            GameObject frame = CreateRect(
                parent, FrameName, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(FrameWidth, FrameHeight));
            frame.AddComponent<Image>().color = PanelColor;

            Text title = CreateLabel(frame.transform, "Title", 300f, 26f, new Vector2(16f, -8f), 16);
            title.text = MailUiFormat.Title(0);

            Button close = CreateButton(
                frame.transform, "Close", 64f, 26f, new Vector2(FrameWidth - 16f - 64f, -8f), "X");

            // Message list: a masked, scrolling column of code-built rows.
            GameObject scrollObject = CreateRect(
                frame.transform, "List", new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(16f, -44f), new Vector2(260f, 380f));
            scrollObject.AddComponent<Image>().color = RowPanelColor;

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
            content.sizeDelta = new Vector2(0f, 0f);

            VerticalLayoutGroup layout = contentObject.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 2f;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;

            ContentSizeFitter fitter = contentObject.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            ScrollRect scroll = scrollObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            // Detail pane: subject, sender/date/state, body and the claim action.
            GameObject detail = CreateRect(
                frame.transform, "Detail", new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(288f, -44f), new Vector2(336f, 380f));
            detail.AddComponent<Image>().color = RowPanelColor;

            Text subject = CreateLabel(detail.transform, "Subject", 320f, 26f, new Vector2(8f, -6f), 16);
            Text meta = CreateLabel(detail.transform, "Meta", 320f, 54f, new Vector2(8f, -34f), 12);
            // Body: a masked, scrolling pane. A fixed Text truncated long messages with no way
            // to reach the end of them.
            GameObject bodyScrollObject = CreateRect(
                detail.transform, "BodyScroll", new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(8f, -94f), new Vector2(320f, 240f));

            // The scroll pane needs a raycast target of its own or a drag falls through to the
            // detail panel and never scrolls; a clear image is enough.
            bodyScrollObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0f);

            GameObject bodyViewportObject = CreateStretch(bodyScrollObject.transform, "Viewport");
            RectTransform bodyViewport = (RectTransform)bodyViewportObject.transform;
            bodyViewportObject.AddComponent<RectMask2D>();

            GameObject bodyContentObject = new GameObject(
                "Content", typeof(RectTransform), typeof(ContentSizeFitter));
            RectTransform bodyContent = (RectTransform)bodyContentObject.transform;
            bodyContent.SetParent(bodyViewport, false);
            bodyContent.anchorMin = new Vector2(0f, 1f);
            bodyContent.anchorMax = new Vector2(1f, 1f);
            bodyContent.pivot = new Vector2(0.5f, 1f);
            bodyContent.anchoredPosition = Vector2.zero;
            bodyContent.sizeDelta = Vector2.zero;

            ContentSizeFitter bodyFitter = bodyContentObject.GetComponent<ContentSizeFitter>();
            bodyFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            Text body = bodyContentObject.AddComponent<Text>();
            body.font = UIFont.Default;
            body.fontSize = 13;
            body.alignment = TextAnchor.UpperLeft;
            body.color = Color.white;
            body.raycastTarget = false;
            body.horizontalOverflow = HorizontalWrapMode.Wrap;
            body.verticalOverflow = VerticalWrapMode.Overflow;

            ScrollRect bodyScroll = bodyScrollObject.AddComponent<ScrollRect>();
            bodyScroll.viewport = bodyViewport;
            bodyScroll.content = bodyContent;
            bodyScroll.horizontal = false;
            bodyScroll.vertical = true;
            bodyScroll.movementType = ScrollRect.MovementType.Clamped;

            Button claim = CreateButton(
                detail.transform, "Claim", 140f, 30f, new Vector2(8f, -342f), "Claim");

            Button claimAll = CreateButton(
                frame.transform, "ClaimAll", 140f, 30f, new Vector2(16f, -432f), "Claim All");

            Text status = CreateLabel(frame.transform, "Status", 440f, 30f, new Vector2(170f, -432f), 12);
            status.color = new Color(1f, 0.65f, 0.6f);

            MailFrameView view = frame.AddComponent<MailFrameView>();
            view.SetHosts(title, content, subject, meta, body, status, claim, claimAll, close);
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

        private static Text CreateLabel(
            Transform parent, string name, float width, float height, Vector2 position, int size)
        {
            GameObject labelObject = CreateRect(
                parent, name, new Vector2(0f, 1f), new Vector2(0f, 1f), position, new Vector2(width, height));
            Text label = labelObject.AddComponent<Text>();
            label.font = UIFont.Default;
            label.fontSize = size;
            label.alignment = TextAnchor.UpperLeft;
            label.color = Color.white;
            label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            return label;
        }

        private static Button CreateButton(
            Transform parent, string name, float width, float height, Vector2 position, string text)
        {
            GameObject buttonObject = CreateRect(
                parent, name, new Vector2(0f, 1f), new Vector2(0f, 1f), position, new Vector2(width, height));
            Image background = buttonObject.AddComponent<Image>();
            background.color = new Color(0.3f, 0.3f, 0.3f, 0.95f);

            Button button = buttonObject.AddComponent<Button>();
            button.targetGraphic = background;

            GameObject textObject = CreateStretch(buttonObject.transform, "Text");
            Text label = textObject.AddComponent<Text>();
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
