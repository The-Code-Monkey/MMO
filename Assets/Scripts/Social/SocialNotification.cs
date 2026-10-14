using System;
using System.Collections.Generic;

namespace MMO.Social
{
    /// <summary>What changed on the authoritative side; a hint telling peer zones to re-read now.</summary>
    public enum SocialNotificationKind
    {
        RaidAllocated,
        RaidCompleted,
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

        public SocialNotificationKind Kind;
        public string RaidId;
        public string PartyId;
        public string HostZoneId;
        public List<string> AccountIds = new List<string>();

        /// <summary>Projects the domain notification onto the flat bus envelope the Lua module relays.</summary>
        public static SocialBusEnvelope ToEnvelope(SocialNotification notification)
        {
            if (notification == null)
                return null;

            return new SocialBusEnvelope
            {
                t = WireType,
                target = notification.RaidId,
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

            var notification = new SocialNotification
            {
                Kind = SocialNotificationKind.RaidAllocated,
                RaidId = envelope.target,
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
