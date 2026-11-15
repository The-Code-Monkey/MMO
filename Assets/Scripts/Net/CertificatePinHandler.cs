using System;
using UnityEngine.Networking;

namespace MMO.Net
{
    /// <summary>
    /// The <c>UnityWebRequest</c> hook that enforces a <see cref="PinSet"/>. Unity calls
    /// <see cref="ValidateCertificate"/> with the presented certificate's DER bytes; this handler
    /// extracts the SPKI hash and asks the pure <see cref="PinRules"/>, so the network layer owns
    /// no comparison logic and the decision is reproducible in an EditMode test.
    ///
    /// <para>
    /// Enforcement is deliberately conditional: a set that does not require pinning, or a loopback
    /// host, accepts the certificate unchanged. A loopback request would otherwise force every
    /// developer to mint a pinned self-signed certificate just to run the backend locally.
    /// A rejected handshake records the reason in <see cref="Rejection"/> and reports it through
    /// the optional callback, so the UI can explain a refusal without exposing pin material.
    /// </para>
    /// </summary>
    public sealed class CertificatePinHandler : CertificateHandler
    {
        private readonly PinSet _pinSet;
        private readonly PinTarget _target;
        private readonly string _host;
        private readonly Action<string> _onRejected;

        /// <summary>The SPKI hash presented by the last certificate, or empty when unreadable.</summary>
        public string PresentedSpkiSha256 { get; private set; } = string.Empty;

        /// <summary>The reason the last certificate was refused, or empty when it was accepted.</summary>
        public string Rejection { get; private set; } = string.Empty;

        /// <summary>True when this handler is actually checking pins for the configured host.</summary>
        public bool Enforced => _pinSet != null
            && PinRules.ShouldEnforce(_pinSet.RequirePinning, _host);

        public CertificatePinHandler(
            PinSet pinSet,
            PinTarget target,
            string host,
            Action<string> onRejected = null)
        {
            _pinSet = pinSet;
            _target = target;
            _host = host;
            _onRejected = onRejected;
        }

        public override bool ValidateCertificate(byte[] certificateData)
        {
            if (!Enforced)
                return true;

            if (!CertificatePinning.TrySpkiSha256(certificateData, out string presented))
                return Reject("The server certificate could not be read for pinning.");

            PresentedSpkiSha256 = presented;
            if (!PinRules.Verify(_pinSet, _target, presented))
                return Reject("The server certificate does not match any configured pin.");

            return true;
        }

        private bool Reject(string message)
        {
            Rejection = message;
            _onRejected?.Invoke(message);
            return false;
        }
    }
}
