using System;
using MMO.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace MMO.UI
{
    /// <summary>
    /// The target frame: a name, a health bar, and a current/max readout for whatever the player
    /// has selected, bound to an <see cref="ITargetSource"/>.
    ///
    /// <para>
    /// Two things drive it, and neither is a frame timer. Selecting a target calls
    /// <see cref="Bind"/>, which repaints once against the target's current state; from then on
    /// the frame repaints only when the target's own <c>Changed</c> event fires — which, for the
    /// networked implementation, is FishNet's <c>SyncVar.OnChange</c> callback arriving from the
    /// server. An idle target therefore costs nothing to keep on screen.
    /// </para>
    ///
    /// <para>
    /// With no target bound the frame root (<see cref="Frame"/>) is deactivated, so an empty
    /// frame is not a permanent fixture on the screen — and re-binding repaints it back to life.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("MMO/UI/Target Frame View")]
    public class TargetFrameView : MonoBehaviour
    {
        private GameObject _frame;
        private Text _nameText;
        private Image _healthFill;
        private Text _healthText;
        private ITargetSource _target;

        /// <summary>The source currently bound, or null.</summary>
        public ITargetSource Target => _target;

        /// <summary>The GameObject hidden while no target is bound; usually this view's own root.</summary>
        public GameObject Frame => _frame;

        /// <summary>The title text, if any.</summary>
        public Text NameText => _nameText;

        /// <summary>The health fill image, if any.</summary>
        public Image HealthFill => _healthFill;

        /// <summary>The "current/max" text, if any.</summary>
        public Text HealthText => _healthText;

        /// <summary>Wires the visuals; public so a factory or a test can assemble the frame.</summary>
        public void SetVisuals(GameObject frame, Text nameText, Image healthFill, Text healthText)
        {
            _frame = frame;
            _nameText = nameText;
            _healthFill = healthFill;
            _healthText = healthText;
        }

        /// <summary>
        /// Shows <paramref name="target"/>, unsubscribing from any previously bound one and
        /// repainting once so the frame is correct the moment it appears. Binding null hides the
        /// frame and detaches it from the old target's events.
        /// </summary>
        public void Bind(ITargetSource target)
        {
            if (_target != null)
                _target.Changed -= OnTargetChanged;

            _target = target;

            if (_target != null)
                _target.Changed += OnTargetChanged;

            Repaint();
        }

        /// <summary>Hides the frame and stops listening to the bound target.</summary>
        public void Unbind() => Bind(null);

        /// <summary>
        /// Paints the bound target, or hides the frame when there is none. Public and virtual so
        /// a test can count repaints and prove they only come from <see cref="Bind"/> or the
        /// target's change event.
        /// </summary>
        public virtual void Repaint()
        {
            bool hasTarget = _target != null;

            if (_frame != null)
                _frame.SetActive(hasTarget);

            if (!hasTarget)
                return;

            if (_nameText != null)
                _nameText.text = _target.TargetName ?? string.Empty;

            int health = _target.Health;
            int max = _target.MaxHealth;

            if (_healthFill != null)
                _healthFill.fillAmount = VitalBarView.ComputeFillAmount(health, max);

            if (_healthText != null)
                _healthText.text = $"{health}/{max}";
        }

        private void OnTargetChanged() => Repaint();

        private void OnDestroy()
        {
            // Detach without repainting: the frame root may be the very GameObject going away,
            // and toggling it mid-destruction buys nothing — the subscription is what has to go.
            if (_target != null)
                _target.Changed -= OnTargetChanged;

            _target = null;
        }
    }
}
