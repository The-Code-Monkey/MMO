using MMO.Moderation;
using UnityEngine;
using UnityEngine.UI;

namespace MMO.UI
{
    /// <summary>
    /// The small, dismissless banner that tells a player their account was warned, kicked or
    /// suspended. It shows only what a notice carries — the action, a plain-language rule
    /// explanation and an expiry — never server-side evidence.
    ///
    /// <para>
    /// The overlay belongs to the owning client's HUD and is built by
    /// <see cref="LocalPlayerHud"/>; it is deliberately not a sync source. A notice is pushed once
    /// (or replayed on reconnect) and lives on screen until the next one replaces it, so a missed
    /// frame cannot silently swallow a moderation outcome.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("MMO/UI/Moderation Notice Overlay")]
    public sealed class ModerationNoticeOverlay : MonoBehaviour
    {
        public const string GameObjectName = "ModerationNoticeOverlay";
        public const float PanelWidth = 560f;
        public const float PanelHeight = 112f;

        [SerializeField] private Image _background;
        [SerializeField] private Text _title;
        [SerializeField] private Text _body;

        /// <summary>Builds the banner under <paramref name="parent"/>, concealed until shown.</summary>
        public static ModerationNoticeOverlay Create(Transform parent)
        {
            var root = new GameObject(GameObjectName, typeof(RectTransform), typeof(Image));
            RectTransform rect = (RectTransform)root.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -24f);
            rect.sizeDelta = new Vector2(PanelWidth, PanelHeight);

            Image background = root.GetComponent<Image>();
            background.color = new Color(0.16f, 0.04f, 0.04f, 0.94f);
            background.raycastTarget = false;

            Text title = CreateLabel(root.transform, "Title", 20, 10f, 26f, TextAnchor.MiddleLeft);
            title.color = new Color(1f, 0.78f, 0.55f, 1f);

            Text body = CreateLabel(root.transform, "Body", 15, 40f, 62f, TextAnchor.UpperLeft);

            var overlay = root.AddComponent<ModerationNoticeOverlay>();
            overlay.SetContent(background, title, body);
            overlay.Hide();
            return overlay;
        }

        /// <summary>Injects the graphics a factory or test built elsewhere.</summary>
        public void SetContent(Image background, Text title, Text body)
        {
            _background = background;
            _title = title;
            _body = body;
        }

        /// <summary>Shows one notice, replacing any previous one.</summary>
        public void Show(ModerationNotice notice)
        {
            if (notice == null)
                return;

            if (_title != null)
                _title.text = "Moderation notice";

            if (_body != null)
                _body.text = ModerationRules.Describe(notice);

            gameObject.SetActive(true);
        }

        /// <summary>Shows a notice decoded from the transport's JSON payload.</summary>
        public void ShowJson(string json)
        {
            if (string.IsNullOrEmpty(json))
                return;

            ModerationNotice notice;
            try
            {
                notice = JsonUtility.FromJson<ModerationNotice>(json);
            }
            catch (System.ArgumentException)
            {
                return;
            }

            Show(notice);
        }

        /// <summary>Hides the banner without destroying the built objects.</summary>
        public void Hide() => gameObject.SetActive(false);

        private static Text CreateLabel(
            Transform parent, string name, int fontSize, float top, float height, TextAnchor alignment)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            RectTransform rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -top);
            rect.sizeDelta = new Vector2(-32f, height);

            Text label = go.GetComponent<Text>();
            label.font = UIFont.Default;
            label.fontSize = fontSize;
            label.alignment = alignment;
            label.color = Color.white;
            label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            return label;
        }
    }
}
