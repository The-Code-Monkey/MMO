using MMO.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace MMO.UI
{
    /// <summary>
    /// The HUD's PvP toggle: one button that asks the server to set the flag, and one label that
    /// says what the server currently holds. It never writes the flag itself and owns no timer —
    /// the countdown comes from the replicated value, so a refused request simply leaves the
    /// screen where it was.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("MMO/UI/PvP Flag")]
    public sealed class PvPFlagView : MonoBehaviour
    {
        public const string GameObjectName = "PvPFlag";

        private static readonly Color ButtonColor = new Color(0.14f, 0.14f, 0.17f, 0.95f);

        [SerializeField] private Button _button;
        [SerializeField] private Text _label;

        private IPvPSource _source;

        /// <summary>The button the player clicks, or null before the factory wired it.</summary>
        public Button Button => _button;

        /// <summary>The caption the view paints, or null before the factory wired it.</summary>
        public Text Label => _label;

        /// <summary>Builds the toggle under <paramref name="parent"/>, concealed until bound.</summary>
        public static PvPFlagView Create(Transform parent)
        {
            GameObject root = UIBuilder.Rect(
                parent, GameObjectName, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(12f, -104f), new Vector2(170f, 30f));

            Image background = root.AddComponent<Image>();
            background.color = ButtonColor;

            Button button = root.AddComponent<Button>();
            button.targetGraphic = background;

            Text label = UIBuilder.Label(root.transform);
            UIBuilder.ApplyLabel(label, 14, TextAnchor.MiddleCenter, resizeTextMinSize: 8);
            label.text = PvPFlagUiFormat.UnflaggedLabel;

            PvPFlagView view = root.AddComponent<PvPFlagView>();
            view.SetContent(button, label);
            button.onClick.AddListener(view.Toggle);
            root.SetActive(false);
            return view;
        }

        /// <summary>Injects the graphics a factory or test built elsewhere.</summary>
        public void SetContent(Button button, Text label)
        {
            _button = button;
            _label = label;
        }

        /// <summary>Binds to the local player's flag source and repaints from it.</summary>
        public void Bind(IPvPSource source)
        {
            if (_source != null)
                _source.FlagChanged -= Repaint;

            _source = source;

            if (_source == null)
            {
                gameObject.SetActive(false);
                return;
            }

            _source.FlagChanged += Repaint;
            gameObject.SetActive(true);
            Repaint();
        }

        /// <summary>Disconnects the source; the view is hidden until bound again.</summary>
        public void Unbind()
        {
            if (_source != null)
                _source.FlagChanged -= Repaint;

            _source = null;
            gameObject.SetActive(false);
        }

        /// <summary>How the request is sent: the opposite of what the server still holds.</summary>
        public void Toggle()
        {
            if (_source == null)
                return;

            _source.RequestFlag(!_source.IsFlagged);
        }

        /// <summary>Repaints from the bound source. Called on bind and on every replicated change.</summary>
        public void Repaint()
        {
            if (_source == null)
                return;

            if (_label != null)
            {
                _label.text = PvPFlagUiFormat.Describe(
                    _source.IsForced, _source.IsFlagged, _source.OptOutCooldown);
            }

            if (_button != null)
                _button.interactable = PvPFlagUiFormat.CanToggle(_source.IsForced);
        }

        private void OnDestroy()
        {
            if (_source != null)
                _source.FlagChanged -= Repaint;
        }
    }
}
