using FishNet.Managing;
using MMO.Net;
using UnityEngine;

namespace MMO.Persistence
{
    /// <summary>
    /// Re-wires token authentication onto the join gate at runtime. The gate's
    /// <see cref="MMOConnectionGate.TokenValidator"/> is a plain runtime property — Unity doesn't
    /// serialize it, so a <em>baked scene's</em> gate would otherwise boot with open joins even when
    /// its serialized <see cref="MMOConnectionSettings.UseTokenAuth"/> says authenticated. This
    /// component rides every <see cref="Net.NetworkManagerFactory"/>-built NetworkManager object and
    /// reads that serialized flag back at load: when it is on and no validator is attached yet, it
    /// attaches the real <see cref="NakamaApiGateway"/>.
    ///
    /// <para>
    /// Attaching the concrete gateway here (rather than on the gate itself) keeps the flag the only
    /// thing a scene carries: bake the Main Menu (or any zone server) with use-token-auth on and the
    /// generic factory + this bootstrap produce a working, authenticated gate in any process that
    /// starts that scene.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("MMO/Persistence/Token Auth Bootstrap")]
    public sealed class TokenAuthBootstrap : MonoBehaviour
    {
        private void Start()
        {
            NetworkManager manager = GetComponentInParent<NetworkManager>();
            if (manager == null)
                return;

            Attach(manager.GetComponent<MMOConnectionGate>());
        }

        /// <summary>
        /// Wires the <paramref name="gate"/> up when its settings ask for token auth. Idempotent
        /// and never downgrades: a validator an owner attached explicitly — a test double, say — is
        /// left alone. Public so tests drive the wiring without waiting for a frame.
        /// </summary>
        public void Attach(MMOConnectionGate gate)
        {
            if (gate == null || !gate.Settings.UseTokenAuth || gate.TokenValidator != null)
                return;

            gate.TokenValidator = new NakamaApiGateway(new BackendSettings());
        }
    }
}