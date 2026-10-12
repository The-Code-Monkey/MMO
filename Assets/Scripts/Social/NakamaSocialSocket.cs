using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using FishNet.Managing;
using UnityEngine;

namespace MMO.Social
{
    /// <summary>
    /// One zone server's game socket to Nakama's realtime endpoint. It joins
    /// <see cref="SocialSocketProtocol.SocialRoom"/> and pumps inbound envelopes to
    /// <see cref="EnvelopeReceived"/> on the main thread; outbound sends are best-effort.
    ///
    /// <para>
    /// Everything it delivers is a hint, never authority — the authoritative state lives in versioned
    /// storage and the 2 s reconcile pass in <see cref="SocialHubService"/> repairs anything a lost
    /// notify missed. That is why a dropped connection degrades chat reach without ever risking
    /// state: reconnect re-joins the room with a refreshed token and backs off 1 → 30 s.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("MMO/Social/Nakama Social Socket")]
    public sealed class NakamaSocialSocket : MonoBehaviour, ISocialBus
    {
        public static NakamaSocialSocket Instance { get; private set; }

        /// <summary>Raised on the main thread with every parsed bus envelope.</summary>
        public event Action<SocialBusEnvelope> EnvelopeReceived;

        /// <summary>True only while the socket is open and the room join has been acknowledged.</summary>
        public bool IsConnected => _transport != null && _transport.IsOpen && _joined;

        [SerializeField, Min(0f)] private float _maxBackoff = 30f;

        private ISocketTransport _transport;
        private INakamaSession _session;
        private NetworkManager _manager;
        private readonly ConcurrentQueue<string> _inbound = new ConcurrentQueue<string>();
        private CancellationTokenSource _cts;
        private string _channelId;
        private bool _joined;
        private bool _connecting;
        private int _cid;
        private float _backoff = 1f;
        private float _nextAttempt;

        private void Awake()
        {
            Instance = this;
            _cts = new CancellationTokenSource();
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;

            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }

        /// <summary>
        /// Injects the transport and the session. Production passes a <see cref="WebSocketTransport"/>
        /// and the shared <c>NakamaRestClient</c>; tests pass fakes.
        /// </summary>
        public void Configure(ISocketTransport transport, INakamaSession session)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _session = session ?? throw new ArgumentNullException(nameof(session));
        }

        private void Update()
        {
            // Unity API is main-thread-only: everything the receive loop saw is drained here.
            while (_inbound.TryDequeue(out string frame))
                HandleFrame(frame);

            if (IsConnected)
            {
                _backoff = 1f;
                return;
            }

            if (_session == null || _transport == null || _connecting)
                return;

            // Open but not yet joined (the join ack has not arrived): wait for it rather than
            // racing a second socket open on the same transport.
            if (_transport.IsOpen)
                return;

            // Only a started zone server opens the backend socket. Ordinary clients share this
            // NetworkManager object but must never talk to Nakama, so they stay offline.
            if (_manager == null)
                _manager = GetComponentInParent<NetworkManager>();

            if (_manager == null || !_manager.IsServerStarted)
                return;

            if (Time.unscaledTime < _nextAttempt)
                return;

            _nextAttempt = Time.unscaledTime + _backoff;   // 1, 2, 4, 8, 16, capped at 30 s
            _backoff = Mathf.Min(_backoff * 2f, _maxBackoff);
            _ = ConnectAsync(_cts != null ? _cts.Token : CancellationToken.None);
        }

        private async Task ConnectAsync(CancellationToken ct)
        {
            _connecting = true;
            _joined = false;
            _channelId = null;

            try
            {
                // Fresh token first — the cached one may be older than the session expiry.
                string token = await _session.GetFreshTokenAsync(ct);
                string url = BuildSocketUrl(_session.HttpBaseUrl, token);

                await _transport.OpenAsync(url, ct);
                await _transport.SendAsync(SocialSocketProtocol.BuildJoin(NextCid()), ct);
                _ = ReceiveLoopAsync(ct);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Social] socket connect failed: {e.Message}");
            }
            finally
            {
                _connecting = false;
            }
        }

        private async Task ReceiveLoopAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    string frame = await _transport.ReceiveAsync(ct);   // throws on disconnect
                    if (!string.IsNullOrEmpty(frame))
                        _inbound.Enqueue(frame);
                }
            }
            catch (Exception e)
            {
                if (!ct.IsCancellationRequested)
                    Debug.LogWarning($"[Social] socket dropped: {e.Message}");

                _joined = false;
            }
        }

        private void HandleFrame(string frame)
        {
            if (!SocialSocketProtocol.TryParse(frame, out SocketMessage message))
                return;

            switch (message.Kind)
            {
                case SocketKind.JoinAck:
                    _channelId = message.ChannelId;   // never construct this id ourselves
                    _joined = true;
                    break;

                case SocketKind.Bus:
                    EnvelopeReceived?.Invoke(message.Envelope);
                    break;

                case SocketKind.Error:
                    Debug.LogWarning($"[Social] socket error: {message.Error}");
                    break;
            }
        }

        /// <summary>Best-effort bus send. Returns false while down — callers degrade, never block.</summary>
        public bool TrySend(SocialBusEnvelope envelope)
        {
            if (!IsConnected || envelope == null)
                return false;

            string frame = SocialSocketProtocol.BuildBusMessage(
                NextCid(), _channelId, JsonUtility.ToJson(envelope));
            _ = SendFireAndForgetAsync(frame);
            return true;
        }

        private async Task SendFireAndForgetAsync(string frame)
        {
            try
            {
                CancellationToken ct = _cts != null ? _cts.Token : CancellationToken.None;
                await _transport.SendAsync(frame, ct);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Social] socket send failed: {e.Message}");
            }
        }

        /// <summary>Builds the websocket handshake URL from the REST base url and a session token.</summary>
        public static string BuildSocketUrl(string httpBaseUrl, string token)
        {
            string ws = (httpBaseUrl ?? string.Empty).Replace("https", "wss").Replace("http", "ws");
            return $"{ws}/ws?lang=en&status=false&format=json&token={Uri.EscapeDataString(token ?? string.Empty)}";
        }

        private string NextCid() => (++_cid).ToString();
    }
}
