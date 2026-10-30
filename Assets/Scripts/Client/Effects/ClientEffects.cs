using UnityEngine;

namespace MMO.Client
{
    /// <summary>
    /// The local presentation endpoint for an accepted cast: one pooled burst at the effect
    /// origin, skipped for allies when the ZvZ setting hides their spells.
    ///
    /// <para>
    /// Built by <see cref="LocalPlayerHud"/> on this client's player object, so a dedicated
    /// server never creates one and a remote player never builds a second. It is deliberately
    /// cosmetic — damage has already been decided on the server by the time it runs.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ClientEffects : MonoBehaviour
    {
        /// <summary>Seconds a placeholder burst stays visible.</summary>
        public const float DefaultLifetime = 0.4f;

        /// <summary>Expected simultaneous casts used to size the pool.</summary>
        public const int DefaultExpectedConcurrent = 32;

        /// <summary>The one endpoint built by this client, or null before the HUD starts.</summary>
        public static ClientEffects Instance { get; private set; }

        private VfxPool _pool;
        private SettingsModel _settings = SettingsModel.Defaults();

        /// <summary>The settings driving presentation; replaced by the settings panel later.</summary>
        public SettingsModel Settings => _settings;

        /// <summary>The pool this service drives, exposed for diagnostics and tests.</summary>
        public VfxPool Pool => _pool;

        /// <summary>
        /// Ensures this client has an effects endpoint parented to <paramref name="parent"/>.
        /// Idempotent: a second HUD build reuses the live instance.
        /// </summary>
        public static ClientEffects Ensure(Transform parent)
        {
            if (Instance != null)
                return Instance;

            var root = new GameObject("ClientEffects");
            if (parent != null)
                root.transform.SetParent(parent, false);

            return root.AddComponent<ClientEffects>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            _settings = new SettingsStore().Load();
            BuildPool();
        }

        /// <summary>Plays the placeholder burst for one accepted cast.</summary>
        /// <param name="abilityName">The resolved ability, kept for future per-skill effects.</param>
        /// <param name="position">World point the effect landed on.</param>
        /// <param name="isOwnCast">True when this client owns the caster.</param>
        public void Play(string abilityName, Vector3 position, bool isOwnCast)
        {
            if (!isOwnCast && !_settings.ShowAllySpellEffects)
                return;

            _pool?.Rent(position, DefaultLifetime);
        }

        /// <summary>Swaps the presentation settings, e.g. when the settings panel applies a change.</summary>
        public void ApplySettings(SettingsModel settings)
            => _settings = settings ?? SettingsModel.Defaults();

        private void BuildPool()
        {
            GameObject template = VfxTemplateFactory.Create();
            template.transform.SetParent(transform, false);
            template.SetActive(false);

            _pool = gameObject.AddComponent<VfxPool>();
            _pool.Configure(template, PoolBudget.Prewarm(DefaultExpectedConcurrent));
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }
    }
}
