using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MMO.Persistence;
using UnityEngine;

namespace MMO.Social
{
    /// <summary>
    /// The zone server's raid orchestrator. #51 gave it single-party allocation; #54 widens it to a
    /// set of member parties sharing one <see cref="RaidInstanceRecord"/>. Every mutation is a
    /// read-modify-write inside a bounded CAS retry loop: the raid record is authoritative, each
    /// member party's <see cref="PartyRecord.RaidInstanceId"/> is a derived pointer, and a pointer
    /// whose companion write failed is repaired by the reconcile pass.
    ///
    /// <para>
    /// Cross-object writes follow a defined order — append membership to the raid record first, then
    /// set the party pointer — so a crash between the two leaves a repairable state: a membership
    /// entry whose pointer never landed is re-pushed on the next call, and a pointer without a
    /// membership entry is cleared by <c>SocialHubService</c>'s reconcile pass.
    /// </para>
    /// </summary>
    public sealed class RaidInstanceService
    {
        private readonly ISocialRepository _social;
        private readonly IRaidInstanceRepository _raids;
        private readonly IZoneRegistryRepository _zones;
        private readonly int _casRetries;

        public RaidInstanceService(
            ISocialRepository social, IRaidInstanceRepository raids, IZoneRegistryRepository zones,
            int casRetries = 5)
        {
            _social = social ?? throw new ArgumentNullException(nameof(social));
            _raids = raids ?? throw new ArgumentNullException(nameof(raids));
            _zones = zones ?? throw new ArgumentNullException(nameof(zones));
            _casRetries = casRetries;
        }

        /// <summary>
        /// Leader-only: promotes the requester's full party to a raid seed. The host is chosen from
        /// the healthy zone registry (#57) before any write; with no candidate the allocation fails
        /// cleanly with <see cref="RaidRule.NoHostAvailable"/> and nothing is written. On success it
        /// creates the raid record with the party as its first (seed) member party and the chosen
        /// host recorded, then CAS's the party's pointer.
        /// </summary>
        public async Task<(RaidRule Rule, RaidInstanceRecord Instance)> AllocateAsync(string requesterId)
        {
            // Select the host up front, outside the CAS loop, so a retry overwrites the same record
            // on the same host instead of leaking an orphan instance per attempt.
            ZoneRegistryRecord host;
            List<ZoneRegistryRecord> zones;
            try
            {
                zones = await _zones.LoadZonesAsync();
            }
            catch (Exception e)
            {
                Debug.LogError($"[Raid] Host selection failed: {e.Message}");
                return (RaidRule.BackendUnavailable, null);
            }

            if (RaidHostSelection.Choose(zones, DateTimeOffset.UtcNow, out host) != RaidHostChoice.Ok)
                return (RaidRule.NoHostAvailable, null);

            // The id is minted once, outside the loop, so a CAS retry overwrites the same record
            // instead of leaking an orphan instance per attempt.
            string instanceId = Guid.NewGuid().ToString("N");
            bool instanceWritten = false;

            for (int i = 0; i < _casRetries; i++)
            {
                try
                {
                    SocialIndexRecord index = await _social.LoadIndexAsync(requesterId);
                    if (index == null || string.IsNullOrEmpty(index.PartyId))
                        return (RaidRule.NoParty, null);

                    PartyRecord party = await _social.LoadPartyAsync(index.PartyId);
                    RaidRule rule = RaidRules.CanAllocate(party, requesterId);
                    if (rule != RaidRule.Ok)
                    {
                        // Another zone won the race after we wrote our instance: drop the orphan.
                        if (instanceWritten)
                            await SafeDeleteAsync(instanceId);
                        return (rule, null);
                    }

                    var instance = new RaidInstanceRecord
                    {
                        InstanceId = instanceId,
                        PartyId = party.PartyId,
                        SeedPartyId = party.PartyId,
                        LeaderId = party.LeaderId,
                        HostZoneId = host.ServerId,
                        HostAddress = host.Host,
                        HostPort = host.Port,
                        ZoneTemplateId = host.ZoneTemplateId,
                    };
                    instance.MemberPartyIds.Add(party.PartyId);
                    RefreshMembers(instance, new[] { party });

                    await _raids.SaveRaidInstanceAsync(instance);
                    instanceWritten = true;

                    party.RaidInstanceId = instance.InstanceId;
                    await _social.SavePartyAsync(party);

                    // Best-effort capacity accounting on the chosen host so the next selection sees
                    // this instance. A missed write only costs a slightly stale load signal (the
                    // heartbeat preserves it); recency is a follow-up, not a correctness requirement.
                    await AdjustHostInstancesAsync(host.ServerId, +1);

                    // Only after the winning CAS: peers start the handoff before the next reconcile
                    // tick. A lost CAS or a rule failure never reaches this line.
                    await SafeNotifyAsync(RaidNotification(SocialNotificationKind.RaidAllocated, instance));
                    return (RaidRule.Ok, instance);
                }
                catch (StorageVersionConflictException)
                {
                    await Backoff(i);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Raid] Allocate failed: {e.Message}");
                    if (instanceWritten)
                        await SafeDeleteAsync(instanceId);
                    return (RaidRule.CasExhausted, null);
                }
            }

