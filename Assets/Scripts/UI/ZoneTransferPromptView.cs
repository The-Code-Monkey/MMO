using UnityEngine;
using UnityEngine.UI;

namespace MMO.UI
{
    /// <summary>
    /// The bottom-centre prompt that tells the local player which zone a nearby gate leads to and
    /// what key opens the transfer. Like every other HUD view it never polls: the local player's
    /// transfer component shows and hides it as the player moves into and out of a gate's range.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("MMO/UI/Zone Transfer Prompt")]
    public sealed class ZoneTransferPromptView : MonoBehaviour
    {
        public const string PanelName = "ZoneTransferPrompt";
        public const string Format = "To switch to zone {0}, press E";

        private static readonly Color PanelColor = new Color(0f, 0f, 0f, 0.82f);

        private Text _label;

        /// <summary>The label the prompt paints, or null before the factory wired it.</summary>
        public Text Label => _label;

        /// <summary>True while the prompt is on screen.</summary>
        public bool IsShown => gameObject.activeSelf;

        /// <summary>Builds the concealed prompt under <paramref name="parent"/>.</summary>
        public static ZoneTransferPromptView Create(Transform parent)
        {
            GameObject root = new GameObject(PanelName, typeof(RectTransform));
            RectTransform rootRect = (RectTransform)root.transform;
            rootRect.SetParent(parent, false);
            rootRect.anchorMin = new Vector2(0.5f, 0f);
            rootRect.anchorMax = new Vector2(0.5f, 0f);
            rootRect.pivot = new Vector2(0.5f, 0f);
            rootRect.anchoredPosition = new Vector2(0f, 120f);
            rootRect.sizeDelta = new Vector2(420f, 32f);

            ZoneTransferPromptView view = root.AddComponent<ZoneTransferPromptView>();

            Image background = root.AddComponent<Image>();
            background.color = PanelColor;
            background.raycastTarget = false;

            var labelObject = new GameObject("Label", typeof(RectTransform));
            RectTransform labelRect = (RectTransform)labelObject.transform;
            labelRect.SetParent(root.transform, false);
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(8f, 0f);
            labelRect.offsetMax = new Vector2(-8f, 0f);

            Text label = labelObject.AddComponent<Text>();
            label.font = UIFont.Default;
            label.fontSize = 14;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.white;
            label.raycastTarget = false;
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 8;
            label.resizeTextMaxSize = 14;
            label.text = string.Empty;

            view._label = label;
            root.SetActive(false);
            return view;
        }

        /// <summary>Shows the prompt for <paramref name="zoneName"/>.</summary>
        public void Show(string zoneName)
        {
            if (_label != null)
                _label.text = string.Format(Format, zoneName);

            gameObject.SetActive(true);
        }

        /// <summary>Hides the prompt.</summary>
        public void Hide()
        {
            if (gameObject.activeSelf)
                gameObject.SetActive(false);
        }
    }
}
