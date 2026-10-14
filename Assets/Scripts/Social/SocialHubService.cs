using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FishNet.Managing;
using MMO.Auction;
using MMO.Inventory;
using MMO.Net;
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
    public sealed class SocialHubService : MonoBehaviour, ILocalRoster, ISocialBus
    {
        public static SocialHubService Instance { get; private set; }

        [SerializeField, Min(0.5f)] private float _syncInterval = 2f;
        [SerializeField, Min(1)] private int _casRetries = 5;
        [SerializeField, Min(0.5f)] private float _presenceInterval = 30f;

        /// <summary>How often to refresh the local players' friend-list online flags.</summary>
        [SerializeField, Min(1f)] private float _friendRefreshInterval = 10f;

        /// <summary>How long to wait before trying the backend again after a failed connect.</summary>
        [SerializeField, Min(1)] private float _connectRetrySeconds = 10f;

        [SerializeField] private string _zoneId = "local";

        /// <summary>Address the zone advertises as its raid instance host when it allocates one.</summary>
        [SerializeField] private string _instanceHostAddress = "127.0.0.1";

        /// <summary>Port the zone advertises as its raid instance host when it allocates one.</summary>
        [SerializeField] private ushort _instanceHostPort;

        /// <summary>Scene/build this zone serves; recorded on the raid instances it hosts (#57).</summary>
        [SerializeField] private string _zoneTemplateId = "default";

        /// <summary>Raid instances this zone is willing to host before it stops being a candidate.</summary>
        [SerializeField, Min(1)] private int _maxInstances = 100;

        /// <summary>Configuration for which zones have auction houses (central trading hubs).</summary>
        [SerializeField] private AuctionHouseZoneConfig _auctionHouseZoneConfig;

        /// <summary>Orchestrator control plane base URL (e.g., "http://127.0.0.1:8080").</summary>
        [SerializeField] private string _orchestratorUrl = "http://127.0.0.1:8080";

        /// <summary>Whether on-demand raid orchestration is enabled.</summary>
        [SerializeField] private bool _orchestratorEnabled = true;

        /// <summary>How long to wait for an orchestrated container to become healthy.</summary>
        [SerializeField, Min(1f)] private float _orchestratorHealthTimeoutSeconds = 90f;

        private readonly Dictionary<string, LocalPlayerHandle> _local = new Dictionary<string, LocalPlayerHandle>();
        private readonly Dictionary<string, SocialIndexRecord> _lastIndexes = new Dictionary<string, SocialIndexRecord>();

        /// <summary>Last raid instance a local member was told to transfer to, so a sync does not repeat it.</summary>
        private readonly Dictionary<string, string> _lastRaidTransfers = new Dictionary<string, string>();

        /// <summary>Last known RaidInstanceId per account, for detecting cleared pointers on completion.</summary>
        private readonly Dictionary<string, string> _lastRaidInstanceIds = new Dictionary<string, string>();

        /// <summary>Cached raid records for hosted instances (key = instanceId), so anchors survive Delete.</summary>
        private readonly Dictionary<string, RaidInstanceRecord> _hostedRaidCache = new Dictionary<string, RaidInstanceRecord>();

        /// <summary>Members already issued a return transfer this session (exactly-once per session).</summary>
        private readonly HashSet<string> _returnedMembers = new HashSet<string>();

        /// <summary>Local <see cref="PlayerGuild"/>s keyed by account, whose bags a bank mutation moves items into.</summary>
        private readonly Dictionary<string, PlayerGuild> _localGuilds = new Dictionary<string, PlayerGuild>();

        /// <summary>Server-wide single-writer authority for item instance ownership.</summary>
        private ItemOwnershipRegistry _ownershipRegistry;

        /// <summary>Access to the item ownership registry for production code.</summary>
        public ItemOwnershipRegistry OwnershipRegistry => _ownershipRegistry;

        private ISocialRepository _repository;
        private IRaidInstanceRepository _raidRepository;
        private IZoneRegistryRepository _zoneRepository;
        private NakamaRestClient _rest;
        private ISocialBus _socketBus;
        private ChatService _chat;
        private RaidInstanceService _raidService;
        private RaidOrchestrationService _orchestrationService;
        private IOrchestratorClient _orchestratorClient;
        private AuctionService _auctionService;
        private NetworkManager _manager;
        private bool _connected;
        private bool _subscribed;
        private Task<bool> _connectTask;
        private float _nextSyncTime;
        private float _nextPresenceTime;
        private float _nextFriendRefreshTime;
        private float _nextConnectAttemptTime;
        private float _nextZoneHeartbeatTime;

        /// <summary>Raised on the main thread for every inbound bus envelope (chat or notify).</summary>
        public event Action<SocialBusEnvelope> EnvelopeReceived;

        /// <summary>Chat routing, coded against this hub as its roster and bus.</summary>
        public ChatService Chat => _chat;

        /// <summary>The shared REST client the socket and the notify hook use, or null in tests.</summary>
        public NakamaRestClient Rest => _rest;

        /// <summary>The auction service for player-driven trading.</summary>
        public AuctionService Auction => _auctionService;

        bool ISocialBus.IsConnected => _socketBus != null && _socketBus.IsConnected;

        bool ISocialBus.TrySend(SocialBusEnvelope envelope)
            => _socketBus != null && _socketBus.TrySend(envelope);

        /// <summary>Raised with the authoritative party whenever any party this zone hosts changes.</summary>
        public event Action<PartyRecord> PartyChanged;

        /// <summary>Raised with the authoritative guild whenever a guild this zone hosts changes.</summary>
        public event Action<GuildRecord> GuildChanged;

        /// <summary>
        /// Raised with the authoritative bank whenever a guild bank this zone hosts changes (a local
        /// deposit/withdraw/edit, or the reconcile pass seeing a change made on another zone).
        /// </summary>
        public event Action<GuildBankRecord> BankChanged;

        /// <summary>Raised when a local member must move to another zone to join its party's raid instance.</summary>
        public event Action<string, RaidInstanceRecord> RaidTransferRequested;

        /// <summary>Raised when a local member must return to their origin zone after raid completion.</summary>
        public event Action<string, RaidReturnAnchor> RaidReturnRequested;

        /// <summary>
        /// Raised with the authoritative raid whenever a raid this zone hosts a member of changes, so
        /// <see cref="PlayerRaid"/> can repaint the replicated roster (mirrors <see cref="PartyChanged"/>).
        /// </summary>
        public event Action<RaidInstanceRecord> RaidChanged;

        /// <summary>Raised for the one invited account; delivery is a <c>TargetRpc</c> underneath.</summary>
        public event Action<string, PendingPartyInvite> PartyInviteReceived;

        /// <summary>Raised for the one invited account's guild invite.</summary>
        public event Action<string, PendingGuildInvite> GuildInviteReceived;

        /// <summary>Raised when a new auction listing is created by a local player.</summary>
        public event Action<string, AuctionListingRecord> AuctionListingCreated;

        /// <summary>Raised when an auction listing is sold (buyer and seller notified).</summary>
        public event Action<string, string, AuctionListingRecord, AuctionTransactionRecord> AuctionListingSold;

        /// <summary>Raised when an auction listing is cancelled by the seller.</summary>
        public event Action<string, AuctionListingRecord> AuctionListingCancelled;

        /// <summary>Raised when a purchase is completed and the buyer receives the item.</summary>
        public event Action<string, AuctionTransactionRecord> AuctionPurchaseCompleted;

        /// <summary>Raised when a new buy order is created by a local player.</summary>
        public event Action<string, AuctionBuyOrderRecord> AuctionBuyOrderCreated;

        /// <summary>Raised when a buy order is fulfilled (seller and buyer notified).</summary>
        public event Action<string, string, AuctionBuyOrderRecord, AuctionTransactionRecord> AuctionBuyOrderFulfilled;

        /// <summary>Raised when a buy order is cancelled by the buyer.</summary>
        public event Action<string, AuctionBuyOrderRecord> AuctionBuyOrderCancelled;

        /// <summary>The zone label stamped into presence so a member's host zone can be resolved.</summary>
        public string ZoneId
        {
            get => _zoneId;
            set => _zoneId = value ?? "local";
        }

        /// <summary>Whether the backend is currently reachable (fakes are considered always up).</summary>
        public bool IsConnected => _connected;

        /// <summary>Whether this zone has an auction house (is a central trading hub).</summary>
        public bool HasAuctionHouse => _auctionHouseZoneConfig != null && _auctionHouseZoneConfig.HasAuctionHouse(_zoneId);

        private void Awake()
        {
            Instance = this;
            EnsureChat();
            if (_repository == null)
            {
                _rest = new NakamaRestClient(new BackendSettings());
                NakamaSocialRepository nakamaRepo = new NakamaSocialRepository(_rest);
                Configure(
                    nakamaRepo,
                    new NakamaRaidInstanceRepository(_rest),
                    new NakamaZoneRegistryRepository(_rest));
            }
        }

        private void OnDestroy()
        {
            if (_socketBus != null)
                _socketBus.EnvelopeReceived -= OnEnvelope;

            if (Instance == this)
                Instance = null;
        }

        /// <summary>
        /// Points the service at a repository. Production reaches the Nakama-backed default in
        /// <see cref="Awake"/>; tests inject an in-memory fake here, which needs no connection.
        /// </summary>
        public void Configure(
            ISocialRepository repository,
            IRaidInstanceRepository raidRepository = null,
            IZoneRegistryRepository zoneRepository = null)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _raidRepository = raidRepository;
            _zoneRepository = zoneRepository;

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

            if (_raidRepository == null && _rest != null)
                _raidRepository = new NakamaRaidInstanceRepository(_rest);

            if (_zoneRepository == null && _rest != null)
                _zoneRepository = new NakamaZoneRegistryRepository(_rest);

            // A deployment with no zone registry (a local single-zone demo, or a pure social fake
            // test) falls back to registering this zone alone, so allocation behaves as #51 did.
            if (_zoneRepository == null && _raidRepository != null)
                _zoneRepository = new SingleZoneRegistryRepository(
                    _zoneId, _instanceHostAddress, _instanceHostPort, _zoneTemplateId, _maxInstances);

            // With neither a raid repository nor a REST client (a pure social fake) raids are off;
            // the hub surfaces BackendUnavailable rather than throwing on a null endpoint.
            _raidService = _raidRepository != null && _zoneRepository != null
                ? new RaidInstanceService(_repository, _raidRepository, _zoneRepository, _casRetries)
                : null;

            // On-demand raid orchestration service (issue #61).
            if (_orchestratorEnabled && _raidService != null)
            {
                _orchestratorClient = new HttpOrchestratorClient(_orchestratorUrl, _orchestratorEnabled);
                _orchestrationService = new RaidOrchestrationService(
                    _orchestratorClient,
                    _zoneRepository,
                    async (party, ct) => await _raidService.AllocateAsync(party.LeaderId),
                    _orchestratorHealthTimeoutSeconds,
                    _maxInstances);
            }
            else
            {
                _orchestratorClient = null;
                _orchestrationService = null;
            }

            // Auction service: requires REST client for RPC calls and repository for storage.
            _auctionService = _rest != null
                ? new AuctionService(
                    new NakamaAuctionRepository(_rest),
                    _rest,
                    this,
                    _casRetries)
                : null;

            // Item ownership registry: single-writer authority for dupe protection.
            _ownershipRegistry = new ItemOwnershipRegistry();
        }

        private void EnsureChat()
        {
            if (_chat == null)
                _chat = new ChatService(this, this);
        }

        #region Realtime backbone

        /// <summary>
        /// Wires the zone server's Nakama socket. Every inbound envelope is routed here: chat
        /// through <see cref="ChatService"/>, friend/party/guild hints as a cue to re-read and push
        /// snapshots. Called by <c>Net.NetworkManagerFactory</c>.
        /// </summary>
        public void AttachSocket(NakamaSocialSocket socket)
        {
            if (_socketBus != null)
                _socketBus.EnvelopeReceived -= OnEnvelope;

            _socketBus = socket;

            if (_socketBus != null)
            {
                _socketBus.EnvelopeReceived += OnEnvelope;
                _ = AnnounceLocalPresenceAsync();
            }

            EnsureChat();
        }

        /// <summary>Attaches the shared REST client the notify hook uses for <c>social_notify</c>.</summary>
        public void AttachRestClient(NakamaRestClient rest)
        {
            if (rest != null)
                _rest = rest;
        }

        /// <summary>Called by <see cref="PlayerChat"/> (server): routes this player's chat deliveries.</summary>
        public void AttachChat(string accountId, Action<string> deliverChat)
        {
            if (_local.TryGetValue(accountId ?? string.Empty, out LocalPlayerHandle handle))
                handle.DeliverChat = deliverChat;
        }

        /// <summary>Called by <see cref="PlayerChat"/> (server) when the object despawns.</summary>
        public void DetachChat(string accountId)
        {
            if (_local.TryGetValue(accountId ?? string.Empty, out LocalPlayerHandle handle))
                handle.DeliverChat = null;
        }

        /// <summary>Called by <see cref="PlayerFriends"/> (server): routes this player's friend pushes.</summary>
        public void AttachFriends(string accountId, Action<string> deliverSnapshot, Action<int> notifyEvent)
        {
            if (_local.TryGetValue(accountId ?? string.Empty, out LocalPlayerHandle handle))
            {
                handle.DeliverFriends = deliverSnapshot;
                handle.NotifyFriendEvent = notifyEvent;
            }
        }

        /// <summary>Called by <see cref="PlayerFriends"/> (server) when the object despawns.</summary>
        public void DetachFriends(string accountId)
        {
            if (_local.TryGetValue(accountId ?? string.Empty, out LocalPlayerHandle handle))
            {
                handle.DeliverFriends = null;
                handle.NotifyFriendEvent = null;
            }
        }

        /// <summary>Called by <see cref="PlayerGuild"/> (server): registers the local guild view for bank work.</summary>
        public void AttachGuildView(string accountId, PlayerGuild guild)
        {
            if (!string.IsNullOrEmpty(accountId) && guild != null)
                _localGuilds[accountId] = guild;
        }

        /// <summary>Called by <see cref="PlayerGuild"/> (server) when the object despawns.</summary>
        public void DetachGuildView(string accountId)
        {
            if (!string.IsNullOrEmpty(accountId))
                _localGuilds.Remove(accountId);
        }

        private readonly Dictionary<string, PlayerWallet> _localWallets = new Dictionary<string, PlayerWallet>();

        /// <summary>Called by <see cref="PlayerWallet"/> (server): registers the local wallet for auction access.</summary>
        public void AttachWalletView(string accountId, PlayerWallet wallet)
        {
            if (!string.IsNullOrEmpty(accountId) && wallet != null)
                _localWallets[accountId] = wallet;
        }

        /// <summary>Called by <see cref="PlayerWallet"/> (server) when the object despawns.</summary>
        public void DetachWalletView(string accountId)
        {
            if (!string.IsNullOrEmpty(accountId))
                _localWallets.Remove(accountId);
        }

        /// <summary>Gets the wallet for a local player (for gold checks).</summary>
        public bool TryGetWallet(string accountId, out PlayerWallet wallet)
        {
            return _localWallets.TryGetValue(accountId, out wallet);
        }

        private void OnEnvelope(SocialBusEnvelope envelope)
        {
            if (envelope == null)
                return;

            EnvelopeReceived?.Invoke(envelope);

            switch (envelope.t)
            {
                case "chat":
                    _chat?.RouteInbound(envelope);
                    break;

                case "friend":
                case "party":
                case "guild":
                    if (envelope.accounts == null)
                        break;

                    foreach (string accountId in envelope.accounts)
                    {
                        if (_local.ContainsKey(accountId ?? string.Empty))
                            _ = PushFriendsAsync(accountId);
                    }
                    break;

                // A peer zone allocated or completed a raid: re-run the reconcile pass early so a
                // hosted member begins (or stops) its transfer before the next 2 s tick.
                case SocialNotification.WireType:
                    _ = OnSocialNotification(SocialNotification.FromEnvelope(envelope));
                    break;
            }
        }

        private async Task AnnounceLocalPresenceAsync()
        {
            foreach (LocalPlayerHandle local in new List<LocalPlayerHandle>(_local.Values))
                await SavePresenceAsync(local);
        }

        #endregion

        #region ILocalRoster

        /// <summary>The zone label stamped into presence so a member's host zone can be resolved.</summary>
        string ILocalRoster.LocalZoneId => _zoneId;

        IReadOnlyList<LocalPlayerHandle> ILocalRoster.Local
        {
            get
            {
                var list = new List<LocalPlayerHandle>(_local.Count);
                foreach (LocalPlayerHandle handle in _local.Values)
                    list.Add(handle);

                return list;
            }
        }

        string ILocalRoster.PartyOf(string accountId)
            => _local.TryGetValue(accountId ?? string.Empty, out LocalPlayerHandle handle) ? handle.PartyId : null;

        string ILocalRoster.NameOf(string accountId)
            => _local.TryGetValue(accountId ?? string.Empty, out LocalPlayerHandle handle)
                ? handle.PlayerName
                : accountId;

        #endregion

        #region Registration + lifecycle

        private void Start()
        {
            _manager = GetComponentInParent<NetworkManager>();
            _nextSyncTime = Time.time + _syncInterval;
            _nextPresenceTime = Time.time + _presenceInterval;
            _nextFriendRefreshTime = Time.time + _friendRefreshInterval;
            // Register with the zone registry on the first connected tick, not one interval later,
            // so peers can select this zone as soon as it is up (#57).
            _nextZoneHeartbeatTime = 0f;
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

            var local = new LocalPlayerHandle
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
            {
                _local.Remove(accountId);
                _lastRaidTransfers.Remove(accountId);
            }
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

            // Advertise this zone to the registry even before any player joins, so a dedicated
            // instance host with no residents can still be selected (#57).
            if (_zoneRepository != null && Time.time >= _nextZoneHeartbeatTime)
            {
                _nextZoneHeartbeatTime = Time.time + _presenceInterval;
                _ = HeartbeatZoneAsync();
            }

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

            // Refresh friend-list online flags on a low cadence so join/transfer/login all see
            // fresh states without hammering the database.
            if (Time.time >= _nextFriendRefreshTime)
            {
                _nextFriendRefreshTime = Time.time + _friendRefreshInterval;
                _ = RefreshLocalFriendsAsync();
            }
        }

        private void OnServerConnectionState(FishNet.Transporting.ServerConnectionStateArgs args)
        {
            if (args.ConnectionState != FishNet.Transporting.LocalConnectionState.Started)
                return;

            _ = EnsureConnectedAsync();
            EnsureSocket();
        }

        /// <summary>
        /// Brings up the realtime bus when the server starts. The scene that ships is baked from
        /// <c>Net.NetworkManagerFactory</c>, but a socket's transport and session are runtime-only,
        /// so a baked component would be inert; this materialises and configures one on the zone
        /// server. A no-op when the factory (or a prior call) already attached one, and when the
        /// repository is a test fake with no backend to talk to.
        /// </summary>
        private void EnsureSocket()
        {
            if (_socketBus != null || _rest == null)
                return;

            NakamaSocialSocket socket = GetComponent<NakamaSocialSocket>();
            if (socket == null)
                socket = gameObject.AddComponent<NakamaSocialSocket>();

            socket.Configure(new WebSocketTransport(), _rest);
            AttachSocket(socket);
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
                {
                    await TeardownRaidAsync(party);
                    await _repository.DeletePartyAsync(party.PartyId, CancellationToken.None);
                }
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
                {
                    await TeardownRaidAsync(party);
                    await _repository.DeletePartyAsync(party.PartyId, CancellationToken.None);
                }
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

        #region Guild bank intents

        /// <summary>
        /// Moves the item at the bag cell into the guild bank's tab. One CAS read-modify-write on
        /// the bank object: the bag change happens only after the write wins, so the bag can never
        /// lose an item the bank did not gain.
        /// </summary>
        public async Task<GuildBankResult> DepositAsync(string accountId, int tabIndex, int bagX, int bagY)
        {
            if (!await EnsureConnectedAsync())
                return new GuildBankResult(GuildBankRule.BackendUnavailable);

            if (!_localGuilds.TryGetValue(accountId ?? string.Empty, out PlayerGuild player) || player == null)
                return new GuildBankResult(GuildBankRule.NoGuild);

            PlayerInventory bag = player.Inventory;
            if (bag == null)
                return new GuildBankResult(GuildBankRule.NoGuild);

            string guildId = null;
            DateTimeOffset now = DateTimeOffset.UtcNow;
            GuildBankResult result = await RetryBankAsync(async () =>
            {
                SocialIndexRecord index = await _repository.LoadIndexAsync(accountId, CancellationToken.None);
                if (index == null || string.IsNullOrEmpty(index.GuildId))
                    return new GuildBankResult(GuildBankRule.NoGuild);

                guildId = index.GuildId;
                GuildRecord guild = await _repository.LoadGuildAsync(guildId, CancellationToken.None);
                GuildBankRecord bank = await LoadOrCreateBankAsync(guildId);

                // Resolve through the cell against the live SyncList, never a client-supplied index:
                // the bag may have shifted between the click and this call.
                int itemIndex = new InventoryGrid(PlayerInventory.GridWidth, PlayerInventory.GridHeight)
                    .FindAt(bag.Items, bagX, bagY);
                if (itemIndex < 0)
                    return new GuildBankResult(GuildBankRule.InvalidItem);

                ItemInstance item = bag.Items[itemIndex];
                GuildRank rank = GuildRules.RankOf(GuildRules.FindMember(guild, accountId));
                GuildBankRule rule = GuildBankRules.CanDeposit(guild, bank, accountId, tabIndex, item, now);
                if (rule != GuildBankRule.Ok)
                    return new GuildBankResult(rule);

                if (!GuildBankRules.Deposit(bank, tabIndex, item, rank, now))
                    return new GuildBankResult(GuildBankRule.TabFull);

                await _repository.SaveGuildBankAsync(bank, CancellationToken.None);
                await AppendLedgerAsync(guildId, accountId, GuildBankLedgerAction.Deposit, tabIndex, item, bagX, bagY);
                return new GuildBankResult(GuildBankRule.Ok, item);
            });

            if (result.Rule == GuildBankRule.Ok)
            {
                // The bank is durable. Only now may the bag lose the item, so a failed write can
                // never strand an item outside both containers.
                bag.RemoveItemAt(bagX, bagY);
                await PushBankAsync(guildId);
            }

            return result;
        }

        /// <summary>
        /// Moves the item at the tab cell into the requesting member's bag. Bag space is reserved
        /// before the CAS; if the add still loses a race, the item is CAS-ed back into the bank.
        /// </summary>
        public async Task<GuildBankResult> WithdrawAsync(string accountId, int tabIndex, int gridX, int gridY, int stackCount = -1)
        {
            if (!await EnsureConnectedAsync())
                return new GuildBankResult(GuildBankRule.BackendUnavailable);

            if (!_localGuilds.TryGetValue(accountId ?? string.Empty, out PlayerGuild player) || player == null)
                return new GuildBankResult(GuildBankRule.NoGuild);

            PlayerInventory bag = player.Inventory;
            if (bag == null)
                return new GuildBankResult(GuildBankRule.NoGuild);

            DateTimeOffset now = DateTimeOffset.UtcNow;
            string guildId = null;
            int cellX = 0;
            int cellY = 0;
            ItemInstance withdrawn = default;

            GuildBankResult result = await RetryBankAsync(async () =>
            {
                SocialIndexRecord index = await _repository.LoadIndexAsync(accountId, CancellationToken.None);
                if (index == null || string.IsNullOrEmpty(index.GuildId))
                    return new GuildBankResult(GuildBankRule.NoGuild);

                guildId = index.GuildId;
                GuildRecord guild = await _repository.LoadGuildAsync(guildId, CancellationToken.None);
                GuildBankRecord bank = await LoadOrCreateBankAsync(guildId);

                GuildBankRule rule = GuildBankRules.CanWithdraw(guild, bank, accountId, tabIndex, gridX, gridY, now);
                if (rule != GuildBankRule.Ok)
                    return new GuildBankResult(rule);

                // Reserve bag space *before* the CAS: a full bag must refuse without touching the bank.
                GuildBankTab tab = GuildBankRules.FindTab(bank, tabIndex);
                ItemInstance candidate = GuildBankRules.ItemAt(tab, gridX, gridY);
                var bagGrid = new InventoryGrid(PlayerInventory.GridWidth, PlayerInventory.GridHeight);
                if (candidate.IsEmpty ||
                    !bagGrid.TryFindFreeSpace(bag.Items, candidate.Width, candidate.Height, out int freeX, out int freeY))
                    return new GuildBankResult(GuildBankRule.BagFull);

                if (!GuildBankRules.Withdraw(guild, bank, accountId, tabIndex, gridX, gridY, now, out withdrawn))
                    return new GuildBankResult(GuildBankRule.NothingToWithdraw);

                cellX = freeX;
                cellY = freeY;
                await _repository.SaveGuildBankAsync(bank, CancellationToken.None);
                await AppendLedgerAsync(guildId, accountId, GuildBankLedgerAction.Withdraw, tabIndex, withdrawn, gridX, gridY);
                return new GuildBankResult(GuildBankRule.Ok, withdrawn);
            });

            if (result.Rule == GuildBankRule.Ok)
            {
                if (!bag.TryAddExistingItem(withdrawn, cellX, cellY))
                {
                    // The bag shifted between the space check and now (another request landed on the
                    // same player): put the item back so nothing is lost, then tell the player.
                    await ReturnToBankAsync(guildId, tabIndex, withdrawn);
                    return new GuildBankResult(GuildBankRule.BagFull);
                }

                await PushBankAsync(guildId);
            }

            return result;
        }

        /// <summary>Leader-only: replaces a rank's deposit/withdraw permissions on the bank.</summary>
        public async Task<GuildBankRule> SetBankPermissionAsync(
            string accountId, GuildRank rank, bool allowDeposit, bool allowWithdraw, int withdrawLimit,
            long withdrawValueLimit = GuildBankRules.UnlimitedValue, long depositValueLimit = GuildBankRules.UnlimitedValue)
        {
            if (!await EnsureConnectedAsync())
                return GuildBankRule.BackendUnavailable;

            if (!GuildBankRules.IsValidPermission(allowDeposit, allowWithdraw, withdrawLimit, withdrawValueLimit, depositValueLimit))
                return GuildBankRule.InvalidPermission;

            string guildId = null;
            GuildBankRule result = await RetryBankPermissionAsync(async () =>
            {
                SocialIndexRecord index = await _repository.LoadIndexAsync(accountId, CancellationToken.None);
                if (index == null || string.IsNullOrEmpty(index.GuildId))
                    return GuildBankRule.NoGuild;

                guildId = index.GuildId;
                GuildRecord guild = await _repository.LoadGuildAsync(guildId, CancellationToken.None);
                GuildBankRule rule = GuildBankRules.CanEditPermissions(guild, accountId);
                if (rule != GuildBankRule.Ok)
                    return rule;

                GuildBankRecord bank = await LoadOrCreateBankAsync(guildId);
                GuildBankRules.SetPermission(bank, rank, allowDeposit, allowWithdraw, withdrawLimit, withdrawValueLimit, depositValueLimit);
                await _repository.SaveGuildBankAsync(bank, CancellationToken.None);
                return GuildBankRule.Ok;
            });

            if (result == GuildBankRule.Ok)
                await PushBankAsync(guildId);

            return result;
        }

        private async Task<GuildBankRecord> LoadOrCreateBankAsync(string guildId)
        {
            if (string.IsNullOrEmpty(guildId))
                return null;

            GuildBankRecord bank = await _repository.LoadGuildBankAsync(guildId, CancellationToken.None);
            if (bank == null)
                bank = GuildBankRules.CreateDefault(guildId);
            else
                GuildBankRules.EnsureDefaults(bank);

            return bank;
        }

        /// <summary>Reloads the authoritative bank and pushes it to every local member of the guild.</summary>
        private async Task PushBankAsync(string guildId)
        {
            if (string.IsNullOrEmpty(guildId))
                return;

            GuildBankRecord bank = await _repository.LoadGuildBankAsync(guildId, CancellationToken.None);
            if (bank != null)
                BankChanged?.Invoke(bank);
        }

        /// <summary>Puts a withdrawn item back into the bank after a bag race lost; best effort.</summary>
        private async Task ReturnToBankAsync(string guildId, int tabIndex, ItemInstance item)
        {
            if (string.IsNullOrEmpty(guildId) || item.IsEmpty)
                return;

            await RetryBankAsync(async () =>
            {
                GuildBankRecord bank = await LoadOrCreateBankAsync(guildId);
                if (!GuildBankRules.Deposit(bank, tabIndex, item))
                    return new GuildBankResult(GuildBankRule.TabFull);

                await _repository.SaveGuildBankAsync(bank, CancellationToken.None);
                return new GuildBankResult(GuildBankRule.Ok);
            });

            await PushBankAsync(guildId);
        }

        private async Task AppendLedgerAsync(string guildId, string actorAccountId, GuildBankLedgerAction action, int tabIndex, ItemInstance item, int gridX, int gridY)
        {
            if (string.IsNullOrEmpty(guildId) || item.IsEmpty)
                return;

            try
            {
                GuildBankLedgerRecord ledger = await _repository.LoadGuildBankLedgerAsync(guildId, CancellationToken.None);
                if (ledger == null)
                {
                    ledger = new GuildBankLedgerRecord
                    {
                        GuildId = guildId,
                        Entries = new List<GuildBankLedgerEntry>(),
                    };
                }
                else if (ledger.Entries == null)
                {
                    ledger.Entries = new List<GuildBankLedgerEntry>();
                }

                ItemDefinition def = ItemDefinitionRegistry.Resolve(item.ItemName);
                long valuePerUnit = def != null ? def.Value : 0;
                int count = item.StackCount < 1 ? 1 : item.StackCount;

                ledger.Entries.Add(new GuildBankLedgerEntry
                {
                    GuildId = guildId,
                    ActorAccountId = actorAccountId,
                    Action = action,
                    TabIndex = tabIndex,
                    ItemName = item.ItemName,
                    Width = item.Width,
                    Height = item.Height,
                    GridX = gridX,
                    GridY = gridY,
                    StackCount = count,
                    ValuePerUnit = valuePerUnit,
                    TotalValue = valuePerUnit * count,
                    TimestampTicks = DateTimeOffset.UtcNow.UtcTicks,
                    AffectedCells = new List<int>(),
                });

                await _repository.SaveGuildBankLedgerAsync(ledger, CancellationToken.None);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Social] Guild bank ledger append failed: {e.Message}");
            }
        }

        private async Task<GuildBankResult> RetryBankAsync(Func<Task<GuildBankResult>> attempt)
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
                    Debug.LogError($"[Social] Guild bank mutation failed: {e.Message}");
                    return new GuildBankResult(GuildBankRule.BackendUnavailable);
                }
            }

            return new GuildBankResult(GuildBankRule.Conflict);
        }

        private async Task<GuildBankRule> RetryBankPermissionAsync(Func<Task<GuildBankRule>> attempt)
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
                    Debug.LogError($"[Social] Guild bank permission edit failed: {e.Message}");
                    return GuildBankRule.BackendUnavailable;
                }
            }

            return GuildBankRule.Conflict;
        }

        #endregion

        #region Friend intents

        /// <summary>Sends a friend request from <paramref name="requesterId"/> to the named player.</summary>
        public async Task<FriendRule> RequestFriendAsync(string requesterId, string targetName)
        {
            if (string.IsNullOrEmpty(requesterId))
                return FriendRule.NotFound;

            if (!await EnsureConnectedAsync())
                return FriendRule.BackendUnavailable;

            string targetId = await _repository.ResolveNameAsync(Normalize(targetName), CancellationToken.None);
            if (string.IsNullOrEmpty(targetId))
                return FriendRule.NotFound;

            FriendRule result = await RetryFriendAsync(async () =>
            {
                FriendListRecord mine = await LoadFriendsAsync(requesterId);
                FriendListRecord theirs = await LoadFriendsAsync(targetId);

                FriendRule rule = FriendRules.CanRequest(requesterId, targetId, mine, theirs);
                if (rule != FriendRule.Ok)
                    return rule;

                long now = NowTicks();

                // Target first: if the second write dies, the dangling PendingIn is derived state
                // that the reconcile pass or the accept path repairs — never a half-friend pair.
                FriendRules.ApplyRequest(theirs, requesterId, ((ILocalRoster)this).NameOf(requesterId), now);
                await _repository.SaveFriendsAsync(theirs, CancellationToken.None);

                FriendRules.ApplyPendingOut(mine, targetId, now);
                await _repository.SaveFriendsAsync(mine, CancellationToken.None);
                return FriendRule.Ok;
            });

            if (result == FriendRule.Ok)
            {
                await SafeNotifyAsync("friend", string.Empty, requesterId, targetId);
                await PushFriendsAsync(requesterId);
                await PushFriendsAsync(targetId);
                NotifyEvent(targetId, FriendEvent.RequestReceived);
            }

            return result;
        }

        /// <summary>Accepts the pending friend request from <paramref name="fromAccountId"/>.</summary>
        public async Task<FriendRule> AcceptFriendAsync(string accountId, string fromAccountId)
        {
            if (!await EnsureConnectedAsync())
                return FriendRule.BackendUnavailable;

            FriendRule result = await RetryFriendAsync(async () =>
            {
                FriendListRecord mine = await LoadFriendsAsync(accountId);
                FriendRule rule = FriendRules.CanAccept(mine, fromAccountId);
                if (rule != FriendRule.Ok)
                    return rule;

                long now = NowTicks();
                FriendRules.ApplyAccept(mine, fromAccountId, now);
                await _repository.SaveFriendsAsync(mine, CancellationToken.None);

                // Materialize the other side immediately; if this best-effort write fails, the
                // reconcile pass sees NeedsRepair and finishes it.
                FriendListRecord theirs = await LoadFriendsAsync(fromAccountId);
                if (theirs != null && FriendRules.NeedsRepair(mine, theirs, accountId, fromAccountId))
                {
                    FriendRules.ApplyAccept(theirs, accountId, now);
                    await _repository.SaveFriendsAsync(theirs, CancellationToken.None);
                }

                return FriendRule.Ok;
            });

            if (result == FriendRule.Ok)
            {
                await SafeNotifyAsync("friend", string.Empty, accountId, fromAccountId);
                await PushFriendsAsync(accountId);
                await PushFriendsAsync(fromAccountId);
                NotifyEvent(fromAccountId, FriendEvent.Accepted);
            }

            return result;
        }

        /// <summary>Declines the pending friend request from <paramref name="fromAccountId"/>.</summary>
        public async Task<FriendRule> DeclineFriendAsync(string accountId, string fromAccountId)
        {
            if (!await EnsureConnectedAsync())
                return FriendRule.BackendUnavailable;

            FriendRule result = await RetryFriendAsync(async () =>
            {
                FriendListRecord mine = await LoadFriendsAsync(accountId);
                FriendRule rule = FriendRules.CanDecline(mine, fromAccountId);
                if (rule != FriendRule.Ok)
                    return rule;

                FriendRules.Remove(mine, fromAccountId);
                await _repository.SaveFriendsAsync(mine, CancellationToken.None);

                FriendListRecord theirs = await LoadFriendsAsync(fromAccountId);
                FriendRules.Remove(theirs, accountId);
                await _repository.SaveFriendsAsync(theirs, CancellationToken.None);
                return FriendRule.Ok;
            });

            if (result == FriendRule.Ok)
            {
                await SafeNotifyAsync("friend", string.Empty, accountId, fromAccountId);
                await PushFriendsAsync(accountId);
                await PushFriendsAsync(fromAccountId);
                NotifyEvent(fromAccountId, FriendEvent.Declined);
            }

            return result;
        }

        /// <summary>Removes an accepted friend from both sides.</summary>
        public async Task<FriendRule> RemoveFriendAsync(string accountId, string targetId)
        {
            if (!await EnsureConnectedAsync())
                return FriendRule.BackendUnavailable;

            FriendRule result = await RetryFriendAsync(async () =>
            {
                FriendListRecord mine = await LoadFriendsAsync(accountId);
                FriendRule rule = FriendRules.CanRemove(mine, targetId);
                if (rule != FriendRule.Ok)
                    return rule;

                FriendRules.Remove(mine, targetId);
                await _repository.SaveFriendsAsync(mine, CancellationToken.None);

                FriendListRecord theirs = await LoadFriendsAsync(targetId);
                FriendRules.Remove(theirs, accountId);
                await _repository.SaveFriendsAsync(theirs, CancellationToken.None);
                return FriendRule.Ok;
            });

            if (result == FriendRule.Ok)
            {
                await SafeNotifyAsync("friend", string.Empty, accountId, targetId);
                await PushFriendsAsync(accountId);
                await PushFriendsAsync(targetId);
                NotifyEvent(targetId, FriendEvent.Removed);
            }

            return result;
        }

        /// <summary>Pushes a fresh snapshot to a local player's friend list.</summary>
        public async Task PushFriendsAsync(string accountId)
        {
            if (_repository == null || string.IsNullOrEmpty(accountId))
                return;

            if (!_local.TryGetValue(accountId, out LocalPlayerHandle handle) || handle.DeliverFriends == null)
                return;

            FriendListRecord record = await LoadFriendsAsync(accountId);
            var ids = new List<string>();
            if (record?.Entries != null)
            {
                foreach (FriendEntry entry in record.Entries)
                    ids.Add(entry.AccountId);
            }

            var online = new HashSet<string>();
            if (ids.Count > 0)
            {
                List<PresenceRecord> presences = await _repository.LoadPresencesAsync(ids, CancellationToken.None);
                DateTimeOffset now = DateTimeOffset.UtcNow;
                foreach (PresenceRecord presence in presences)
                {
                    if (presence != null && presence.IsHeartbeatFresh(now))
                        online.Add(presence.AccountId);
                }
            }

            var snapshot = new FriendListSnapshot();
            if (record?.Entries != null)
            {
                foreach (FriendEntry entry in record.Entries)
                {
                    snapshot.Friends.Add(new FriendView
                    {
                        AccountId = entry.AccountId,
                        PlayerName = string.IsNullOrEmpty(entry.PlayerName) ? entry.AccountId : entry.PlayerName,
                        State = entry.State,
                        Online = online.Contains(entry.AccountId),
                    });
                }
            }

            handle.DeliverFriends(JsonUtility.ToJson(snapshot));
        }

        private async Task RefreshLocalFriendsAsync()
        {
            if (_repository == null || _local.Count == 0)
                return;

            foreach (string accountId in new List<string>(_local.Keys))
                await PushFriendsAsync(accountId);
        }

        private async Task RepairFriendsAsync()
        {
            foreach (string accountId in new List<string>(_local.Keys))
            {
                FriendListRecord mine = await LoadFriendsAsync(accountId);
                if (mine?.Entries == null)
                    continue;

                foreach (FriendEntry entry in new List<FriendEntry>(mine.Entries))
                {
                    if (entry.State != FriendState.Accepted)
                        continue;

                    FriendListRecord theirs = await LoadFriendsAsync(entry.AccountId);
                    if (!FriendRules.NeedsRepair(mine, theirs, accountId, entry.AccountId))
                        continue;

                    try
                    {
                        FriendRules.ApplyAccept(theirs, accountId, NowTicks());
                        await _repository.SaveFriendsAsync(theirs, CancellationToken.None);
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[Social] friend repair failed for '{entry.AccountId}': {e.Message}");
                    }
                }
            }
        }

        private async Task<FriendListRecord> LoadFriendsAsync(string accountId)
            => await _repository.LoadFriendsAsync(accountId, CancellationToken.None)
               ?? new FriendListRecord { AccountId = accountId };

        private void NotifyEvent(string accountId, FriendEvent evt)
        {
            if (_local.TryGetValue(accountId ?? string.Empty, out LocalPlayerHandle handle))
                handle.NotifyFriendEvent?.Invoke((int)evt);
        }

        private async Task SafeNotifyAsync(string type, string target, params string[] accounts)
        {
            if (_rest == null || !_rest.IsConnected)
                return;

            try
            {
                await _rest.RpcAsync("social_notify", JsonUtility.ToJson(new NotifyPayload
                {
                    t = type,
                    target = target,
                    accounts = accounts,
                }), CancellationToken.None);
            }
            catch (Exception e)
            {
                // Best-effort by design: the 2 s reconcile loop recovers a lost notify, so this
                // never fails or blocks a write.
                Debug.LogWarning($"[Social] social_notify ({type}) failed: {e.Message}");
            }
        }

        private static long NowTicks() => DateTimeOffset.UtcNow.UtcDateTime.Ticks;

        private async Task<FriendRule> RetryFriendAsync(Func<Task<FriendRule>> attempt)
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
                    Debug.LogError($"[Social] Friend mutation failed: {e.Message}");
                    return FriendRule.BackendUnavailable;
                }
            }

            return FriendRule.Conflict;
        }

        [Serializable]
        private sealed class NotifyPayload
        {
            public string t;
            public string target;
            public string[] accounts;
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

                // Keep the local roster's party pointer in step so party chat can route.
                if (_local.TryGetValue(index.AccountId, out LocalPlayerHandle localHandle))
                    localHandle.PartyId = index.PartyId;

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

                // Detect raid completion: RaidInstanceId went from set → empty while members are local.
                // This zone is the host if it has members in this party and we cached the raid record.
                if (record != null && _raidRepository != null)
                {
                    _lastRaidInstanceIds.TryGetValue(record.PartyId, out string lastInstanceId);
                    string currentInstanceId = record.RaidInstanceId;

                    if (!string.IsNullOrEmpty(lastInstanceId) && string.IsNullOrEmpty(currentInstanceId))
                    {
                        // Raid completed for this party — drive return for each local member
                        if (record.Members != null)
                        {
                            foreach (PartyMemberRecord member in record.Members)
                            {
                                if (!string.IsNullOrEmpty(member.AccountId) && _local.ContainsKey(member.AccountId))
                                {
                                    await TryTransferHomeAsync(member.AccountId, lastInstanceId);
                                }
                            }
                        }
                    }

                    // Update the last known instance id
                    if (!string.IsNullOrEmpty(currentInstanceId))
                        _lastRaidInstanceIds[record.PartyId] = currentInstanceId;
                    else
                        _lastRaidInstanceIds.Remove(record.PartyId);
                }
            }

            foreach (string guildId in guildIds)
            {
                GuildRecord record = await _repository.LoadGuildAsync(guildId, CancellationToken.None);
                if (record != null)
                    GuildChanged?.Invoke(record);

                // A bank that was never written opens with the default tabs and rank rules, so a
                // brand-new guild still has something to render on every zone hosting its members.
                GuildBankRecord bank = await _repository.LoadGuildBankAsync(guildId, CancellationToken.None);
                if (bank == null)
                    bank = GuildBankRules.CreateDefault(guildId);
                else
                    GuildBankRules.EnsureDefaults(bank);

                BankChanged?.Invoke(bank);
            }

            // Repair any half-accepted friendship whose mirror write or notify was lost.
            await RepairFriendsAsync();

            // Raid handoff: a party whose raid instance is hosted on another zone must move its
            // local members there. The pointer is authoritative; only the zone already holding a
            // member reacts, and only once per instance id.
            if (_raidRepository != null)
                await SyncRaidTransfersAsync(partyIds);
        }

        /// <summary>
        /// A peer zone pushed a social change hint. The body is never trusted beyond ids: we funnel
        /// the notification straight back into <see cref="SyncAsync"/>, which re-reads the record,
        /// re-runs the same <c>RaidRules</c> and repository validation as the poll path, and only
        /// then acts. A stale, duplicate or tampered push can at worst cause an early correct pass,
        /// never an incorrect action. Public so tests can drive one push without a socket.
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

        /// <summary>
        /// Removes a disbanding party (its last member left) from its raid. Since #54 a raid groups
        /// many parties, so this is a <em>partial</em> teardown: the raid and its other member
        /// parties survive; only the whole raid disappears when this was the last party.
        /// </summary>
        private async Task TeardownRaidAsync(PartyRecord party)
        {
            if (_raidService == null || party == null || string.IsNullOrEmpty(party.RaidInstanceId))
                return;

            try
            {
                await _raidService.RemovePartyFromRaidAsync(party.PartyId);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Social] Failed to tear down raid membership for '{party.PartyId}': {e.Message}");
            }
        }

        /// <summary>
        /// Walks this zone's hosted parties for an allocated raid and asks each local member's
        /// <see cref="PlayerRaid"/> to transfer when this zone is not the instance host.
        /// </summary>
        private async Task SyncRaidTransfersAsync(HashSet<string> partyIds)
        {
            foreach (string partyId in partyIds)
            {
                PartyRecord party = await _repository.LoadPartyAsync(partyId, CancellationToken.None);
                if (party == null || string.IsNullOrEmpty(party.RaidInstanceId))
                    continue;

                RaidInstanceRecord instance = await _raidRepository.LoadRaidInstanceAsync(party.RaidInstanceId, CancellationToken.None);

                // Reconcile repair: a pointer whose raid is gone, or whose raid no longer lists this
                // party among its member parties, is stale. Clear it so the party stops steering its
                // members at a dead instance; #54's cross-object writes can leave exactly this state.
                if (instance == null || !RaidRules.IncludesParty(instance, party.PartyId))
                {
                    await RepairDanglingRaidPointerAsync(party);
                    continue;
                }

                // Cache the raid record if this zone is the instance host, so we have the anchors
                // after the record is deleted on completion.
                if (string.Equals(_zoneId, instance.HostZoneId, StringComparison.Ordinal))
                {
                    _hostedRaidCache[instance.InstanceId] = instance;
                }

                bool rosterRaised = false;
                foreach (LocalPlayerHandle local in new List<LocalPlayerHandle>(_local.Values))
                {
                    if (!PartyRules.IsMember(party, local.AccountId))
                        continue;

                    // Repaint the raid view once per hosted party, before deciding on any handoff.
                    if (!rosterRaised)
                    {
                        rosterRaised = true;
                        RaidChanged?.Invoke(instance);
                    }

                    if (!RaidRules.NeedsTransfer(instance.InstanceId, _zoneId, instance.HostZoneId))
                    {
                        _lastRaidTransfers.Remove(local.AccountId);
                        continue;
                    }

                    if (_lastRaidTransfers.TryGetValue(local.AccountId, out string last) && last == instance.InstanceId)
                        continue;

                    // Record return anchor before transferring — a member must never be sent to a
                    // host that doesn't know where to send them back.
                    bool anchorRecorded = await RecordReturnAnchorAsync(instance, local.AccountId);
                    if (!anchorRecorded)
                    {
                        Debug.LogWarning($"[Raid] Skipping transfer for '{local.AccountId}' — failed to record return anchor.");
                        continue;
                    }

                    _lastRaidTransfers[local.AccountId] = instance.InstanceId;
                    RaidTransferRequested?.Invoke(local.AccountId, instance);
                }
            }
        }

        /// <summary>
        /// Clears a party's raid pointer after the reconcile pass found no matching membership entry.
        /// Version-guarded like every other write: a conflict re-reads, and a pointer that another
        /// actor already changed (or cleared) is left alone.
        /// </summary>
        private async Task RepairDanglingRaidPointerAsync(PartyRecord party)
        {
            if (party == null || string.IsNullOrEmpty(party.RaidInstanceId))
                return;

            await RetryPartyAsync(async () =>
            {
                PartyRecord current = await _repository.LoadPartyAsync(party.PartyId, CancellationToken.None);
                if (current == null || string.IsNullOrEmpty(current.RaidInstanceId))
                    return PartyRule.Ok;

                if (current.RaidInstanceId != party.RaidInstanceId)
                    return PartyRule.Ok;

                current.RaidInstanceId = null;
                await _repository.SavePartyAsync(current, CancellationToken.None);
                return PartyRule.Ok;
            });
        }

        /// <summary>
        /// CAS-appends a return anchor for a member being transferred to the raid host. Returns true
        /// if the anchor was recorded (or already exists for this account), false if the CAS loop
        /// was exhausted and the member must not be transferred.
        /// </summary>
        private async Task<bool> RecordReturnAnchorAsync(RaidInstanceRecord instance, string accountId)
        {
            if (instance == null || string.IsNullOrEmpty(accountId) || _raidRepository == null)
                return false;

            string instanceId = instance.InstanceId;
            for (int i = 0; i < _casRetries; i++)
            {
                try
                {
                    RaidInstanceRecord current = await _raidRepository.LoadRaidInstanceAsync(instanceId, CancellationToken.None);
                    if (current == null)
                        return false;

                    if (current.ReturnAnchors == null)
                        current.ReturnAnchors = new List<RaidReturnAnchor>();

                    // Check if anchor already exists for this account
                    bool exists = false;
                    foreach (RaidReturnAnchor a in current.ReturnAnchors)
                    {
                        if (a.AccountId == accountId)
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
                            OriginHost = _instanceHostAddress,
                            OriginPort = _instanceHostPort,
                            OriginServerId = _zoneId,
                            Returned = false,
                        });
                    }

                    await _raidRepository.SaveRaidInstanceAsync(current);
                    return true;
                }
                catch (StorageVersionConflictException)
                {
                    await BackoffAsync(i);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Raid] Failed to record return anchor for '{accountId}': {e.Message}");
                    return false;
                }
            }

            return false;
        }

        /// <summary>
        /// Attempts to transfer a member back to their origin zone using the recorded anchor.
        /// Called when the raid completes (RaidInstanceId cleared) and the member is still local.
        /// </summary>
        private async Task TryTransferHomeAsync(string accountId, string instanceId)
        {
            if (string.IsNullOrEmpty(accountId) || string.IsNullOrEmpty(instanceId))
                return;

            // Exactly-once per session: skip if we've already issued a return for this member
            string key = accountId + ":" + instanceId;
            if (_returnedMembers.Contains(key))
                return;

            // Load the cached raid record (survives DeleteRaidInstanceAsync)
            if (!_hostedRaidCache.TryGetValue(instanceId, out RaidInstanceRecord raid))
                return;

            if (raid.ReturnAnchors == null)
                return;

            RaidReturnAnchor anchor = default;
            bool found = false;
            foreach (RaidReturnAnchor a in raid.ReturnAnchors)
            {
                if (a.AccountId == accountId)
                {
                    anchor = a;
                    found = true;
                    break;
                }
            }

            if (!found)
                return;

            // Check if member is still local to this host zone
            bool memberStillLocal = _local.ContainsKey(accountId);

            // Pure decision: can we transfer back?
            RaidTransferRule rule = RaidTransferRules.CanTransferBack(accountId, _zoneId, anchor, memberStillLocal);
            if (rule != RaidTransferRule.Ok)
                return;

            // Mark as returned in-memory (session-level exactly-once)
            _returnedMembers.Add(key);

            // Raise the return event; PlayerRaid will run the handoff to the anchor's origin
            RaidReturnRequested?.Invoke(accountId, anchor);

            // CAS the anchor's Returned = true for cross-session durability
            await MarkAnchorReturnedAsync(instanceId, accountId);
        }

        /// <summary>
        /// CAS-sets the Returned flag on the anchor after the token has been minted.
        /// Best-effort: a failure leaves the anchor unreturned so the next pass retries.
        /// </summary>
        private async Task MarkAnchorReturnedAsync(string instanceId, string accountId)
        {
            if (_raidRepository == null)
                return;

            for (int i = 0; i < _casRetries; i++)
            {
                try
                {
                    RaidInstanceRecord current = await _raidRepository.LoadRaidInstanceAsync(instanceId, CancellationToken.None);
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

                    await _raidRepository.SaveRaidInstanceAsync(current);
                    return;
                }
                catch (StorageVersionConflictException)
                {
                    await BackoffAsync(i);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Raid] Failed to mark anchor returned for '{accountId}': {e.Message}");
                    return;
                }
            }
        }

        #endregion

        #region Presence

        private async Task AnnounceAsync(LocalPlayerHandle local)
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
            foreach (LocalPlayerHandle local in new List<LocalPlayerHandle>(_local.Values))
                await SavePresenceAsync(local);
        }

        /// <summary>
        /// Refreshes this zone's <see cref="ZoneRegistryRecord"/> so it stays a fresh host candidate
        /// and publishes its advertised address, template and capacity (#57). The existing load is
        /// preserved; the version is reused so a racing write from another process wins cleanly.
        /// </summary>
        private async Task HeartbeatZoneAsync()
        {
            if (_zoneRepository == null)
                return;

            try
            {
                List<ZoneRegistryRecord> zones = await _zoneRepository.LoadZonesAsync(CancellationToken.None);
                ZoneRegistryRecord existing = FindZone(zones, _zoneId);

                ZoneRegistryRecord record = ZoneRegistryRecord.Create(
                    _zoneId,
                    _instanceHostAddress,
                    _instanceHostPort,
                    _zoneTemplateId,
                    _maxInstances,
                    DateTimeOffset.UtcNow,
                    existing?.ActiveInstances ?? 0,
                    existing?.StorageVersion);

                await _zoneRepository.SaveZoneAsync(record, CancellationToken.None);
            }
            catch (StorageVersionConflictException)
            {
                // A racing heartbeat from another write wins; the next tick refreshes ours.
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Social] Failed to write zone registry heartbeat for '{_zoneId}': {e.Message}");
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

        private async Task SavePresenceAsync(LocalPlayerHandle local)
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
            if (_local.TryGetValue(accountId, out LocalPlayerHandle local))
                return local.PlayerName;

            PresenceRecord presence = await _repository.LoadPresenceAsync(accountId, CancellationToken.None);
            return string.IsNullOrEmpty(presence?.PlayerName) ? accountId : presence.PlayerName;
        }

        private PartyMemberRecord NewMember(string accountId, SocialIndexRecord _, DateTimeOffset now)
            => new PartyMemberRecord
            {
                AccountId = accountId,
                PlayerName = _local.TryGetValue(accountId, out LocalPlayerHandle local) ? local.PlayerName : accountId,
                ZoneId = _zoneId,
                JoinedAtTicks = now.UtcDateTime.Ticks,
            };

        private static string Normalize(string name) => (name ?? string.Empty).Trim().ToLowerInvariant();

        /// <summary>Gets the player's display name for auction listings.</summary>
        public string GetPlayerName(string accountId)
        {
            if (_local.TryGetValue(accountId, out LocalPlayerHandle local))
                return local.PlayerName;
            return accountId;
        }

        /// <summary>Checks if a local player exists and gets their PlayerGuild (for bag access).</summary>
        public bool TryGetLocalPlayer(string accountId, out PlayerGuild player)
        {
            return _localGuilds.TryGetValue(accountId, out player);
        }

        /// <summary>Notifies the seller's client that their listing was created.</summary>
        internal void NotifyAuctionListingCreated(string sellerAccountId, AuctionListingRecord listing)
        {
            AuctionListingCreated?.Invoke(sellerAccountId, listing);
        }

        /// <summary>Notifies both buyer and seller that a listing was sold.</summary>
        internal void NotifyAuctionListingSold(string buyerAccountId, string sellerAccountId, AuctionListingRecord listing, AuctionTransactionRecord transaction)
        {
            AuctionListingSold?.Invoke(buyerAccountId, sellerAccountId, listing, transaction);
        }

        /// <summary>Notifies the seller that their listing was cancelled.</summary>
        internal void NotifyAuctionListingCancelled(string sellerAccountId, AuctionListingRecord listing)
        {
            AuctionListingCancelled?.Invoke(sellerAccountId, listing);
        }

        /// <summary>Notifies the buyer that their purchase completed and item was delivered.</summary>
        internal void NotifyAuctionPurchaseCompleted(string buyerAccountId, AuctionTransactionRecord transaction)
        {
            AuctionPurchaseCompleted?.Invoke(buyerAccountId, transaction);
        }

        /// <summary>Notifies the buyer's client that their buy order was created.</summary>
        internal void NotifyAuctionBuyOrderCreated(string buyerAccountId, AuctionBuyOrderRecord buyOrder)
        {
            AuctionBuyOrderCreated?.Invoke(buyerAccountId, buyOrder);
        }

        /// <summary>Notifies both seller and buyer that a buy order was fulfilled.</summary>
        internal void NotifyAuctionBuyOrderFulfilled(string sellerAccountId, string buyerAccountId, AuctionBuyOrderRecord buyOrder, AuctionTransactionRecord transaction)
        {
            AuctionBuyOrderFulfilled?.Invoke(sellerAccountId, buyerAccountId, buyOrder, transaction);
        }

        /// <summary>Notifies the buyer that their buy order was cancelled.</summary>
        internal void NotifyAuctionBuyOrderCancelled(string buyerAccountId, AuctionBuyOrderRecord buyOrder)
        {
            AuctionBuyOrderCancelled?.Invoke(buyerAccountId, buyOrder);
        }

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

        #endregion

        #region Raid intents

        /// <summary>Leader-only: allocates a raid instance for the requester's party.</summary>
        public async Task<(RaidRule Rule, RaidInstanceRecord Instance)> AllocateRaidAsync(string requesterId)
        {
            if (_raidService == null)
                return (RaidRule.BackendUnavailable, null);

            // Use orchestration service if enabled, otherwise fall back to direct allocation.
            if (_orchestrationService != null)
            {
                SocialIndexRecord index = await _repository.LoadIndexAsync(requesterId);
                if (index == null || string.IsNullOrEmpty(index.PartyId))
                    return (RaidRule.NoParty, null);

                PartyRecord party = await _repository.LoadPartyAsync(index.PartyId);
                var orchestrated = await _orchestrationService.EnterRaidAsync(party, CancellationToken.None);
                if (orchestrated.Rule == RaidRule.Ok)
                    await SyncAsync();
                return orchestrated;
            }

            var result = await _raidService.AllocateAsync(requesterId);
            if (result.Rule == RaidRule.Ok)
                await SyncAsync();
            return result;
        }

        /// <summary>
        /// Leader-only: joins the requester's party into an existing raid. Capacity is re-checked
        /// against the live roster inside the service's CAS loop, so a race for the last seat loses
        /// cleanly rather than oversubscribing the raid.
        /// </summary>
        public async Task<RaidRule> JoinRaidAsync(string requesterId, string raidId)
        {
            if (_raidService == null)
                return RaidRule.BackendUnavailable;
            RaidRule rule = await _raidService.JoinAsync(requesterId, raidId);
            if (rule == RaidRule.Ok)
                await SyncAsync();
            return rule;
        }

        /// <summary>Leader-only: pulls the requester's whole party out of its raid.</summary>
        public async Task<RaidRule> LeaveRaidAsync(string requesterId)
        {
            if (_raidService == null)
                return RaidRule.BackendUnavailable;
            RaidRule rule = await _raidService.LeaveAsync(requesterId);
            if (rule == RaidRule.Ok)
                await SyncAsync();
            return rule;
        }

        /// <summary>Raid-leader-only: hands raid leadership to another member.</summary>
        public async Task<RaidRule> PromoteRaidLeaderAsync(string requesterId, string targetId)
        {
            if (_raidService == null)
                return RaidRule.BackendUnavailable;
            RaidRule rule = await _raidService.PromoteLeaderAsync(requesterId, targetId);
            if (rule == RaidRule.Ok)
                await SyncAsync();
            return rule;
        }

        /// <summary>Leader-only: completes the raid and tears its instance down.</summary>
        public async Task<RaidRule> CompleteRaidAsync(string requesterId)
        {
            if (_raidService == null)
                return RaidRule.BackendUnavailable;

            // Get the instance ID before completing, so we can teardown orchestrated instances.
            string instanceId = null;
            if (_orchestrationService != null)
            {
                SocialIndexRecord index = await _repository.LoadIndexAsync(requesterId);
                if (index != null && !string.IsNullOrEmpty(index.PartyId))
                {
                    PartyRecord party = await _repository.LoadPartyAsync(index.PartyId);
                    if (party != null)
                        instanceId = party.RaidInstanceId;
                }
            }

            RaidRule rule = await _raidService.CompleteAsync(requesterId);
            if (rule == RaidRule.Ok)
            {
                await SyncAsync();

                // Teardown orchestrated container if this was an orchestrated raid.
                if (_orchestrationService != null && !string.IsNullOrEmpty(instanceId))
                {
                    // Check if this was an orchestrated instance by looking at the raid record's host zone.
                    // Orchestrated instances have HostZoneId starting with "orchestrator-".
                    RaidInstanceRecord raid = await _raidRepository.LoadRaidInstanceAsync(instanceId);
                    if (raid != null && raid.HostZoneId != null && raid.HostZoneId.StartsWith("orchestrator-"))
                    {
                        await _orchestrationService.TeardownAsync(instanceId, CancellationToken.None);
                    }
                }
            }
            return rule;
        }

        #endregion
    }
}