            if (instanceWritten)
                await SafeDeleteAsync(instanceId);
            return (RaidRule.CasExhausted, null);
        }

        /// <summary>
        /// Leader-only: joins the requester's party into an existing raid. Capacity is checked against
        /// the live roster; the membership append is CAS'd first, then the party pointer, so two
        /// parties racing the last seat cannot both win (the loser re-reads and reports
        /// <see cref="RaidRule.RaidFull"/>).
        /// </summary>
        public async Task<RaidRule> JoinAsync(string requesterId, string raidId)
        {
            if (string.IsNullOrEmpty(raidId))
                return RaidRule.NoRaid;

            for (int i = 0; i < _casRetries; i++)
            {
                try
                {
                    SocialIndexRecord index = await _social.LoadIndexAsync(requesterId);
                    if (index == null || string.IsNullOrEmpty(index.PartyId))
                        return RaidRule.NoParty;

                    PartyRecord party = await _social.LoadPartyAsync(index.PartyId);
                    if (party == null)
                        return RaidRule.NoParty;
                    if (party.LeaderId != requesterId)
                        return RaidRule.NotLeader;

                    // A retry after the pointer write lost its CAS: membership already landed, so
                    // just re-push the pointer.
                    if (party.RaidInstanceId == raidId)
                        return RaidRule.Ok;

                    RaidInstanceRecord raid = await _raids.LoadRaidInstanceAsync(raidId);
                    if (raid == null)
                        return RaidRule.NoRaid;

                    EnsureMemberList(raid);
                    if (raid.MemberPartyIds.Contains(party.PartyId))
                    {
                        party.RaidInstanceId = raidId;
                        await _social.SavePartyAsync(party);
                        return RaidRule.Ok;
                    }

                    List<PartyRecord> memberParties = await LoadMemberPartiesAsync(raid);
                    RaidRule rule = RaidRules.CanJoin(raid, party, memberParties);
                    if (rule != RaidRule.Ok)
                        return rule;

                    raid.MemberPartyIds.Add(party.PartyId);
                    memberParties.Add(party);
                    RefreshMembers(raid, memberParties);
                    await _raids.SaveRaidInstanceAsync(raid);

                    party.RaidInstanceId = raidId;
                    await _social.SavePartyAsync(party);

                    await SafeNotifyAsync(RaidNotification(SocialNotificationKind.RaidAllocated, raid));
                    return RaidRule.Ok;
                }
                catch (StorageVersionConflictException)
                {
                    await Backoff(i);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Raid] Join failed: {e.Message}");
                    return RaidRule.CasExhausted;
                }
            }

