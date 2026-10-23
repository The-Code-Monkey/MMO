using MMO.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace MMO.UI
{
    /// <summary>
    /// The centre-screen cast bar for mounting and dismounting. It is visible only while
    /// <see cref="MountState.Mounting"/> or <see cref="MountState.Dismounting"/> is running and
    /// fills from the replicated <see cref="IMountSource.CastProgress"/>.
    ///
    /// Like every other HUD view it has no <c>Update</c>: it repaints when the mount source's
    /// change event fires, so the server's progress is what the player sees.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("MMO/UI/Mount Cast Bar")]
    public sealed class MountCastBarView : MonoBehaviour
    {
        private IMountSource _source;
        private Image _fill;
        private Text _label;

        /// <summary>The source currently bound, or null.</summary>
        public IMountSource Source => _source;

        /// <summary>Wires the fill and label the factory built; starts the bar hidden.</summary>
        public void SetVisuals(Image fill, Text label)
        {
            _fill = fill;
            _label = label;

            gameObject.SetActive(false);
        }

        /// <summary>Binds the local player's mount state; null hides the bar.</summary>
        public void Bind(IMountSource source)
        {
            if (_source != null)
                _source.MountChanged -= OnMountChanged;

            _source = source;

            if (_source != null)
                _source.MountChanged += OnMountChanged;

            Repaint();
        }

        /// <summary>Detaches from the source and hides the bar.</summary>
        public void Unbind()
        {
            if (_source != null)
                _source.MountChanged -= OnMountChanged;

            _source = null;
            gameObject.SetActive(false);
        }

        /// <summary>Shows, fills and labels the bar from the bound state, or hides it.</summary>
        public void Repaint()
        {
            bool casting = _source != null &&
                           (_source.State == MountState.Mounting || _source.State == MountState.Dismounting);

            gameObject.SetActive(casting);

            if (!casting)
                return;

            if (_fill != null)
                _fill.fillAmount = Mathf.Clamp01(_source.CastProgress);

            if (_label != null)
                _label.text = _source.State == MountState.Mounting ? "Mounting..." : "Dismounting...";
        }

        private void OnMountChanged() => Repaint();

        private void OnDestroy()
        {
            if (_source != null)
                _source.MountChanged -= OnMountChanged;
        }
    }
}
