using System;
using System.Threading;
using System.Threading.Tasks;
using MMO.Net;

namespace MMO.Social
{
    /// <summary>Thin leader-facing raid intents over the raid lifecycle and orchestration services.</summary>
    internal sealed class RaidIntentsService
    {
        private readonly RaidInstanceService _raids;
        private readonly RaidOrchestrationService _orchestration;
        private readonly Func<ISocialRepository> _repository;
        private readonly Func<IRaidInstanceRepository> _raidRepository;
        private readonly Func<Task> _reconcile;

        public RaidIntentsService(
            RaidInstanceService raids,
            RaidOrchestrationService orchestration,
            Func<ISocialRepository> repository,
            Func<IRaidInstanceRepository> raidRepository,
            Func<Task> reconcile)
        {
            _raids = raids;
            _orchestration = orchestration;
            _repository = repository;
            _raidRepository = raidRepository;
            _reconcile = reconcile;
        }

        /// <summary>Leader-only: allocates a raid instance for the requester's party.</summary>
        public async Task<(RaidRule Rule, RaidInstanceRecord Instance)> AllocateAsync(string requesterId)
        {
            if (_raids == null)
                return (RaidRule.BackendUnavailable, null);

            if (_orchestration != null)
            {
                SocialIndexRecord index = await _repository().LoadIndexAsync(requesterId);
                if (index == null || string.IsNullOrEmpty(index.PartyId))
                    return (RaidRule.NoParty, null);

                PartyRecord party = await _repository().LoadPartyAsync(index.PartyId);
                var orchestrated = await _orchestration.EnterRaidAsync(party, CancellationToken.None);
                if (orchestrated.Rule == RaidRule.Ok)
                    await _reconcile();
                return orchestrated;
            }

            var result = await _raids.AllocateAsync(requesterId);
            if (result.Rule == RaidRule.Ok)
                await _reconcile();
            return result;
        }

        /// <summary>Leader-only: joins the requester's party into an existing raid.</summary>
        public async Task<RaidRule> JoinAsync(string requesterId, string raidId)
        {
            if (_raids == null)
                return RaidRule.BackendUnavailable;

            RaidRule rule = await _raids.JoinAsync(requesterId, raidId);
            if (rule == RaidRule.Ok)
                await _reconcile();
            return rule;
        }

        /// <summary>Leader-only: pulls the requester's whole party out of its raid.</summary>
        public async Task<RaidRule> LeaveAsync(string requesterId)
        {
            if (_raids == null)
                return RaidRule.BackendUnavailable;

            RaidRule rule = await _raids.LeaveAsync(requesterId);
            if (rule == RaidRule.Ok)
                await _reconcile();
            return rule;
        }

        /// <summary>Raid-leader-only: hands raid leadership to another member.</summary>
        public async Task<RaidRule> PromoteLeaderAsync(string requesterId, string targetId)
        {
            if (_raids == null)
                return RaidRule.BackendUnavailable;

            RaidRule rule = await _raids.PromoteLeaderAsync(requesterId, targetId);
            if (rule == RaidRule.Ok)
                await _reconcile();
            return rule;
        }

        /// <summary>Leader-only: completes the raid and tears its instance down.</summary>
        public async Task<RaidRule> CompleteAsync(string requesterId)
        {
            if (_raids == null)
                return RaidRule.BackendUnavailable;

            // Get the instance ID before completing, so we can tear down orchestrated instances.
            string instanceId = null;
            if (_orchestration != null)
            {
                SocialIndexRecord index = await _repository().LoadIndexAsync(requesterId);
                if (index != null && !string.IsNullOrEmpty(index.PartyId))
                {
                    PartyRecord party = await _repository().LoadPartyAsync(index.PartyId);
                    if (party != null)
                        instanceId = party.RaidInstanceId;
                }
            }

            RaidRule rule = await _raids.CompleteAsync(requesterId);
            if (rule == RaidRule.Ok)
            {
                await _reconcile();

                // Orchestrated instances have HostZoneId starting with "orchestrator-".
                if (_orchestration != null && !string.IsNullOrEmpty(instanceId))
                {
                    RaidInstanceRecord raid = await _raidRepository().LoadRaidInstanceAsync(instanceId);
                    if (raid != null && raid.HostZoneId != null && raid.HostZoneId.StartsWith("orchestrator-"))
                        await _orchestration.TeardownAsync(instanceId, CancellationToken.None);
                }
            }

            return rule;
        }
    }
}
