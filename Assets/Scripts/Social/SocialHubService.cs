using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FishNet.Managing;
using MMO.Persistence;
using UnityEngine;

namespace MMO.Social
{
    /// <summary>
    /// Server-side owner of all social state on this zone. Sits on the NetworkManager next to
    /// <see cref="PlayerPersistenceService"/> (wired by <c>Net.NetworkManagerFactory</c>). Clients
    /// never call Nakama; they send FishNet intents to <see cref="PlayerParty"/>/<see cref="PlayerGuild"/>,
    /// which land here.
    ///
    /// <para>
    /// Every mutation is a read-modify-write through a bounded CAS retry loop, so N zone servers can
    /// mutate one shared object: the party/guild object is authoritative, the per-account
    /// <see cref="SocialIndexRecord"/> pointers are derived, and the reconcile pass repairs a pointer
    /// whose companion write failed. A pass every <see cref="_syncInterval"/> diffs this zone's live
    /// players against the backend and pushes changes to the local FishNet rosters — that pass is
    /// what makes an invite sent on Zone B appear for a player on Zone A.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("MMO/Social/Social Hub Service")]
    public sealed class SocialHubService : MonoBehaviour
    {
        public static SocialHubService Instance { get; private set; }

        [SerializeField, Min(0.5f)] private float _syncInterval = 2f;
        [SerializeField, Min(1)] private int _casRetries = 5;
        [SerializeField, Min(0.5f)] private float _presenceInterval = 30f;

        /// <summary>How long to wait before trying the backend again after a failed connect.</summary>
        [SerializeField, Min(1)] private float _connectRetrySeconds = 10f;

        [SerializeField] private string _zoneId = "local";

        private readonly Dictionary<string, LocalPlayer> _local = new Dictionary<string, LocalPlayer>();
        private readonly Dictionary<string, SocialIndexRecord> _lastIndexes = new Dictionary<string, SocialIndexRecord>();

        private ISocialRepository _repository;
        private NakamaRestClient _rest;
        private NetworkManager _manager;
        private bool _connected;
        private bool _subscribed;
        private Task<bool> _connectTask;
        private float _nextSyncTime;
        private float _nextPresenceTime;
        private float _nextConnectAttemptTime;

        /// <summary>Raised with the authoritative party whenever any party this zone hosts changes.</summary>
        public event Action<PartyRecord> PartyChanged;

        /// <summary>Raised with the authoritative guild whenever a guild this zone hosts changes.</summary>
        public event Action<GuildRecord> GuildChanged;

        /// <summary>Raised for the one invited account; delivery is a <c>TargetRpc</c> underneath.</summary>
        public event Action<string, PendingPartyInvite> PartyInviteReceived;

        /// <summary>Raised for the one invited account's guild invite.</summary>
        public event Action<string, PendingGuildInvite> GuildInviteReceived;

        /// <summary>The zone label stamped into presence so a member's host zone can be resolved.</summary>
        public string ZoneId
        {
            get => _zoneId;
            set => _zoneId = value ?? "local";
        }

        /// <summary>Whether the backend is currently reachable (fakes are considered always up).</summary>
        public bool IsConnected => _connected;

        private void Awake()
        {
            Instance = this;
            if (_repository == null)
            {
                _rest = new NakamaRestClient(new BackendSettings());
                Configure(new NakamaSocialRepository(_rest));
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        /// <summary>
        /// Points the service at a repository. Production reaches the Nakama-backed default in
        /// <see cref="Awake"/>; tests inject an in-memory fake here, which needs no connection.
        /// </summary>
        public void Configure(ISocialRepository repository)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));

            if (repository is NakamaSocialRepository nakama)
            {
                _rest = nakama.Rest;
                _connected = _rest.IsConnected;
            }
            else
            {
                _rest = null;
                _connected = true;
            }
        }

        #region Registration + lifecycle

