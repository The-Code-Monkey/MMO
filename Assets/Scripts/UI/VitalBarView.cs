using System;
using MMO.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace MMO.UI
{
    /// <summary>
    /// One bar of the HUD — health or mana — bound to an <see cref="IVitalsSource"/>.
    ///
    /// <para>
    /// The binding is the point: <see cref="Bind"/> subscribes to the source's
    /// <c>VitalsChanged</c> event, which the networked implementation raises from FishNet's
    /// <c>SyncVar.OnChange</c> callbacks, and every repaint is a response to that event plus the
    /// one repaint <see cref="Bind"/> itself performs so already-replicated values paint
    /// immediately. There is deliberately no <c>Update</c> method anywhere in this class — a bar
    /// showing nothing but unchanged numbers costs nothing at all between changes.
    /// </para>
    ///
    /// <para>
    /// Visuals are optional: <see cref="SetVisuals"/> points the view at the <see cref="Image"/>
    /// whose <c>fillAmount</c> it drives and the <see cref="Text"/> it writes "current/max" into.
    /// A view whose visuals were never assigned still runs the whole bind/repaint path without
    /// throwing, which is what lets tests exercise the event wiring alone.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("MMO/UI/Vital Bar View")]
    public class VitalBarView : MonoBehaviour
    {
        [SerializeField] private VitalKind _kind = VitalKind.Health;

        private Image _fill;
        private Text _valueText;
        private IVitalsSource _source;

        /// <summary>Which vital this bar paints.</summary>
        public VitalKind Kind => _kind;

        /// <summary>The source currently bound, or null.</summary>
        public IVitalsSource Source => _source;

        /// <summary>The image whose <c>fillAmount</c> this bar drives, if any.</summary>
        public Image Fill => _fill;

        /// <summary>The text this bar writes "current/max" into, if any.</summary>
        public Text ValueText => _valueText;

        /// <summary>Declares which vital this bar shows. Repaints at once when a source is bound.</summary>
        public void SetKind(VitalKind kind)
        {
            _kind = kind;

            if (_source != null)
                Repaint();
        }

        /// <summary>Points the view at its visuals; public so a factory or a test can wire them.</summary>
        public void SetVisuals(Image fill, Text valueText)
        {
            _fill = fill;
            _valueText = valueText;
        }

        /// <summary>
        /// Binds the bar to a source: unsubscribes whatever was bound before, subscribes to
        /// <c>VitalsChanged</c>, and repaints once so current values show without waiting for the
        /// next change. Binding null is an unbind.
        /// </summary>
        public void Bind(IVitalsSource source)
        {
            if (_source != null)
                _source.VitalsChanged -= OnVitalsChanged;

            _source = source;

            if (_source != null)
                _source.VitalsChanged += OnVitalsChanged;

            Repaint();
        }

        /// <summary>Unsubscribes from the bound source. The bar keeps its last painted state.</summary>
        public void Unbind()
        {
            if (_source != null)
                _source.VitalsChanged -= OnVitalsChanged;

            _source = null;
        }

        /// <summary>
        /// Reads the bound source and paints it. Public and virtual so a test can count repaints
        /// and prove they only ever come from <see cref="Bind"/> or a change event.
        /// </summary>
        public virtual void Repaint()
        {
            if (_source == null)
                return;

            int current = _kind == VitalKind.Health ? _source.Health : _source.Mana;
            int max = _kind == VitalKind.Health ? _source.EffectiveMaxHealth : _source.EffectiveMaxMana;

            if (_fill != null)
                _fill.fillAmount = ComputeFillAmount(current, max);

            if (_valueText != null)
                _valueText.text = $"{current}/{max}";
        }

        /// <summary>
        /// The fraction a bar of <paramref name="current"/> out of <paramref name="max"/> should
        /// show. A cap of zero (an unspawned source, or a degenerate fixture) reads as empty
        /// rather than dividing by zero, and the result is always clamped into 0..1 so a source
        /// that somehow overshoots its own maximum cannot paint past the end of the bar.
        /// </summary>
        public static float ComputeFillAmount(int current, int max)
        {
            if (max <= 0)
                return 0f;

            return Mathf.Clamp01(current / (float)max);
        }

        private void OnVitalsChanged() => Repaint();

        private void OnDestroy() => Unbind();
    }
}
