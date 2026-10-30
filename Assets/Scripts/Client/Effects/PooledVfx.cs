using UnityEngine;

namespace MMO.Client
{
    /// <summary>
    /// One pooled burst. Counts down on its own and hands itself back to its pool instead of
    /// destroying the object, which is what keeps a fight free of allocation spikes.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PooledVfx : MonoBehaviour
    {
        private VfxPool _pool;
        private float _remaining;

        /// <summary>Wires the pool this instance returns to; called when the pool builds it.</summary>
        public void SetPool(VfxPool pool) => _pool = pool;

        /// <summary>Shows the instance for <paramref name="lifetime"/> seconds.</summary>
        public void Play(float lifetime) => _remaining = Mathf.Max(0.01f, lifetime);

        private void Update()
        {
            if (_remaining <= 0f)
                return;

            _remaining -= Time.deltaTime;
            if (_remaining <= 0f)
                _pool?.Return(gameObject);
        }

        private void OnDisable() => _remaining = 0f;
    }
}