        private void Start()
        {
            _manager = GetComponentInParent<NetworkManager>();
            _nextSyncTime = Time.time + _syncInterval;
            _nextPresenceTime = Time.time + _presenceInterval;
        }

        /// <summary>
        /// Called by <see cref="PlayerParty"/> (server) once the player object has spawned. The
        /// local FishNet views subscribe to <see cref="PartyChanged"/>/<see cref="GuildChanged"/>
        /// themselves; the hub only needs the account id and display name.
        /// </summary>
        public void RegisterLocal(string accountId, string playerName)
        {
            if (string.IsNullOrEmpty(accountId))
                return;

            var local = new LocalPlayer
            {
                AccountId = accountId,
                PlayerName = string.IsNullOrEmpty(playerName) ? accountId : playerName,
            };

            _local[accountId] = local;
            _ = AnnounceAsync(local);
        }

        /// <summary>Called by <see cref="PlayerParty"/> (server) when the player object despawns.</summary>
        public void UnregisterLocal(string accountId)
        {
            if (!string.IsNullOrEmpty(accountId))
                _local.Remove(accountId);
        }

        private void Update()
        {
            if (_manager == null)
                _manager = GetComponentInParent<NetworkManager>();

            if (_manager != null && !_subscribed && _manager.ServerManager != null)
            {
                _manager.ServerManager.OnServerConnectionState += OnServerConnectionState;
                _subscribed = true;
            }

            // Connecting is driven by the server-start event and by on-demand mutations; the tick
            // loop only reconciles once the backend is actually up, so an unreachable backend does
            // not hot-loop a reconnect every frame.
            if (_repository == null || _manager == null || !_manager.IsServerStarted || !_connected)
                return;

            if (_local.Count == 0)
                return;

            if (Time.time >= _nextPresenceTime)
            {
                _nextPresenceTime = Time.time + _presenceInterval;
                _ = HeartbeatAsync();
            }

            if (Time.time >= _nextSyncTime)
            {
                _nextSyncTime = Time.time + _syncInterval;
                _ = SyncAsync();
            }
        }

        private void OnServerConnectionState(FishNet.Transporting.ServerConnectionStateArgs args)
        {
            if (args.ConnectionState == FishNet.Transporting.LocalConnectionState.Started)
                _ = EnsureConnectedAsync();
        }

        private void OnDisable()
        {
            if (_manager != null && _manager.ServerManager != null)
                _manager.ServerManager.OnServerConnectionState -= OnServerConnectionState;

            _subscribed = false;
        }

        private Task<bool> EnsureConnectedAsync()
        {
            if (_connected)
                return Task.FromResult(true);

            if (_rest == null)
                return Task.FromResult(false);

            if (_connectTask != null)
                return _connectTask;

            // De-bounce a dead backend: the reconcile tick would otherwise re-attempt on every
            // pass, and each attempt is a network call with its own timeout.
            if (Time.realtimeSinceStartup < _nextConnectAttemptTime)
                return Task.FromResult(false);

            _nextConnectAttemptTime = Time.realtimeSinceStartup + _connectRetrySeconds;
            _connectTask = ConnectCoreAsync();
            return _connectTask;
        }

