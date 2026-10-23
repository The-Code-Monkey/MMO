using System.Collections.Generic;
using MMO.Content;
using UnityEngine;
using UnityEngine.UI;

namespace MMO.UI
{
    /// <summary>
    /// The minimal dialogue reader: speaker, text and one button per server-sent choice. It does
    /// not know node ids or quest actions; a click only forwards the choice index.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("MMO/UI/Dialogue View")]
    public sealed class DialogueView : MonoBehaviour
    {
        public const string PanelName = "DialoguePanel";
        public const float PanelWidth = 520f;
        public const float PanelHeight = 280f;

        private static readonly Color PanelColor = new(0f, 0f, 0f, 0.82f);
        private static readonly Color ButtonColor = new(0.18f, 0.3f, 0.5f, 0.95f);

        private GameObject _panel;
        private Text _speaker;
        private Text _text;
        private RectTransform _choices;
        private PlayerDialogue _source;
        private readonly List<GameObject> _buttons = new();

        public PlayerDialogue Source => _source;
        public bool IsOpen => _panel != null && _panel.activeSelf;
        public IReadOnlyList<GameObject> Buttons => _buttons;

        /// <summary>Builds the concealed dialogue panel under <paramref name="parent"/>.</summary>
        public static DialogueView Create(Transform parent)
        {
            GameObject root = new GameObject(PanelName, typeof(RectTransform));
            RectTransform rootRect = (RectTransform)root.transform;
            rootRect.SetParent(parent, false);
            rootRect.anchorMin = new Vector2(0.5f, 0f);
            rootRect.anchorMax = new Vector2(0.5f, 0f);
            rootRect.pivot = new Vector2(0.5f, 0f);
            rootRect.anchoredPosition = new Vector2(0f, 24f);
            rootRect.sizeDelta = new Vector2(PanelWidth, PanelHeight);

            DialogueView view = root.AddComponent<DialogueView>();
            view._panel = root;
            root.AddComponent<Image>().color = PanelColor;

            view._speaker = CreateLabel(
                root.transform, "Speaker", new Vector2(16f, -10f),
                new Vector2(PanelWidth - 32f, 24f), 16, TextAnchor.MiddleLeft);
            view._speaker.fontStyle = FontStyle.Bold;

            view._text = CreateLabel(
                root.transform, "Text", new Vector2(16f, -40f),
                new Vector2(PanelWidth - 32f, 100f), 14, TextAnchor.UpperLeft);

            GameObject choicesObject = new GameObject(
                "Choices", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            RectTransform choices = (RectTransform)choicesObject.transform;
            choices.SetParent(root.transform, false);
            choices.anchorMin = new Vector2(0f, 0f);
            choices.anchorMax = new Vector2(1f, 0f);
            choices.pivot = new Vector2(0.5f, 0f);
            choices.anchoredPosition = new Vector2(0f, 12f);
            choices.sizeDelta = new Vector2(-32f, 0f);

            VerticalLayoutGroup layout = choicesObject.GetComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.LowerLeft;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            layout.spacing = 4f;

            ContentSizeFitter fitter = choicesObject.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            view._choices = choices;
            root.SetActive(false);
            return view;
        }

        /// <summary>Binds the view to a player's dialogue shell, or hides it when null.</summary>
        public void Bind(PlayerDialogue source)
        {
            Unbind();
            _source = source;

            if (_source == null)
            {
                Hide();
                return;
            }

            _source.NodeShown += Show;
            _source.Closed += Hide;

            if (_source.IsOpen)
                Show(_source.Current);
            else
                Hide();
        }

        public void Unbind()
        {
            if (_source == null)
                return;

            _source.NodeShown -= Show;
            _source.Closed -= Hide;
            _source = null;
        }

        public void Show(DialoguePayload payload)
        {
            if (_panel == null)
                return;

            _panel.SetActive(true);

            if (_speaker != null)
                _speaker.text = payload.Speaker;

            if (_text != null)
                _text.text = payload.Text;

            ClearButtons();

            string[] choices = payload.ChoiceTexts;
            if (choices == null)
                return;

            for (int i = 0; i < choices.Length; i++)
            {
                int index = i;
                Button button = CreateButton(choices[i]);
                button.onClick.AddListener(() => _source?.RequestChoice(index));
                _buttons.Add(button.gameObject);
            }
        }

        public void Hide()
        {
            ClearButtons();

            if (_panel != null)
                _panel.SetActive(false);
        }

        private void OnDestroy() => Unbind();

        private void ClearButtons()
        {
            for (int i = 0; i < _buttons.Count; i++)
                ViewObject.Destroy(_buttons[i]);

            _buttons.Clear();
        }

        private Button CreateButton(string label)
        {
            var buttonObject = new GameObject(
                "Choice", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));

            RectTransform rect = (RectTransform)buttonObject.transform;
            rect.SetParent(_choices, false);
            rect.sizeDelta = new Vector2(0f, 30f);

            LayoutElement layout = buttonObject.GetComponent<LayoutElement>();
            layout.minHeight = 30f;
            layout.preferredHeight = 30f;

            Image background = buttonObject.GetComponent<Image>();
            background.color = ButtonColor;

            Button button = buttonObject.GetComponent<Button>();
            button.targetGraphic = background;

            Text text = CreateStretch(buttonObject.transform, "Text").AddComponent<Text>();
            text.font = UIFont.Default;
            text.fontSize = 13;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.raycastTarget = false;
            text.text = label;

            return button;
        }

        private static Text CreateLabel(
            Transform parent, string name, Vector2 position, Vector2 size, int fontSize, TextAnchor alignment)
        {
            GameObject labelObject = new GameObject(name, typeof(RectTransform));
            RectTransform rect = (RectTransform)labelObject.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            Text label = labelObject.AddComponent<Text>();
            label.font = UIFont.Default;
            label.fontSize = fontSize;
            label.alignment = alignment;
            label.color = Color.white;
            label.raycastTarget = false;
            label.text = string.Empty;
            return label;
        }

        private static GameObject CreateStretch(Transform parent, string name)
        {
            var stretch = new GameObject(name, typeof(RectTransform));
            RectTransform rect = (RectTransform)stretch.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return stretch;
        }
    }
}
