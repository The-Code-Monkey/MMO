using System;

namespace MMO.Social
{
    /// <summary>The outcome of a guild decision. <see cref="Ok"/> is the only success.</summary>
    public enum GuildRule
    {
        Ok,
        InvalidName,
        SelfInvite,
        TargetOffline,
        TargetNotFound,
        TargetAlreadyInGuild,
        AlreadyInGuild,
        GuildFull,
        NotLeader,
        NotOfficer,
        NoGuild,
        NotInGuild,
        InviteExpired,
        NoPendingInvite,
        AlreadyAtRank,
        BackendUnavailable,
        Conflict,
    }

    /// <summary>
    /// Every guild decision as a pure function, mirroring <see cref="PartyRules"/>: the zone server
    /// validates here before it writes, and <see cref="SocialHubService"/> reuses the same checks in
    /// its reconcile pass. No network, storage, or Unity dependency.
    /// </summary>
    public static class GuildRules
    {
        public const int MaxMembers = GuildRecord.DefaultMaxMembers;
        public const int MaxNameLength = 32;
        public static readonly TimeSpan InviteTtl = TimeSpan.FromMinutes(5);

        /// <summary>Whether a guild may be created with this name by this player right now.</summary>
        public static GuildRule CanCreate(string accountId, string guildName, SocialIndexRecord index)
        {
            if (string.IsNullOrEmpty(accountId))
                return GuildRule.TargetNotFound;

            if (!IsValidName(guildName))
                return GuildRule.InvalidName;

            if (!string.IsNullOrEmpty(index?.GuildId))
                return GuildRule.AlreadyInGuild;

            return GuildRule.Ok;
        }

        /// <summary>A name is valid when it is non-blank and within the length cap.</summary>
        public static bool IsValidName(string guildName)
        {
            string trimmed = guildName?.Trim();
            return !string.IsNullOrEmpty(trimmed) && trimmed.Length <= MaxNameLength;
        }

        /// <summary>
        /// Whether <paramref name="inviterId"/> (a leader or officer) may invite the target. The
        /// guild is passed so capacity and the inviter's rank are checked before the backend write.
        /// </summary>
        public static GuildRule CanInvite(
            string inviterId,
            GuildRecord guild,
            SocialIndexRecord target,
            bool targetOnline)
        {
            if (string.IsNullOrEmpty(inviterId) || target == null || string.IsNullOrEmpty(target.AccountId))
                return GuildRule.TargetNotFound;

            if (target.AccountId == inviterId)
                return GuildRule.SelfInvite;

            if (!targetOnline)
                return GuildRule.TargetOffline;

            if (!string.IsNullOrEmpty(target.GuildId))
                return GuildRule.TargetAlreadyInGuild;

            if (guild == null)
                return GuildRule.NoGuild;

            GuildMemberRecord inviter = FindMember(guild, inviterId);
            if (inviter.AccountId == null)
                return GuildRule.NotInGuild;

            if (RankOf(inviter) < GuildRank.Officer)
                return GuildRule.NotOfficer;

            if (guild.Members.Count >= MaxMembers)
                return GuildRule.GuildFull;

            return GuildRule.Ok;
        }

        public static bool IsInviteExpired(PendingGuildInvite invite, DateTimeOffset now)
            => invite.ExpiresAtTicks <= 0 || now.UtcDateTime.Ticks > invite.ExpiresAtTicks;

        /// <summary>Whether the invite holder may accept: has a live invite, guild has room.</summary>
        public static GuildRule CanAccept(GuildRecord guild, SocialIndexRecord index, DateTimeOffset now)
        {
            if (index == null || index.PendingGuildInvite.ExpiresAtTicks <= 0)
                return GuildRule.NoPendingInvite;

            if (IsInviteExpired(index.PendingGuildInvite, now))
                return GuildRule.InviteExpired;

            if (guild == null)
                return GuildRule.NoGuild;

            if (FindMember(guild, index.AccountId).AccountId != null)
                return GuildRule.AlreadyInGuild;

            if (guild.Members.Count >= MaxMembers)
                return GuildRule.GuildFull;

            return GuildRule.Ok;
        }

        public static GuildRecord Accept(GuildRecord guild, GuildMemberRecord member)
        {
            if (guild == null)
                return null;

            if (FindMember(guild, member.AccountId).AccountId == null)
                guild.Members.Add(member);

            return guild;
        }

