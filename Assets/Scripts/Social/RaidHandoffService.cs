using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MMO.Persistence;
using UnityEngine;

namespace MMO.Social
{
    /// <summary>
    /// Raid transfer/return handoff for this zone's local members: records return anchors before a
    /// transfer, raises one transfer per instance, and returns members to their origin exactly once.
    /// </summary>
    internal sealed class RaidHandoffService
    {
        private readonly Func<ISocialRepository> _repository;
        private readonly Func<IRaidInstanceRepository> _raids;
        private readonly LocalPlayerRegistry _players;
        private readonly Func<string> _zoneId;
        private readonly Func<string> _hostAddress;
        private readonly Func<ushort> _hostPort;
        private readonly int _casRetries;
        private readonly Action<RaidInstanceRecord> _raidChanged;
        private readonly Action<string, RaidInstanceRecord> _transferRequested;
        private readonly Action<string, RaidReturnAnchor> _returnRequested;

        private readonly Dictionary<string, string> _lastTransfers = new Dictionary<string, string>();
        private readonly Dictionary<string, string> _lastInstanceIds = new Dictionary<string, string>();
        private readonly Dictionary<string, RaidInstanceRecord> _hostedCache = new Dictionary<string, RaidInstanceRecord>();
        private readonly HashSet<string> _returnedMembers = new HashSet<string>();

        public RaidHandoffService(
            Func<ISocialRepository> repository,
            Func<IRaidInstanceRepository> raids,
            LocalPlayerRegistry players,
            Func<string> zoneId,
            Func<string> hostAddress,
            Func<ushort> hostPort,
            int casRetries,
            Action<RaidInstanceRecord> raidChanged,
            Action<string, RaidInstanceRecord> transferRequested,
            Action<string, RaidReturnAnchor> returnRequested)
        {
            _repository = repository;
            _raids = raids;
            _players = players;
            _zoneId = zoneId;
            _hostAddress = hostAddress;
            _hostPort = hostPort;
            _casRetries = casRetries;
            _raidChanged = raidChanged;
            _transferRequested = transferRequested;
            _returnRequested = returnRequested;
        }

        public void Forget(string accountId)
        {
            if (!string.IsNullOrEmpty(accountId))
                _lastTransfers.Remove(accountId);
        }

        /// <summary>Detects a raid pointer that went set → empty and returns each local member home.</summary>
        public async Task SyncCompletionAsync(PartyRecord record)
        {
            IRaidInstanceRepository raids = _raids();
            if (record == null || raids == null)
                return;

            _lastInstanceIds.TryGetValue(record.PartyId, out string lastInstanceId);
            string currentInstanceId = record.RaidInstanceId;

            if (!string.IsNullOrEmpty(lastInstanceId) && string.IsNullOrEmpty(currentInstanceId))
            {
                if (record.Members != null)
                {
                    foreach (PartyMemberRecord member in record.Members)
                    {
                        if (!string.IsNullOrEmpty(member.AccountId) && _players.Contains(member.AccountId))
                            await TryTransferHomeAsync(member.AccountId, lastInstanceId);
                    }
                }
            }

            if (!string.IsNullOrEmpty(currentInstanceId))
                _lastInstanceIds[record.PartyId] = currentInstanceId;
            else
                _lastInstanceIds.Remove(record.PartyId);
        }

        /// <summary>
        /// Walks this zone's hosted parties for an allocated raid and asks each local member's
        /// <see cref="PlayerRaid"/> to transfer when this zone is not the instance host.
        /// </summary>
        public async Task SyncTransfersAsync(IEnumerable<string> partyIds)
        {
            ISocialRepository repository = _repository();
            IRaidInstanceRepository raids = _raids();
            if (repository == null || raids == null)
                return;

            foreach (string partyId in partyIds)
            {
                PartyRecord party = await repository.LoadPartyAsync(partyId, CancellationToken.None);
                if (party == null || string.IsNullOrEmpty(party.RaidInstanceId))
                    continue;

                RaidInstanceRecord instance = await raids.LoadRaidInstanceAsync(party.RaidInstanceId, CancellationToken.None);

                // A pointer whose raid is gone, or whose raid no longer lists this party, is stale.
                if (instance == null || !RaidRules.IncludesParty(instance, party.PartyId))
                {
                    await RepairDanglingPointerAsync(party);
                    continue;
                }

                // Cache the raid record if this zone is the host, so anchors survive Delete.
                if (string.Equals(_zoneId(), instance.HostZoneId, StringComparison.Ordinal))
                    _hostedCache[instance.InstanceId] = instance;

                bool rosterRaised = false;
                foreach (LocalPlayerHandle local in _players.Local)
                {
                    if (!PartyRules.IsMember(party, local.AccountId))
                        continue;

                    if (!rosterRaised)
                    {
                        rosterRaised = true;
                        _raidChanged?.Invoke(instance);
                    }

                    if (!RaidRules.NeedsTransfer(instance.InstanceId, _zoneId(), instance.HostZoneId))
                    {
                        _lastTransfers.Remove(local.AccountId);
                        continue;
                    }

                    if (_lastTransfers.TryGetValue(local.AccountId, out string last) && last == instance.InstanceId)
                        continue;

                    bool anchorRecorded = await RecordReturnAnchorAsync(instance, local.AccountId);
                    if (!anchorRecorded)
                    {
                        Debug.LogWarning($"[Raid] Skipping transfer for '{local.AccountId}' — failed to record return anchor.");
                        continue;
                    }

                    _lastTransfers[local.AccountId] = instance.InstanceId;
                    _transferRequested?.Invoke(local.AccountId, instance);
                }
            }
        }

