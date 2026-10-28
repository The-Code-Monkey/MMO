using FishNet.Object;
using MMO.Banking;
using UnityEngine;

namespace MMO.Net
{
    /// <summary>
    /// A scene-authored "put a bank teller here" marker. The server materialises it through
    /// <see cref="WorldBootstrap"/>; clients carry the marker but never spawn from it. Beyond
    /// placement it carries the teller's <see cref="BankKind"/>, which the bootstrap applies with
    /// <see cref="BankTeller.Configure"/> once the object is spawned — so a personal and a guild
    /// teller are the same prefab wearing different configuration.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("MMO/Net/Bank Teller Marker")]
    public sealed class BankTellerMarker : MonoBehaviour
    {
        [Tooltip("Optional. Leave empty to use the shared BankTeller prefab the server has registered.")]
        [SerializeField]
        private NetworkObject _tellerPrefab;

        [SerializeField]
        private BankKind _kind = BankKind.Personal;

        public NetworkObject TellerPrefab => _tellerPrefab;

        public BankKind Kind => _kind;

        public void SetTellerPrefab(NetworkObject prefab) => _tellerPrefab = prefab;

        public void SetKind(BankKind kind) => _kind = kind;
    }
}
