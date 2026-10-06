using MMO.Gameplay;
using UnityEngine;

namespace MMO.UI
{
    /// <summary>
    /// The HUD proper: the health bar and the mana bar, as a pair, bound to one vitals source.
    ///
    /// <para>
    /// The component itself is only the pairing — <see cref="Bind"/> forwards the source to both
    /// bars, each of which subscribes to its own change event and repaints independently. A hit
    /// that spends no mana repaints the health bar and, because the source raises one vitals
    /// event, the mana bar too; nothing here inspects values to decide whether it "needs" to
    /// redraw, and nothing here asks Unity to run it every frame.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("MMO/UI/Player HUD View")]
    public class PlayerHudView : MonoBehaviour
    {
        private VitalBarView _healthBar;
        private VitalBarView _manaBar;
        private IVitalsSource _source;

        /// <summary>The health bar, or null when none was assigned.</summary>
        public VitalBarView HealthBar => _healthBar;

        /// <summary>The mana bar, or null when none was assigned.</summary>
        public VitalBarView ManaBar => _manaBar;

        /// <summary>The source the pair is bound to, or null.</summary>
        public IVitalsSource Source => _source;

        /// <summary>Wires the pair; public so a factory or a test can assemble the HUD.</summary>
        public void SetBars(VitalBarView healthBar, VitalBarView manaBar)
        {
            _healthBar = healthBar;
            _manaBar = manaBar;
        }

        /// <summary>Binds both bars to <paramref name="source"/>, or unbinds them when null.</summary>
        public void Bind(IVitalsSource source)
        {
            _source = source;

            if (_healthBar != null)
                _healthBar.Bind(source);

            if (_manaBar != null)
                _manaBar.Bind(source);
        }

        /// <summary>Unbinds both bars; the HUD stops repainting until the next <see cref="Bind"/>.</summary>
        public void Unbind()
        {
            _source = null;

            if (_healthBar != null)
                _healthBar.Unbind();

            if (_manaBar != null)
                _manaBar.Unbind();
        }

        private void OnDestroy() => Unbind();
    }
}
