using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MMO.Social
{
    /// <summary>
    /// Byte-level socket seam. Production wraps <see cref="System.Net.WebSockets.ClientWebSocket"/>
    /// (connect/send/receive entirely off the main thread); tests inject a duplex fake so
    /// connect → join → route runs in EditMode with no network.
    /// </summary>
    public interface ISocketTransport
    {
        bool IsOpen { get; }

        Task OpenAsync(string url, CancellationToken ct);

        Task SendAsync(string payload, CancellationToken ct);

        /// <summary>
        /// Next text frame. Throws when the connection drops. Called from a background loop, so an
        /// implementation must be safe to call off the main thread.
        /// </summary>
        Task<string> ReceiveAsync(CancellationToken ct);

        Task CloseAsync();
    }

    /// <summary>
    /// Session access the socket needs. Implemented by the phase-0 <c>NakamaRestClient</c>, so the
    /// socket reconnects with a token that is still valid instead of replaying a stale one.
    /// </summary>
    public interface INakamaSession
    {
        Task<string> GetFreshTokenAsync(CancellationToken ct);

        /// <summary>Scheme://host:port the REST calls (and the derived socket URL) use.</summary>
        string HttpBaseUrl { get; }
    }

    /// <summary>
    /// The bus, not the socket: what <see cref="ChatService"/> and <see cref="SocialHubService"/>
    /// are coded against. <see cref="NakamaSocialSocket"/> implements it in production; tests
    /// substitute a fake that records sends and pushes inbound envelopes — no MonoBehaviour, no
    /// network, pure EditMode.
    /// </summary>
    public interface ISocialBus
    {
        bool IsConnected { get; }

        event Action<SocialBusEnvelope> EnvelopeReceived;

        /// <summary>Best-effort send. Returns false while down — callers degrade, never block.</summary>
        bool TrySend(SocialBusEnvelope envelope);
    }

    /// <summary>
    /// This zone's players. Implemented by <see cref="SocialHubService"/>'s registry, extending the
    /// account ids it already tracks. <see cref="LocalZoneId"/> mirrors the zone-transfer trigger's
    /// server id (serialized id, <c>SystemInfo.deviceName</c> fallback).
    /// </summary>
    public interface ILocalRoster
    {
        string LocalZoneId { get; }

        IReadOnlyList<LocalPlayerHandle> Local { get; }

        /// <summary>The party id a local account belongs to, or null when they have no party.</summary>
        string PartyOf(string accountId);

        /// <summary>The display name of a local account, falling back to its id.</summary>
        string NameOf(string accountId);
    }

    /// <summary>
    /// One local player's social handles. <see cref="PlayerChat"/>/<see cref="PlayerFriends"/>
    /// attach their delivery callbacks through <see cref="SocialHubService.AttachChat"/> /
    /// <see cref="SocialHubService.AttachFriends"/> on start; <see cref="PlayerParty"/> fills
    /// <see cref="PartyId"/> as the authoritative roster arrives. The delivery callbacks close over
    /// the owning NetworkConnection, so the hub never touches FishNet directly.
    /// </summary>
    public sealed class LocalPlayerHandle
    {
        public string AccountId;
        public string PlayerName;
        public string PartyId;

        /// <summary>Applies a chat envelope (JSON) to this player's client.</summary>
        public Action<string> DeliverChat;

        /// <summary>Applies a full friend-list snapshot (JSON) to this player's client.</summary>
        public Action<string> DeliverFriends;

        /// <summary>Raises a friend-event toast on this player's client.</summary>
        public Action<int> NotifyFriendEvent;
    }
}