            return RaidRule.CasExhausted;
        }

        /// <summary>
        /// Leader-only: pulls the requester's whole party out of its raid. Partial teardown when
        /// other member parties remain; full teardown (delete + clear every pointer) when it was the
        /// last party.
        /// </summary>
        public async Task<RaidRule> LeaveAsync(string requesterId)
        {
            for (int i = 0; i < _casRetries; i++)
            {
                try
                {
                    SocialIndexRecord index = await _social.LoadIndexAsync(requesterId);
                    if (index == null || string.IsNullOrEmpty(index.PartyId))
                        return RaidRule.NoParty;

                    PartyRecord party = await _social.LoadPartyAsync(index.PartyId);
                    if (party == null)
                        return RaidRule.NoParty;
                    if (string.IsNullOrEmpty(party.RaidInstanceId))
                        return RaidRule.RaidMissing;
                    if (party.LeaderId != requesterId)
                        return RaidRule.NotLeader;

                    RaidInstanceRecord raid = await _raids.LoadRaidInstanceAsync(party.RaidInstanceId);
                    if (raid == null)
                    {
                        // The record vanished underneath us; just repair the dangling pointer.
                        await ClearRaidPointerAsync(party.PartyId);
                        return RaidRule.Ok;
                    }

                    EnsureMemberList(raid);
                    string raidId = raid.InstanceId;
                    List<PartyRecord> memberParties = await LoadMemberPartiesAsync(raid);
                    var originalMemberIds = new List<string>(raid.MemberPartyIds ?? new List<string>());
                    RaidInstanceRecord remaining = RaidRules.RemoveParty(raid, party.PartyId);

                    if (remaining == null)
                    {
                        // Last party left: full teardown. The snapshot still names every account.
                        await _raids.DeleteRaidInstanceAsync(raidId);
                        foreach (string pid in originalMemberIds)
                            await ClearRaidPointerAsync(pid);
                        await AdjustHostInstancesAsync(raid.HostZoneId, -1);
                        await SafeNotifyAsync(RaidNotification(SocialNotificationKind.RaidCompleted, raid));
                        return RaidRule.Ok;
                    }

                    remaining.LeaderId = RaidRules.ResolveLeader(remaining, memberParties);
                    RefreshMembers(remaining, memberParties);
                    await _raids.SaveRaidInstanceAsync(remaining);

                    party.RaidInstanceId = null;
                    await _social.SavePartyAsync(party);

                    await SafeNotifyAsync(RaidNotification(SocialNotificationKind.RaidAllocated, remaining));
                    return RaidRule.Ok;
                }
                catch (StorageVersionConflictException)
                {
                    await Backoff(i);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Raid] Leave failed: {e.Message}");
                    return RaidRule.CasExhausted;
                }
            }

            return RaidRule.CasExhausted;
        }

        /// <summary>Raid-leader-only: hands raid leadership to another member party's account.</summary>
        public async Task<RaidRule> PromoteLeaderAsync(string requesterId, string targetId)
        {
            for (int i = 0; i < _casRetries; i++)
            {
                try
                {
                    SocialIndexRecord index = await _social.LoadIndexAsync(requesterId);
                    if (index == null || string.IsNullOrEmpty(index.PartyId))
                        return RaidRule.NoParty;

                    PartyRecord party = await _social.LoadPartyAsync(index.PartyId);
                    if (party == null || string.IsNullOrEmpty(party.RaidInstanceId))
                        return RaidRule.RaidMissing;

                    RaidInstanceRecord raid = await _raids.LoadRaidInstanceAsync(party.RaidInstanceId);
                    if (raid == null)
                        return RaidRule.NoRaid;

                    EnsureMemberList(raid);
                    List<PartyRecord> memberParties = await LoadMemberPartiesAsync(raid);
                    RaidRule rule = RaidRules.CanPromote(raid, memberParties, requesterId, targetId);
                    if (rule != RaidRule.Ok)
                        return rule;

                    raid.LeaderId = targetId;
                    await _raids.SaveRaidInstanceAsync(raid);
                    await SafeNotifyAsync(RaidNotification(SocialNotificationKind.RaidAllocated, raid));
                    return RaidRule.Ok;
                }
                catch (StorageVersionConflictException)
                {
                    await Backoff(i);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Raid] Promote failed: {e.Message}");
                    return RaidRule.CasExhausted;
                }
            }

            return RaidRule.CasExhausted;
        }

        /// <summary>
        /// Removes a member party from its raid when the party itself disbands (its last member left).
        /// Partial teardown when siblings remain; the whole raid is deleted only when it was alone.
        /// Called from the party-disband path, so the party record still exists.
        /// </summary>
        public async Task RemovePartyFromRaidAsync(string partyId)
        {
            if (string.IsNullOrEmpty(partyId))
                return;

            for (int i = 0; i < _casRetries; i++)
            {
                try
                {
                    PartyRecord party = await _social.LoadPartyAsync(partyId);
                    if (party == null || string.IsNullOrEmpty(party.RaidInstanceId))
                        return;

                    RaidInstanceRecord raid = await _raids.LoadRaidInstanceAsync(party.RaidInstanceId);
                    if (raid == null)
                    {
                        await ClearRaidPointerAsync(partyId);
                        return;
                    }

                    EnsureMemberList(raid);
                    List<PartyRecord> memberParties = await LoadMemberPartiesAsync(raid);
                    RaidInstanceRecord remaining = RaidRules.RemoveParty(raid, partyId);
                    if (remaining == null)
                    {
                        await _raids.DeleteRaidInstanceAsync(raid.InstanceId);
                        await ClearRaidPointerAsync(partyId);
                        await AdjustHostInstancesAsync(raid.HostZoneId, -1);
                        await SafeNotifyAsync(RaidNotification(SocialNotificationKind.RaidCompleted, raid));
                        return;
                    }

                    remaining.LeaderId = RaidRules.ResolveLeader(remaining, memberParties);
                    RefreshMembers(remaining, memberParties);
                    await _raids.SaveRaidInstanceAsync(remaining);
                    await ClearRaidPointerAsync(partyId);
                    await SafeNotifyAsync(RaidNotification(SocialNotificationKind.RaidAllocated, remaining));
                    return;
                }
                catch (StorageVersionConflictException)
                {
                    await Backoff(i);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Raid] Partial teardown for party '{partyId}' failed: {e.Message}");
                    return;
                }
            }
        }

        /// <summary>
        /// Leader-only: completes the raid. Clears every member party's pointer and deletes the
        /// instance; a partial failure is repaired by the reconcile pass.
        /// </summary>
        public async Task<RaidRule> CompleteAsync(string requesterId)
        {
            for (int i = 0; i < _casRetries; i++)
            {
                try
                {
                    SocialIndexRecord index = await _social.LoadIndexAsync(requesterId);
                    if (index == null || string.IsNullOrEmpty(index.PartyId))
                        return RaidRule.NoParty;

                    PartyRecord party = await _social.LoadPartyAsync(index.PartyId);
                    RaidRule rule = RaidRules.CanComplete(party, requesterId);
                    if (rule != RaidRule.Ok)
                        return rule;

                    string instanceId = party.RaidInstanceId;
                    RaidInstanceRecord raid = await _raids.LoadRaidInstanceAsync(instanceId);
                    if (raid != null)
                        EnsureMemberList(raid);

                    // Multi-party: only the raid leader may complete, even if another party's leader
                    // asked. Transferred leadership is honoured; the seed alone is not special here.
                    if (raid != null && !string.IsNullOrEmpty(raid.LeaderId) && raid.LeaderId != requesterId)
                        return RaidRule.NotLeader;

                    var memberPartyIds = raid?.MemberPartyIds ?? new List<string> { party.PartyId };
                    foreach (string pid in new List<string>(memberPartyIds))
                        await ClearRaidPointerAsync(pid);

                    // Only after the winning CAS: peers clear their local raid handoff before the
                    // next reconcile tick (feeds the return pass in #55).
                    await SafeNotifyAsync(
                        PartyRaidNotification(SocialNotificationKind.RaidCompleted, instanceId, party));
                    if (!string.IsNullOrEmpty(instanceId))
                        await _raids.DeleteRaidInstanceAsync(instanceId);
                    if (raid != null)
                        await AdjustHostInstancesAsync(raid.HostZoneId, -1);
                    return RaidRule.Ok;
                }
                catch (StorageVersionConflictException)
                {
                    await Backoff(i);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Raid] Complete failed: {e.Message}");
                    return RaidRule.CasExhausted;
                }
            }
            return RaidRule.CasExhausted;
        }

        /// <summary>
        /// Clears a disbanding party's raid membership. Kept for the #51 call site and used by the
        /// reconcile path: it now performs the same partial teardown as a party leaving the raid.
        /// </summary>
        public Task DisbandAndClearRaidAsync(string partyId)
            => RemovePartyFromRaidAsync(partyId);

        /// <summary>
        /// Normalizes a legacy #51 record, which carried only a single <see cref="RaidInstanceRecord.PartyId"/>,
        /// into the multi-party shape before it is mutated. Idempotent.
        /// </summary>
        private static void EnsureMemberList(RaidInstanceRecord raid)
        {
            if (raid.MemberPartyIds == null)
                raid.MemberPartyIds = new List<string>();
            if (raid.MemberPartyIds.Count == 0 && !string.IsNullOrEmpty(raid.PartyId))
                raid.MemberPartyIds.Add(raid.PartyId);
            if (string.IsNullOrEmpty(raid.SeedPartyId))
                raid.SeedPartyId = raid.PartyId;
        }

        private async Task<List<PartyRecord>> LoadMemberPartiesAsync(RaidInstanceRecord raid)
        {
            var parties = new List<PartyRecord>();
            if (raid?.MemberPartyIds == null)
                return parties;

            foreach (string partyId in raid.MemberPartyIds)
            {
                PartyRecord party = await _social.LoadPartyAsync(partyId);
                if (party != null)
                    parties.Add(party);
            }

            return parties;
        }

        /// <summary>Refreshes the derived flat <see cref="RaidInstanceRecord.Members"/> snapshot.</summary>
        private static void RefreshMembers(RaidInstanceRecord raid, IReadOnlyList<PartyRecord> memberParties)
        {
            if (raid.Members == null)
                raid.Members = new List<RaidInstanceMember>();
            raid.Members.Clear();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            if (raid.MemberPartyIds == null || memberParties == null)
                return;

            foreach (string partyId in raid.MemberPartyIds)
            {
                PartyRecord party = FindParty(memberParties, partyId);
                if (party?.Members == null)
                    continue;

                foreach (PartyMemberRecord member in party.Members)
                {
                    if (!string.IsNullOrEmpty(member.AccountId) && seen.Add(member.AccountId))
                        raid.Members.Add(new RaidInstanceMember
                        {
                            AccountId = member.AccountId,
                            PlayerName = member.PlayerName,
                        });
                }
            }
        }

        private static PartyRecord FindParty(IReadOnlyList<PartyRecord> parties, string partyId)
        {
            for (int i = 0; i < parties.Count; i++)
            {
                if (parties[i] != null && parties[i].PartyId == partyId)
                    return parties[i];
            }

            return null;
        }

        /// <summary>
        /// Clears one party's raid pointer, retrying on a version conflict. Best-effort: the reconcile
        /// pass is the backstop, so a failure is logged and never thrown to the caller.
        /// </summary>
        private async Task ClearRaidPointerAsync(string partyId)
        {
            for (int i = 0; i < _casRetries; i++)
            {
                try
                {
                    PartyRecord party = await _social.LoadPartyAsync(partyId);
                    if (party == null || string.IsNullOrEmpty(party.RaidInstanceId))
                        return;

                    party.RaidInstanceId = null;
                    await _social.SavePartyAsync(party);
                    return;
                }
                catch (StorageVersionConflictException)
                {
                    await Backoff(i);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Raid] Failed to clear raid pointer for party '{partyId}': {e.Message}");
                    return;
                }
            }
        }

        private async Task SafeDeleteAsync(string instanceId)
        {
            try
            {
                await _raids.DeleteRaidInstanceAsync(instanceId);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Raid] Failed to clean up orphan instance '{instanceId}': {e.Message}");
            }
        }

        /// <summary>
        /// Best-effort CAS bump of a zone's <see cref="ZoneRegistryRecord.ActiveInstances"/> so the
        /// next selection sees the new load. A missed write only leaves the load signal slightly
        /// stale (the zone's own heartbeat preserves the last stored value) and never fails the
        /// allocation, so a failure is logged and never thrown to the caller.
        /// </summary>
        private async Task AdjustHostInstancesAsync(string serverId, int delta)
        {
            if (string.IsNullOrEmpty(serverId))
                return;

            for (int i = 0; i < _casRetries; i++)
            {
                try
                {
                    List<ZoneRegistryRecord> zones = await _zones.LoadZonesAsync();
                    ZoneRegistryRecord zone = FindZone(zones, serverId);
                    if (zone == null)
                        return;

                    zone.ActiveInstances = Math.Max(0, zone.ActiveInstances + delta);
                    await _zones.SaveZoneAsync(zone);
                    return;
                }
                catch (StorageVersionConflictException)
                {
                    await Backoff(i);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Raid] Failed to adjust host instance count for '{serverId}': {e.Message}");
                    return;
                }
            }
        }

        private static ZoneRegistryRecord FindZone(IReadOnlyList<ZoneRegistryRecord> zones, string serverId)
        {
            if (zones == null)
                return null;

            for (int i = 0; i < zones.Count; i++)
            {
                if (zones[i] != null && zones[i].ServerId == serverId)
                    return zones[i];
            }

            return null;
        }

        private static SocialNotification RaidNotification(
            SocialNotificationKind kind, RaidInstanceRecord instance)
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

        private static SocialNotification PartyRaidNotification(
            SocialNotificationKind kind, string instanceId, PartyRecord party)
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

        /// <summary>
        /// Best-effort by contract: a failed notify must never fail the allocation, because the
        /// reconcile pass (<c>SocialHubService.SyncAsync</c>) will converge every host within one
        /// tick anyway (the fail-open posture from #36).
        /// </summary>
        private async Task SafeNotifyAsync(SocialNotification notification)
        {
            try
            {
                await _social.NotifySocialAsync(notification);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Raid] social_notify push failed; reconcile will converge. {e.Message}");
            }
        }

        private static Task Backoff(int attempt)
        {
            int ms = Math.Min(8, 1 << attempt) * 10;
            return Task.Delay(ms);
        }
    }
}
