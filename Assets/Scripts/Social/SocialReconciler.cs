using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MMO.Social
{
    /// <summary>
    /// Diffs this zone's live players against the backend and pushes roster/invite changes to their
    /// FishNet objects. The poll path is authoritative; a push only triggers an early pass.
    /// </summary>
    internal sealed class SocialReconciler
    {
        private readonly Func<ISocialRepository> _repository;
        private readonly Func<Task<bool>> _ensureConnected;
        private readonly LocalPlayerRegistry _players;
        private readonly FriendService _friends;
        private readonly RaidHandoffService _raidHandoff;
        private readonly Func<IRaidInstanceRepository> _raids;
        private readonly Action<PartyRecord> _partyChanged;
        private readonly Action<GuildRecord> _guildChanged;
        private readonly Action<GuildBankRecord> _bankChanged;
        private readonly Action<string, PendingPartyInvite> _partyInviteReceived;
        private readonly Action<string, PendingGuildInvite> _guildInviteReceived;
        private readonly Dictionary<string, SocialIndexRecord> _lastIndexes = new Dictionary<string, SocialIndexRecord>();

        public SocialReconciler(
            Func<ISocialRepository> repository,
            Func<Task<bool>> ensureConnected,
            LocalPlayerRegistry players,
            FriendService friends,
            RaidHandoffService raidHandoff,
            Func<IRaidInstanceRepository> raids,
            Action<PartyRecord> partyChanged,
            Action<GuildRecord> guildChanged,
            Action<GuildBankRecord> bankChanged,
            Action<string, PendingPartyInvite> partyInviteReceived,
            Action<string, PendingGuildInvite> guildInviteReceived)
        {
            _repository = repository;
            _ensureConnected = ensureConnected;
            _players = players;
            _friends = friends;
            _raidHandoff = raidHandoff;
            _raids = raids;
            _partyChanged = partyChanged;
            _guildChanged = guildChanged;
            _bankChanged = bankChanged;
            _partyInviteReceived = partyInviteReceived;
            _guildInviteReceived = guildInviteReceived;
        }

        /// <summary>
        /// Diffs this zone's live players against the backend and pushes roster/invite changes to
        /// their FishNet objects. Public so tests can drive one pass without waiting a frame.
        /// </summary>
        public async Task SyncAsync()
        {
            if (!await _ensureConnected() || _players.Count == 0)
                return;

            ISocialRepository repository = _repository();
            var ids = _players.AccountIds();
            List<SocialIndexRecord> indexes = await repository.LoadIndexesAsync(ids, CancellationToken.None);

            var partyIds = new HashSet<string>();
            var guildIds = new HashSet<string>();

            foreach (SocialIndexRecord index in indexes)
            {
                if (index == null || string.IsNullOrEmpty(index.AccountId))
                    continue;

                _lastIndexes.TryGetValue(index.AccountId, out SocialIndexRecord known);

                // Membership that vanished (a kick, or leaving on another zone) has to clear the
                // local roster view; the authoritative refresh below repaints the remaining members.
                if (known != null && !string.IsNullOrEmpty(known.PartyId) && string.IsNullOrEmpty(index.PartyId))
                    _partyChanged?.Invoke(null);
                if (known != null && !string.IsNullOrEmpty(known.GuildId) && string.IsNullOrEmpty(index.GuildId))
                    _guildChanged?.Invoke(null);

                if (known == null ||
                    known.PendingPartyInvite.PartyId != index.PendingPartyInvite.PartyId ||
                    known.PendingPartyInvite.ExpiresAtTicks != index.PendingPartyInvite.ExpiresAtTicks)
                {
                    if (!string.IsNullOrEmpty(index.PendingPartyInvite.PartyId))
                        _partyInviteReceived?.Invoke(index.AccountId, index.PendingPartyInvite);
                }

                if (known == null ||
                    known.PendingGuildInvite.GuildId != index.PendingGuildInvite.GuildId ||
                    known.PendingGuildInvite.ExpiresAtTicks != index.PendingGuildInvite.ExpiresAtTicks)
                {
                    if (!string.IsNullOrEmpty(index.PendingGuildInvite.GuildId))
                        _guildInviteReceived?.Invoke(index.AccountId, index.PendingGuildInvite);
                }

                _lastIndexes[index.AccountId] = index;

                // Keep the local roster's party pointer in step so party chat can route.
                if (_players.TryGet(index.AccountId, out LocalPlayerHandle localHandle))
                    localHandle.PartyId = index.PartyId;

                if (!string.IsNullOrEmpty(index.PartyId))
                    partyIds.Add(index.PartyId);

                if (!string.IsNullOrEmpty(index.GuildId))
                    guildIds.Add(index.GuildId);
            }

            foreach (string partyId in partyIds)
            {
                PartyRecord record = await repository.LoadPartyAsync(partyId, CancellationToken.None);
                if (record != null)
                    _partyChanged?.Invoke(record);

                // Detect raid completion: RaidInstanceId went from set → empty while members are local.
                if (record != null && _raids() != null)
                    await _raidHandoff.SyncCompletionAsync(record);
            }

            foreach (string guildId in guildIds)
            {
                GuildRecord record = await repository.LoadGuildAsync(guildId, CancellationToken.None);
                if (record != null)
                    _guildChanged?.Invoke(record);

                // A bank that was never written opens with the default tabs and rank rules, so a
                // brand-new guild still has something to render on every zone hosting its members.
                GuildBankRecord bank = await repository.LoadGuildBankAsync(guildId, CancellationToken.None);
                if (bank == null)
                    bank = GuildBankRules.CreateDefault(guildId);
                else
                    GuildBankRules.EnsureDefaults(bank);

                _bankChanged?.Invoke(bank);
            }

            // Repair any half-accepted friendship whose mirror write or notify was lost.
            await _friends.RepairAsync();

            // Raid handoff: a party whose raid instance is hosted on another zone must move its
            // local members there.
            if (_raids() != null)
                await _raidHandoff.SyncTransfersAsync(partyIds);
        }

        /// <summary>
        /// A peer zone pushed a social change hint. The body is never trusted beyond ids: we funnel
        /// the notification straight back into <see cref="SyncAsync"/>, which re-reads the record,
        /// re-runs the same <c>RaidRules</c> and repository validation as the poll path.
        /// </summary>
        public Task OnSocialNotification(SocialNotification notification)
        {
            if (notification == null)
                return Task.CompletedTask;

            if (notification.Kind != SocialNotificationKind.RaidAllocated &&
                notification.Kind != SocialNotificationKind.RaidCompleted)
                return Task.CompletedTask;

            return SyncAsync();
        }
    }
}