        private async Task RepairDanglingPointerAsync(PartyRecord party)
        {
            if (party == null || string.IsNullOrEmpty(party.RaidInstanceId))
                return;

            ISocialRepository repository = _repository();
            for (int i = 0; i < _casRetries; i++)
            {
                try
                {
                    PartyRecord current = await repository.LoadPartyAsync(party.PartyId, CancellationToken.None);
                    if (current == null || string.IsNullOrEmpty(current.RaidInstanceId))
                        return;

                    if (current.RaidInstanceId != party.RaidInstanceId)
                        return;

                    current.RaidInstanceId = null;
                    await repository.SavePartyAsync(current, CancellationToken.None);
                    return;
                }
                catch (StorageVersionConflictException)
                {
                    await SocialBackoff.DelayAsync(i);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Raid] Failed to repair raid pointer for '{party.PartyId}': {e.Message}");
                    return;
                }
            }
        }

        /// <summary>
        /// CAS-appends a return anchor before transferring. Returns false when the anchor could not
        /// be recorded, so the member is never sent to a host that cannot send them back.
        /// </summary>
        private async Task<bool> RecordReturnAnchorAsync(RaidInstanceRecord instance, string accountId)
        {
            IRaidInstanceRepository raids = _raids();
            if (instance == null || string.IsNullOrEmpty(accountId) || raids == null)
                return false;

            string instanceId = instance.InstanceId;
            for (int i = 0; i < _casRetries; i++)
            {
                try
                {
                    RaidInstanceRecord current = await raids.LoadRaidInstanceAsync(instanceId, CancellationToken.None);
                    if (current == null)
                        return false;

                    if (current.ReturnAnchors == null)
                        current.ReturnAnchors = new List<RaidReturnAnchor>();

                    bool exists = false;
                    foreach (RaidReturnAnchor anchor in current.ReturnAnchors)
                    {
                        if (anchor.AccountId == accountId)
                        {
                            exists = true;
                            break;
                        }
                    }

                    if (!exists)
                    {
                        current.ReturnAnchors.Add(new RaidReturnAnchor
                        {
                            AccountId = accountId,
                            OriginHost = _hostAddress(),
                            OriginPort = _hostPort(),
                            OriginServerId = _zoneId(),
                            Returned = false,
                        });
                    }

                    await raids.SaveRaidInstanceAsync(current);
                    return true;
                }
                catch (StorageVersionConflictException)
                {
                    await SocialBackoff.DelayAsync(i);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Raid] Failed to record return anchor for '{accountId}': {e.Message}");
                    return false;
                }
            }

            return false;
        }

        private async Task TryTransferHomeAsync(string accountId, string instanceId)
        {
            if (string.IsNullOrEmpty(accountId) || string.IsNullOrEmpty(instanceId))
                return;

            // Exactly-once per session.
            string key = accountId + ":" + instanceId;
            if (_returnedMembers.Contains(key))
                return;

            if (!_hostedCache.TryGetValue(instanceId, out RaidInstanceRecord raid))
                return;

            if (raid.ReturnAnchors == null)
                return;

            RaidReturnAnchor anchor = default;
            bool found = false;
            foreach (RaidReturnAnchor candidate in raid.ReturnAnchors)
            {
                if (candidate.AccountId == accountId)
                {
                    anchor = candidate;
                    found = true;
                    break;
                }
            }

            if (!found)
                return;

            bool memberStillLocal = _players.Contains(accountId);
            RaidTransferRule rule = RaidTransferRules.CanTransferBack(accountId, _zoneId(), anchor, memberStillLocal);
            if (rule != RaidTransferRule.Ok)
                return;

            _returnedMembers.Add(key);
            _returnRequested?.Invoke(accountId, anchor);
            await MarkAnchorReturnedAsync(instanceId, accountId);
        }

        private async Task MarkAnchorReturnedAsync(string instanceId, string accountId)
        {
            IRaidInstanceRepository raids = _raids();
            if (raids == null)
                return;

            for (int i = 0; i < _casRetries; i++)
            {
                try
                {
                    RaidInstanceRecord current = await raids.LoadRaidInstanceAsync(instanceId, CancellationToken.None);
                    if (current == null || current.ReturnAnchors == null)
                        return;

                    bool modified = false;
                    for (int j = 0; j < current.ReturnAnchors.Count; j++)
                    {
                        if (current.ReturnAnchors[j].AccountId == accountId && !current.ReturnAnchors[j].Returned)
                        {
                            var anchor = current.ReturnAnchors[j];
                            anchor.Returned = true;
                            current.ReturnAnchors[j] = anchor;
                            modified = true;
                            break;
                        }
                    }

                    if (!modified)
                        return;

                    await raids.SaveRaidInstanceAsync(current);
                    return;
                }
                catch (StorageVersionConflictException)
                {
                    await SocialBackoff.DelayAsync(i);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Raid] Failed to mark anchor returned for '{accountId}': {e.Message}");
                    return;
                }
            }
        }
    }
}
