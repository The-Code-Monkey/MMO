using System;
using System.Threading.Tasks;
using MMO.Persistence;
using UnityEngine;

namespace MMO.Social
{
    public sealed class RaidInstanceService
    {
        private readonly ISocialRepository _social;
        private readonly IRaidInstanceRepository _raids;
        private readonly int _casRetries;

        public RaidInstanceService(ISocialRepository social, IRaidInstanceRepository raids, int casRetries = 5)
        {
            _social = social ?? throw new ArgumentNullException(nameof(social));
            _raids = raids ?? throw new ArgumentNullException(nameof(raids));
            _casRetries = casRetries;
        }

        public async Task<(RaidRule Rule, RaidInstanceRecord Instance)> AllocateAsync(
            string requesterId, string hostZoneId, string hostAddress = null, ushort hostPort = 0)
        {
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
                        HostZoneId = hostZoneId ?? "local",
                        HostAddress = hostAddress,
                        HostPort = hostPort,
                    };
                    foreach (var m in party.Members)
                        instance.Members.Add(new RaidInstanceMember { AccountId = m.AccountId, PlayerName = m.PlayerName });

                    await _raids.SaveRaidInstanceAsync(instance);
                    instanceWritten = true;

                    party.RaidInstanceId = instance.InstanceId;
                    await _social.SavePartyAsync(party);
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
                    party.RaidInstanceId = null;
                    await _social.SavePartyAsync(party);
                    if (!string.IsNullOrEmpty(instanceId))
                        await _raids.DeleteRaidInstanceAsync(instanceId);
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

        public async Task DisbandAndClearRaidAsync(string partyId)
        {
            if (string.IsNullOrEmpty(partyId))
                return;
            try
            {
                PartyRecord party = await _social.LoadPartyAsync(partyId);
                if (party == null)
                    return;
                string instanceId = party.RaidInstanceId;
                if (!string.IsNullOrEmpty(instanceId))
                {
                    party.RaidInstanceId = null;
                    await _social.SavePartyAsync(party);
                    await _raids.DeleteRaidInstanceAsync(instanceId);
                }
            }
            catch (StorageVersionConflictException)
            {
            }
            catch (Exception e)
            {
                Debug.LogError($"[Raid] Disband clear failed: {e.Message}");
            }
        }

        private static Task Backoff(int attempt)
        {
            int ms = Math.Min(8, 1 << attempt) * 10;
            return Task.Delay(ms);
        }
    }
}