        /// <summary>
        /// Removes a member. Returns null when the guild disbands (its last member left); otherwise
        /// the same record. When the leader leaves, the earliest-joined officer is promoted, falling
        /// back to the earliest-joined member.
        /// </summary>
        public static GuildRecord Remove(GuildRecord guild, string accountId)
        {
            if (guild == null)
                return null;

            GuildMemberRecord member = FindMember(guild, accountId);
            if (member.AccountId == null)
                return guild;

            guild.Members.Remove(member);

            if (guild.Members.Count == 0)
                return null;

            if (guild.LeaderId == accountId)
            {
                GuildMemberRecord next = Successor(guild);
                guild.LeaderId = next.AccountId;
                int index = guild.Members.IndexOf(next);
                if (index >= 0)
                {
                    GuildMemberRecord promoted = guild.Members[index];
                    promoted.Rank = (int)GuildRank.Leader;
                    guild.Members[index] = promoted;
                }
            }

            return guild;
        }

        /// <summary>Whether <paramref name="actorId"/> may kick <paramref name="targetId"/>.</summary>
        public static GuildRule CanKick(GuildRecord guild, string actorId, string targetId)
        {
            if (guild == null)
                return GuildRule.NoGuild;

            GuildMemberRecord actor = FindMember(guild, actorId);
            if (actor.AccountId == null)
                return GuildRule.NotInGuild;

            if (targetId == actorId)
                return GuildRule.NotInGuild;

            GuildMemberRecord target = FindMember(guild, targetId);
            if (target.AccountId == null)
                return GuildRule.TargetNotFound;

            // Must strictly outrank the target; a member cannot kick anyone.
            if (RankOf(actor) <= RankOf(target))
                return GuildRule.NotOfficer;

            return GuildRule.Ok;
        }

        /// <summary>Whether <paramref name="actorId"/> may promote the target one rank.</summary>
        public static GuildRule CanPromote(GuildRecord guild, string actorId, string targetId)
        {
            if (guild == null)
                return GuildRule.NoGuild;

            GuildMemberRecord actor = FindMember(guild, actorId);
            if (actor.AccountId == null)
                return GuildRule.NotInGuild;

            if (RankOf(actor) != GuildRank.Leader)
                return GuildRule.NotLeader;

            if (targetId == actorId)
                return GuildRule.AlreadyAtRank;

            GuildMemberRecord target = FindMember(guild, targetId);
            if (target.AccountId == null)
                return GuildRule.TargetNotFound;

            if (RankOf(target) >= GuildRank.Officer)
                return GuildRule.AlreadyAtRank;

            return GuildRule.Ok;
        }

        /// <summary>Applies one promotion step (member → officer), leaving a leader untouched.</summary>
        public static GuildRecord Promote(GuildRecord guild, string accountId)
        {
            int index = IndexOf(guild, accountId);
            if (index < 0)
                return guild;

            GuildMemberRecord member = guild.Members[index];
            if (RankOf(member) < GuildRank.Officer)
            {
                member.Rank = (int)GuildRank.Officer;
                guild.Members[index] = member;
            }

            return guild;
        }

        public static GuildMemberRecord FindMember(GuildRecord guild, string accountId)
        {
            if (guild?.Members == null || string.IsNullOrEmpty(accountId))
                return default;

            for (int i = 0; i < guild.Members.Count; i++)
            {
                if (guild.Members[i].AccountId == accountId)
                    return guild.Members[i];
            }

            return default;
        }

        public static bool IsMember(GuildRecord guild, string accountId)
            => FindMember(guild, accountId).AccountId != null;

        public static GuildRank RankOf(GuildMemberRecord member)
            => (GuildRank)Math.Clamp(member.Rank, (int)GuildRank.Member, (int)GuildRank.Leader);

        private static int IndexOf(GuildRecord guild, string accountId)
        {
            if (guild?.Members == null || string.IsNullOrEmpty(accountId))
                return -1;

            for (int i = 0; i < guild.Members.Count; i++)
            {
                if (guild.Members[i].AccountId == accountId)
                    return i;
            }

            return -1;
        }

        // The successor is the earliest-joined officer, or the earliest-joined member when no
        // officer remains — one deterministic rule instead of a special case per rank.
        private static GuildMemberRecord Successor(GuildRecord guild)
        {
            GuildMemberRecord bestOfficer = default;
            GuildMemberRecord bestMember = default;

            foreach (GuildMemberRecord member in guild.Members)
            {
                if (RankOf(member) >= GuildRank.Officer &&
                    (bestOfficer.AccountId == null || member.JoinedAtTicks < bestOfficer.JoinedAtTicks))
                    bestOfficer = member;

                if (bestMember.AccountId == null || member.JoinedAtTicks < bestMember.JoinedAtTicks)
                    bestMember = member;
            }

            return bestOfficer.AccountId != null ? bestOfficer : bestMember;
        }
    }
}
