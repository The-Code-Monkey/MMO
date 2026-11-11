using System;
using System.Collections.Generic;

namespace MMO.Social
{
    /// <summary>What changed on the authoritative side; a hint telling peer zones to re-read now.</summary>
    public enum SocialNotificationKind
    {
        RaidAllocated,
        RaidCompleted,
        MarketChanged,
    }

    /// <summary>
    /// The domain shape of one cross-zone social push. It carries ids only — never state. A peer
    /// zone resolves the party/instance through the same rules and repository reads the reconcile
    /// path uses, so a lost, duplicated, tampered or stale push can at worst cause an early correct
    /// pass (<see cref="SocialHubService.OnSocialNotification"/>).
    /// </summary>
    [Serializable]
    public sealed class SocialNotification
    {
        /// <summary>The bus <c>t</c> value every raid notification rides under.</summary>
        public const string WireType = "raid";

        /// <summary>The bus <c>t</c> value a market-cache hint rides under.</summary>
        public const string MarketWireType = "market";

        public SocialNotificationKind Kind;
        public string ListingId;
        public string RaidId;
        public string PartyId;
        public string HostZoneId;
        public List<string> AccountIds = new List<string>();

        /// <summary>Projects the domain notification onto the flat bus envelope the Lua module relays.</summary>
        public static SocialBusEnvelope ToEnvelope(SocialNotification notification)
        {
            if (notification == null)
                return null;

            bool market = notification.Kind == SocialNotificationKind.MarketChanged;
            return new SocialBusEnvelope
            {
                t = market ? MarketWireType : WireType,
                target = market ? notification.ListingId : notification.RaidId,
                accounts = notification.AccountIds != null
                    ? notification.AccountIds.ToArray()
                    : Array.Empty<string>(),
                ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            };
        }

        /// <summary>
        /// Reads a relayed bus envelope back into the domain shape. Only ids survive the wire, and
        /// even those are re-read on the receiving side — the payload is a hint, never authority.
        /// </summary>
        public static SocialNotification FromEnvelope(SocialBusEnvelope envelope)
        {
            if (envelope == null)
                return null;

            bool market = envelope.t == MarketWireType;
            var notification = new SocialNotification
            {
                Kind = market ? SocialNotificationKind.MarketChanged : SocialNotificationKind.RaidAllocated,
                RaidId = market ? null : envelope.target,
                ListingId = market ? envelope.target : null,
            };

            if (envelope.accounts != null)
                notification.AccountIds.AddRange(envelope.accounts);

            return notification;
        }
    }

    /// <summary>The Lua <c>social_notify</c> RPC's return value; delivery is always best-effort.</summary>
    [Serializable]
    public sealed class NotifyResponse
    {
        public bool ok;
    }
}