        private async Task<bool> ConnectCoreAsync()
        {
            try
            {
                await _rest.ConnectAsync(CancellationToken.None);
                _connected = true;
                Debug.Log("[Social] Connected to the backend.");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Social] Backend unavailable; social features are read-only until it returns. {e.Message}");
                _connectTask = null;
                return false;
            }
        }

        #endregion

        #region Party intents

        /// <summary>Invites the named player to the inviter's party, creating one if needed.</summary>
        public async Task<PartyRule> InviteAsync(string inviterId, string targetName)
        {
            if (!await EnsureConnectedAsync())
                return PartyRule.BackendUnavailable;

            string targetId = await _repository.ResolveNameAsync(Normalize(targetName), CancellationToken.None);
            if (string.IsNullOrEmpty(targetId))
                return PartyRule.TargetNotFound;

            DateTimeOffset now = DateTimeOffset.UtcNow;
            PresenceRecord targetPresence = await _repository.LoadPresenceAsync(targetId, CancellationToken.None);
            bool targetOnline = targetPresence != null && targetPresence.IsHeartbeatFresh(now);

            PartyRule inviteResult = await RetryPartyAsync(async () =>
            {
                SocialIndexRecord inviterIndex = await LoadOrCreateIndexAsync(inviterId);
                SocialIndexRecord targetIndex = await LoadOrCreateIndexAsync(targetId);

                PartyRecord inviterParty = null;
                if (!string.IsNullOrEmpty(inviterIndex.PartyId))
                    inviterParty = await _repository.LoadPartyAsync(inviterIndex.PartyId, CancellationToken.None);

                PartyRule rule = PartyRules.CanInvite(inviterId, inviterParty, targetIndex, targetOnline);
                if (rule != PartyRule.Ok)
                    return rule;

                if (inviterParty == null)
                {
                    // Claim the pointer before the party exists: a conflict on this index save
                    // aborts the attempt with no party written, so a retry cannot orphan one. A
                    // crash between the two writes leaves the pointer dangling, which the reconcile
                    // pass repairs — never a leaked party.
                    string partyId = Guid.NewGuid().ToString("N");
                    inviterIndex.PartyId = partyId;
                    await _repository.SaveIndexAsync(inviterIndex, CancellationToken.None);

                    inviterParty = new PartyRecord { PartyId = partyId, LeaderId = inviterId };
                    inviterParty.Members.Add(NewMember(inviterId, inviterIndex, now));
                    await _repository.SavePartyAsync(inviterParty, CancellationToken.None);
                }

                string fromName = await ResolveDisplayNameAsync(inviterId, inviterIndex);
                targetIndex.PendingPartyInvite = new PendingPartyInvite
                {
                    FromAccountId = inviterId,
                    FromPlayerName = fromName,
                    PartyId = inviterParty.PartyId,
                    ExpiresAtTicks = now.Add(PartyRules.InviteTtl).UtcDateTime.Ticks,
                };
                await _repository.SaveIndexAsync(targetIndex, CancellationToken.None);
                return PartyRule.Ok;
            });

            if (inviteResult == PartyRule.Ok)
            {
                PendingPartyInvite invite = default;
                SocialIndexRecord target = await _repository.LoadIndexAsync(targetId, CancellationToken.None);
                if (target != null)
                    invite = target.PendingPartyInvite;

                PartyInviteReceived?.Invoke(targetId, invite);
            }

            return inviteResult;
        }

        /// <summary>Accepts the party invite this account holds.</summary>
        public async Task<PartyRule> AcceptAsync(string accountId)
        {
            if (!await EnsureConnectedAsync())
                return PartyRule.BackendUnavailable;

            DateTimeOffset now = DateTimeOffset.UtcNow;
            PartyRule result = await RetryPartyAsync(async () =>
            {
                SocialIndexRecord index = await _repository.LoadIndexAsync(accountId, CancellationToken.None);
                PartyRecord party = null;
                if (index != null && !string.IsNullOrEmpty(index.PendingPartyInvite.PartyId))
                    party = await _repository.LoadPartyAsync(index.PendingPartyInvite.PartyId, CancellationToken.None);

                PartyRule rule = PartyRules.CanAccept(party, index, now);
                if (rule != PartyRule.Ok)
                    return rule;

                PartyRules.Accept(party, NewMember(accountId, index, now));
                await _repository.SavePartyAsync(party, CancellationToken.None);

                index.PartyId = party.PartyId;
                index.PendingPartyInvite = default;
                await _repository.SaveIndexAsync(index, CancellationToken.None);
                return PartyRule.Ok;
            });

            if (result == PartyRule.Ok)
                await SyncAsync();

            return result;
        }

        /// <summary>Declines the party invite this account holds.</summary>
        public async Task<PartyRule> DeclineAsync(string accountId)
        {
            if (!await EnsureConnectedAsync())
                return PartyRule.BackendUnavailable;

            return await RetryPartyAsync(async () =>
            {
                SocialIndexRecord index = await _repository.LoadIndexAsync(accountId, CancellationToken.None);
                if (index == null || index.PendingPartyInvite.ExpiresAtTicks <= 0)
                    return PartyRule.NoParty;

                index.PendingPartyInvite = default;
                await _repository.SaveIndexAsync(index, CancellationToken.None);
                return PartyRule.Ok;
            });
        }

        /// <summary>Leaves the party this account belongs to, disbanding it when it was the last member.</summary>
        public async Task<PartyRule> LeaveAsync(string accountId)
        {
            if (!await EnsureConnectedAsync())
                return PartyRule.BackendUnavailable;

            PartyRule result = await RetryPartyAsync(async () =>
            {
                SocialIndexRecord index = await _repository.LoadIndexAsync(accountId, CancellationToken.None);
                if (index == null || string.IsNullOrEmpty(index.PartyId))
                    return PartyRule.NoParty;

                PartyRecord party = await _repository.LoadPartyAsync(index.PartyId, CancellationToken.None);
                if (party == null)
                {
                    index.PartyId = null;
                    index.PendingPartyInvite = default;
                    await _repository.SaveIndexAsync(index, CancellationToken.None);
                    return PartyRule.Ok;
                }

                PartyRecord remaining = PartyRules.Remove(party, accountId);
                if (remaining == null)
                    await _repository.DeletePartyAsync(party.PartyId, CancellationToken.None);
                else
                    await _repository.SavePartyAsync(remaining, CancellationToken.None);

                index.PartyId = null;
                index.PendingPartyInvite = default;
                await _repository.SaveIndexAsync(index, CancellationToken.None);
                return PartyRule.Ok;
            });

            if (result == PartyRule.Ok)
                await SyncAsync();

            return result;
        }

        /// <summary>Leader-only: removes <paramref name="targetId"/> from the leader's party.</summary>
        public async Task<PartyRule> KickAsync(string leaderId, string targetId)
        {
            if (!await EnsureConnectedAsync())
                return PartyRule.BackendUnavailable;

            PartyRule result = await RetryPartyAsync(async () =>
            {
                SocialIndexRecord leaderIndex = await _repository.LoadIndexAsync(leaderId, CancellationToken.None);
                if (leaderIndex == null || string.IsNullOrEmpty(leaderIndex.PartyId))
                    return PartyRule.NoParty;

                PartyRecord party = await _repository.LoadPartyAsync(leaderIndex.PartyId, CancellationToken.None);
                PartyRule rule = PartyRules.CanKick(party, leaderId, targetId);
                if (rule != PartyRule.Ok)
                    return rule;

                PartyRecord remaining = PartyRules.Remove(party, targetId);
                if (remaining == null)
                    await _repository.DeletePartyAsync(party.PartyId, CancellationToken.None);
                else
                    await _repository.SavePartyAsync(remaining, CancellationToken.None);

                SocialIndexRecord targetIndex = await _repository.LoadIndexAsync(targetId, CancellationToken.None);
                if (targetIndex != null)
                {
                    targetIndex.PartyId = null;
                    targetIndex.PendingPartyInvite = default;
                    await _repository.SaveIndexAsync(targetIndex, CancellationToken.None);
                }

                return PartyRule.Ok;
            });

            if (result == PartyRule.Ok)
                await SyncAsync();

            return result;
        }

        #endregion

        #region Guild intents

        /// <summary>Creates a guild with this account as its leader.</summary>
        public async Task<GuildRule> CreateGuildAsync(string accountId, string guildName)
        {
            if (!await EnsureConnectedAsync())
                return GuildRule.BackendUnavailable;

            DateTimeOffset now = DateTimeOffset.UtcNow;
            GuildRule result = await RetryPartyAsyncGuild(async () =>
            {
                SocialIndexRecord index = await LoadOrCreateIndexAsync(accountId);
                GuildRule rule = GuildRules.CanCreate(accountId, guildName, index);
                if (rule != GuildRule.Ok)
                    return rule;

                // Claim the pointer before the guild exists, for the same reason the party invite
                // claims the leader's index first: a conflict aborts cleanly and the reconcile pass
                // repairs a pointer whose guild write never landed.
                string guildId = Guid.NewGuid().ToString("N");
                index.GuildId = guildId;
                index.PendingGuildInvite = default;
                await _repository.SaveIndexAsync(index, CancellationToken.None);

                var guild = new GuildRecord
                {
                    GuildId = guildId,
                    Name = guildName.Trim(),
                    LeaderId = accountId,
                };
                guild.Members.Add(new GuildMemberRecord
                {
                    AccountId = accountId,
                    PlayerName = await ResolveDisplayNameAsync(accountId, index),
                    Rank = (int)GuildRank.Leader,
                    JoinedAtTicks = now.UtcDateTime.Ticks,
                });
                await _repository.SaveGuildAsync(guild, CancellationToken.None);
                return GuildRule.Ok;
            });

            if (result == GuildRule.Ok)
                await SyncAsync();

            return result;
        }

        /// <summary>Leader/officer-only: invites the named player to the inviter's guild.</summary>
        public async Task<GuildRule> InviteToGuildAsync(string inviterId, string targetName)
        {
            if (!await EnsureConnectedAsync())
                return GuildRule.BackendUnavailable;

            string targetId = await _repository.ResolveNameAsync(Normalize(targetName), CancellationToken.None);
            if (string.IsNullOrEmpty(targetId))
                return GuildRule.TargetNotFound;

            DateTimeOffset now = DateTimeOffset.UtcNow;
            PresenceRecord targetPresence = await _repository.LoadPresenceAsync(targetId, CancellationToken.None);
            bool targetOnline = targetPresence != null && targetPresence.IsHeartbeatFresh(now);

            GuildRule result = await RetryPartyAsyncGuild(async () =>
            {
                SocialIndexRecord inviterIndex = await _repository.LoadIndexAsync(inviterId, CancellationToken.None);
                SocialIndexRecord targetIndex = await LoadOrCreateIndexAsync(targetId);

                GuildRecord guild = null;
                if (inviterIndex != null && !string.IsNullOrEmpty(inviterIndex.GuildId))
                    guild = await _repository.LoadGuildAsync(inviterIndex.GuildId, CancellationToken.None);

                GuildRule rule = GuildRules.CanInvite(inviterId, guild, targetIndex, targetOnline);
                if (rule != GuildRule.Ok)
                    return rule;

                targetIndex.PendingGuildInvite = new PendingGuildInvite
                {
                    FromAccountId = inviterId,
                    FromPlayerName = await ResolveDisplayNameAsync(inviterId, inviterIndex),
                    GuildId = guild.GuildId,
                    ExpiresAtTicks = now.Add(GuildRules.InviteTtl).UtcDateTime.Ticks,
                };
                await _repository.SaveIndexAsync(targetIndex, CancellationToken.None);
                return GuildRule.Ok;
            });

            if (result == GuildRule.Ok)
            {
                SocialIndexRecord target = await _repository.LoadIndexAsync(targetId, CancellationToken.None);
                if (target != null)
                    GuildInviteReceived?.Invoke(targetId, target.PendingGuildInvite);
            }

            return result;
        }

        /// <summary>Accepts the guild invite this account holds.</summary>
        public async Task<GuildRule> AcceptGuildInviteAsync(string accountId)
        {
            if (!await EnsureConnectedAsync())
                return GuildRule.BackendUnavailable;

            DateTimeOffset now = DateTimeOffset.UtcNow;
            GuildRule result = await RetryPartyAsyncGuild(async () =>
            {
                SocialIndexRecord index = await _repository.LoadIndexAsync(accountId, CancellationToken.None);
                GuildRecord guild = null;
                if (index != null && !string.IsNullOrEmpty(index.PendingGuildInvite.GuildId))
                    guild = await _repository.LoadGuildAsync(index.PendingGuildInvite.GuildId, CancellationToken.None);

                GuildRule rule = GuildRules.CanAccept(guild, index, now);
                if (rule != GuildRule.Ok)
                    return rule;

                GuildRules.Accept(guild, new GuildMemberRecord
                {
                    AccountId = accountId,
                    PlayerName = await ResolveDisplayNameAsync(accountId, index),
                    Rank = (int)GuildRank.Member,
                    JoinedAtTicks = now.UtcDateTime.Ticks,
                });
                await _repository.SaveGuildAsync(guild, CancellationToken.None);

                index.GuildId = guild.GuildId;
                index.PendingGuildInvite = default;
                await _repository.SaveIndexAsync(index, CancellationToken.None);
                return GuildRule.Ok;
            });

            if (result == GuildRule.Ok)
                await SyncAsync();

            return result;
        }

        /// <summary>Leaves the guild this account belongs to, disbanding it when it was the last member.</summary>
        public async Task<GuildRule> LeaveGuildAsync(string accountId)
        {
            if (!await EnsureConnectedAsync())
                return GuildRule.BackendUnavailable;

            GuildRule result = await RetryPartyAsyncGuild(async () =>
            {
                SocialIndexRecord index = await _repository.LoadIndexAsync(accountId, CancellationToken.None);
                if (index == null || string.IsNullOrEmpty(index.GuildId))
                    return GuildRule.NoGuild;

                GuildRecord guild = await _repository.LoadGuildAsync(index.GuildId, CancellationToken.None);
                if (guild == null)
                {
                    index.GuildId = null;
                    index.PendingGuildInvite = default;
                    await _repository.SaveIndexAsync(index, CancellationToken.None);
                    return GuildRule.Ok;
                }

                GuildRecord remaining = GuildRules.Remove(guild, accountId);
                if (remaining == null)
                    await _repository.DeleteGuildAsync(guild.GuildId, CancellationToken.None);
                else
                    await _repository.SaveGuildAsync(remaining, CancellationToken.None);

                index.GuildId = null;
                index.PendingGuildInvite = default;
                await _repository.SaveIndexAsync(index, CancellationToken.None);
                return GuildRule.Ok;
            });

            if (result == GuildRule.Ok)
                await SyncAsync();

            return result;
        }

        /// <summary>Leader-only: promotes the target one rank.</summary>
        public async Task<GuildRule> PromoteAsync(string leaderId, string targetId)
        {
            if (!await EnsureConnectedAsync())
                return GuildRule.BackendUnavailable;

            GuildRule result = await RetryPartyAsyncGuild(async () =>
            {
                SocialIndexRecord leaderIndex = await _repository.LoadIndexAsync(leaderId, CancellationToken.None);
                if (leaderIndex == null || string.IsNullOrEmpty(leaderIndex.GuildId))
                    return GuildRule.NoGuild;

                GuildRecord guild = await _repository.LoadGuildAsync(leaderIndex.GuildId, CancellationToken.None);
                GuildRule rule = GuildRules.CanPromote(guild, leaderId, targetId);
                if (rule != GuildRule.Ok)
                    return rule;

                GuildRules.Promote(guild, targetId);
                await _repository.SaveGuildAsync(guild, CancellationToken.None);
                return GuildRule.Ok;
            });

            if (result == GuildRule.Ok)
                await SyncAsync();

            return result;
        }

        /// <summary>Leader/officer-only: removes the target from the guild.</summary>
        public async Task<GuildRule> KickFromGuildAsync(string actorId, string targetId)
        {
            if (!await EnsureConnectedAsync())
                return GuildRule.BackendUnavailable;

            GuildRule result = await RetryPartyAsyncGuild(async () =>
            {
                SocialIndexRecord actorIndex = await _repository.LoadIndexAsync(actorId, CancellationToken.None);
                if (actorIndex == null || string.IsNullOrEmpty(actorIndex.GuildId))
                    return GuildRule.NoGuild;

                GuildRecord guild = await _repository.LoadGuildAsync(actorIndex.GuildId, CancellationToken.None);
                GuildRule rule = GuildRules.CanKick(guild, actorId, targetId);
                if (rule != GuildRule.Ok)
                    return rule;

                GuildRecord remaining = GuildRules.Remove(guild, targetId);
                if (remaining == null)
                    await _repository.DeleteGuildAsync(guild.GuildId, CancellationToken.None);
                else
                    await _repository.SaveGuildAsync(remaining, CancellationToken.None);

                SocialIndexRecord targetIndex = await _repository.LoadIndexAsync(targetId, CancellationToken.None);
                if (targetIndex != null)
                {
                    targetIndex.GuildId = null;
                    targetIndex.PendingGuildInvite = default;
                    await _repository.SaveIndexAsync(targetIndex, CancellationToken.None);
                }

                return GuildRule.Ok;
            });

            if (result == GuildRule.Ok)
                await SyncAsync();

            return result;
        }

        #endregion

        #region Reconcile (cross-zone fanout)

        /// <summary>
        /// Diffs this zone's live players against the backend and pushes roster/invite changes to
        /// their FishNet objects. Public so tests can drive one pass without waiting a frame.
        /// </summary>
        public async Task SyncAsync()
        {
            if (!await EnsureConnectedAsync() || _local.Count == 0)
                return;

            var ids = new List<string>(_local.Keys);
            List<SocialIndexRecord> indexes = await _repository.LoadIndexesAsync(ids, CancellationToken.None);

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
                    PartyChanged?.Invoke(null);
                if (known != null && !string.IsNullOrEmpty(known.GuildId) && string.IsNullOrEmpty(index.GuildId))
                    GuildChanged?.Invoke(null);

                if (known == null ||
                    known.PendingPartyInvite.PartyId != index.PendingPartyInvite.PartyId ||
                    known.PendingPartyInvite.ExpiresAtTicks != index.PendingPartyInvite.ExpiresAtTicks)
                {
                    if (!string.IsNullOrEmpty(index.PendingPartyInvite.PartyId))
                        PartyInviteReceived?.Invoke(index.AccountId, index.PendingPartyInvite);
                }

                if (known == null ||
                    known.PendingGuildInvite.GuildId != index.PendingGuildInvite.GuildId ||
                    known.PendingGuildInvite.ExpiresAtTicks != index.PendingGuildInvite.ExpiresAtTicks)
                {
                    if (!string.IsNullOrEmpty(index.PendingGuildInvite.GuildId))
                        GuildInviteReceived?.Invoke(index.AccountId, index.PendingGuildInvite);
                }

                _lastIndexes[index.AccountId] = index;

                if (!string.IsNullOrEmpty(index.PartyId))
                    partyIds.Add(index.PartyId);

                if (!string.IsNullOrEmpty(index.GuildId))
                    guildIds.Add(index.GuildId);
            }

            foreach (string partyId in partyIds)
            {
                PartyRecord record = await _repository.LoadPartyAsync(partyId, CancellationToken.None);
                if (record != null)
                    PartyChanged?.Invoke(record);
            }

            foreach (string guildId in guildIds)
            {
                GuildRecord record = await _repository.LoadGuildAsync(guildId, CancellationToken.None);
                if (record != null)
                    GuildChanged?.Invoke(record);
            }
        }

        #endregion

        #region Presence

        private async Task AnnounceAsync(LocalPlayer local)
        {
            if (!await EnsureConnectedAsync())
                return;

            await SavePresenceAsync(local);

            try
            {
                await _repository.SaveNameAsync(Normalize(local.PlayerName), local.AccountId, CancellationToken.None);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Social] Failed to publish the name index for '{local.AccountId}': {e.Message}");
            }
        }

        private async Task HeartbeatAsync()
        {
            foreach (LocalPlayer local in new List<LocalPlayer>(_local.Values))
                await SavePresenceAsync(local);
        }

        private async Task SavePresenceAsync(LocalPlayer local)
        {
            if (local == null)
                return;

            try
            {
                PresenceRecord existing = await _repository.LoadPresenceAsync(local.AccountId, CancellationToken.None);
                PresenceRecord record = PresenceRecord.CreateOnline(
                    local.AccountId, local.PlayerName, _zoneId, DateTimeOffset.UtcNow);

                if (existing != null)
                    record.StorageVersion = existing.StorageVersion;

                await _repository.SavePresenceAsync(record, CancellationToken.None);
            }
            catch (StorageVersionConflictException)
            {
                // A racing heartbeat from another zone wins; the next tick refreshes ours.
            }
            catch (Exception e)
            {
                Debug.LogError($"[Social] Failed to write presence for '{local.AccountId}': {e.Message}");
            }
        }

        #endregion

        #region Helpers

        private async Task<SocialIndexRecord> LoadOrCreateIndexAsync(string accountId)
        {
            SocialIndexRecord index = await _repository.LoadIndexAsync(accountId, CancellationToken.None);
            return index ?? new SocialIndexRecord { AccountId = accountId };
        }

        private async Task<string> ResolveDisplayNameAsync(string accountId, SocialIndexRecord _)
        {
            if (_local.TryGetValue(accountId, out LocalPlayer local))
                return local.PlayerName;

            PresenceRecord presence = await _repository.LoadPresenceAsync(accountId, CancellationToken.None);
            return string.IsNullOrEmpty(presence?.PlayerName) ? accountId : presence.PlayerName;
        }

        private PartyMemberRecord NewMember(string accountId, SocialIndexRecord _, DateTimeOffset now)
            => new PartyMemberRecord
            {
                AccountId = accountId,
                PlayerName = _local.TryGetValue(accountId, out LocalPlayer local) ? local.PlayerName : accountId,
                ZoneId = _zoneId,
                JoinedAtTicks = now.UtcDateTime.Ticks,
            };

        private static string Normalize(string name) => (name ?? string.Empty).Trim().ToLowerInvariant();

        private async Task<PartyRule> RetryPartyAsync(Func<Task<PartyRule>> attempt)
        {
            for (int i = 0; i < _casRetries; i++)
            {
                try
                {
                    return await attempt();
                }
                catch (StorageVersionConflictException)
                {
                    await BackoffAsync(i);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Social] Party mutation failed: {e.Message}");
                    return PartyRule.BackendUnavailable;
                }
            }

            return PartyRule.Conflict;
        }

        private async Task<GuildRule> RetryPartyAsyncGuild(Func<Task<GuildRule>> attempt)
        {
            for (int i = 0; i < _casRetries; i++)
            {
                try
                {
                    return await attempt();
                }
                catch (StorageVersionConflictException)
                {
                    await BackoffAsync(i);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Social] Guild mutation failed: {e.Message}");
                    return GuildRule.BackendUnavailable;
                }
            }

            return GuildRule.Conflict;
        }

        private static Task BackoffAsync(int attempt)
            => Task.Delay(50 * (1 << attempt));

        private sealed class LocalPlayer
        {
            public string AccountId;
            public string PlayerName;
        }

        #endregion
    }
}
