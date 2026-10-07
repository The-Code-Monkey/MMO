using MMO.Persistence;
using UnityEngine;

namespace MMO.Net
{
    /// <summary>
    /// Hosts the process's <see cref="ZoneTransferTokenStore"/> and exposes it to the trigger that
    /// issues tokens and the gate that consumes them. Rides every
    /// <see cref="NetworkManagerFactory"/>-built NetworkManager, next to the join gate.
    ///
    /// <para>
    /// The signing secret is shared by every zone server in a deployment, exactly like
    /// <see cref="BackendSettings.ServerDeviceId"/> is: a token minted by Server A must verify on
    /// Server B, which is a different process. It defaults to the local Nakama server key so the
    /// shipped docker-compose works untouched; a real deployment must set its own secret on every
    /// instance.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("MMO/Net/Zone Transfer Token Service")]
    public sealed class ZoneTransferTokenService : MonoBehaviour
    {
        /// <summary>The most recently loaded service, for scene objects that cannot be wired directly.</summary>
        public static ZoneTransferTokenService Instance { get; private set; }

        [SerializeField] private string _secretKey = BackendSettings.DefaultServerKey;
        [SerializeField, Min(1f)] private float _tokenTtlSeconds = ZoneTransferTokenStore.DefaultTtlSeconds;

        /// <summary>The store the trigger issues through and the gate consumes through.</summary>
        public IZoneTransferTokenStore Store { get; private set; }

        /// <summary>The secret every zone server must share for cross-process validation.</summary>
        public string SecretKey => _secretKey;

        /// <summary>Lifetime given to tokens that do not carry their own expiry.</summary>
        public float TokenTtlSeconds => _tokenTtlSeconds;

        private void Awake()
        {
            Instance = this;

            if (Store == null)
            {
                string secret = string.IsNullOrEmpty(_secretKey) ? BackendSettings.DefaultServerKey : _secretKey;
                Store = new ZoneTransferTokenStore(secret, _tokenTtlSeconds);
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        /// <summary>Swaps in a store; production wires the default in <see cref="Awake"/>, tests inject a double.</summary>
        public void Configure(IZoneTransferTokenStore store) => Store = store;
    }
}
