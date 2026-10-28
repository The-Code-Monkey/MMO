using FishNet.Managing;
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

        private void OnEnable()
        {
            Instance = this;

            if (Store == null)
            {
                string secret = string.IsNullOrEmpty(_secretKey) ? BackendSettings.DefaultServerKey : _secretKey;
                Store = new ZoneTransferTokenStore(secret, _tokenTtlSeconds);
            }
        }

        private void Start()
        {
            // Runtime properties are not serialized, so a baked scene boots with a gate that has
            // no transfer-token store. Wire it here exactly as the factory does when it builds one.
            NetworkManager manager = GetComponentInParent<NetworkManager>();
            MMOConnectionGate gate = manager != null
                ? manager.GetComponentInChildren<MMOConnectionGate>(true)
                : null;

            if (gate != null && gate.TransferTokenStore == null)
                gate.TransferTokenStore = Store;
        }

        private void OnDestroy()
        {
            // ReferenceEquals, not ==: Unity's destroyed-object operator can read a dying instance
            // as unequal to itself, which would leave the static pointing at a destroyed object.
            if (ReferenceEquals(Instance, this))
                Instance = null;
        }

        /// <summary>Swaps in a store; production wires the default in <see cref="OnEnable"/>, tests inject a double.</summary>
        public void Configure(IZoneTransferTokenStore store) => Store = store;
    }
}
