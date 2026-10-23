using UnityEngine;
using UnityEngine.UI;

namespace MMO.UI
{
    /// <summary>
    /// Collapses a window down to the small button that opened it. The button stays on screen and
    /// toggles the panel, so the bag and the paper-doll each own an always-visible icon and the
    /// windows only take space while the player is looking at them.
    ///
    /// The view owns no data. It is wired once by <see cref="NetworkHudFactory"/> with the panel it
    /// controls and the button that controls it, and starts collapsed.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("MMO/UI/Collapsible Panel")]
    public sealed class CollapsiblePanelView : MonoBehaviour
    {
        private GameObject _panel;
        private Button _toggle;

        /// <summary>True while the controlled window is on screen.</summary>
        public bool IsExpanded { get; private set; }

        /// <summary>Declares what this button opens. Idempotent, so a rebuild can call it again.</summary>
        public void Configure(GameObject panel, Button toggle)
        {
            if (_toggle != null)
                _toggle.onClick.RemoveListener(Toggle);

            _panel = panel;
            _toggle = toggle;

            if (_toggle != null)
                _toggle.onClick.AddListener(Toggle);

            SetExpanded(false);
        }

        /// <summary>Flips the window between collapsed and expanded.</summary>
        public void Toggle() => SetExpanded(!IsExpanded);

        /// <summary>Opens or closes the window without touching its contents.</summary>
        public void SetExpanded(bool expanded)
        {
            IsExpanded = expanded;

            if (_panel != null)
                _panel.SetActive(expanded);
        }

        private void OnDestroy()
        {
            if (_toggle != null)
                _toggle.onClick.RemoveListener(Toggle);
        }
    }
}
