using System.Collections.Generic;

namespace MMO.Social
{
    /// <summary>Builds raid social-notification payloads from a raid or party roster.</summary>
    internal static class RaidNotificationFactory
    {
        public static SocialNotification ForRaid(SocialNotificationKind kind, RaidInstanceRecord instance)
        {
            var notification = new SocialNotification
            {
                Kind = kind,
                RaidId = instance.InstanceId,
                PartyId = instance.PartyId,
                HostZoneId = instance.HostZoneId,
            };

            if (instance.Members != null)
            {
                foreach (RaidInstanceMember member in instance.Members)
                    notification.AccountIds.Add(member.AccountId);
            }

            return notification;
        }

        public static SocialNotification ForParty(SocialNotificationKind kind, string instanceId, PartyRecord party)
        {
            var notification = new SocialNotification
            {
                Kind = kind,
                RaidId = instanceId,
                PartyId = party?.PartyId,
            };

            if (party?.Members != null)
            {
                foreach (PartyMemberRecord member in party.Members)
                    notification.AccountIds.Add(member.AccountId);
            }

            return notification;
        }
    }
}
