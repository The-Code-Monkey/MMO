using System;
using MMO.Auction;

namespace MMO.Social
{
    /// <summary>
    /// Owns the zone's realtime socket and routes inbound envelopes: chat to <see cref="ChatService"/>,
    /// roster hints to friend pushes, and raid notifications to the reconcile pass.
    /// </summary>
    internal sealed class SocialRealtimeRouter : ISocialBus
    {
        private readonly LocalPlayerRegistry _players;
        private readonly Func<FriendService> _friends;
        private readonly Func<SocialReconciler> _reconciler;
        private readonly Func<AuctionService> _auction;
        private ISocialBus _socket;
        private ChatService _chat;

        public SocialRealtimeRouter(
            LocalPlayerRegistry players,
            Func<FriendService> friends,
            Func<SocialReconciler> reconciler,
            Func<AuctionService> auction = null)
        {
            _players = players;
            _friends = friends;
            _reconciler = reconciler;
            _auction = auction;
        }

        public event Action<SocialBusEnvelope> EnvelopeReceived;

        public bool IsConnected => _socket != null && _socket.IsConnected;

        public bool IsAttached => _socket != null;

        public ChatService Chat => _chat;

        public bool TrySend(SocialBusEnvelope envelope)
            => _socket != null && _socket.TrySend(envelope);

        public void EnsureChat(ILocalRoster roster)
        {
            if (_chat == null)
                _chat = new ChatService(this, roster);
        }

        public void Attach(ILocalRoster roster, ISocialBus socket)
        {
            if (_socket != null)
                _socket.EnvelopeReceived -= OnEnvelope;

            _socket = socket;

            if (_socket != null)
                _socket.EnvelopeReceived += OnEnvelope;

            EnsureChat(roster);
        }

        public void Detach()
        {
            if (_socket == null)
                return;

            _socket.EnvelopeReceived -= OnEnvelope;
            _socket = null;
        }

        private void OnEnvelope(SocialBusEnvelope envelope)
        {
            if (envelope == null)
                return;

            EnvelopeReceived?.Invoke(envelope);

            switch (envelope.t)
            {
                case "chat":
                    _chat?.RouteInbound(envelope);
                    break;

                case "friend":
                case "party":
                case "guild":
                    if (envelope.accounts == null)
                        break;

                    FriendService friends = _friends();
                    if (friends == null)
                        break;

                    foreach (string accountId in envelope.accounts)
                    {
                        if (_players.Contains(accountId))
                            _ = friends.PushAsync(accountId);
                    }
                    break;

                case SocialNotification.WireType:
                    _ = _reconciler()?.OnSocialNotification(SocialNotification.FromEnvelope(envelope));
                    break;

                case SocialNotification.MarketWireType:
                    _ = _auction?.Invoke()?.RefreshSearchRowAsync(envelope.target);
                    break;
            }
        }
    }
}
