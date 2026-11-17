using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FishNet.Managing;
using MMO.Auction;
using MMO.Economy;
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

        /// <summary>How often to sweep each local player's inbox for claimable mail.</summary>
        [SerializeField, Min(1f)] private float _mailSweepInterval = 5f;

        [SerializeField] private string _zoneId = "local";

        /// <summary>
        /// The territory this zone's auction house stands in, or empty when sales here are untaxed.
        /// A zone sets this to the <c>ClaimFlag.TerritoryId</c> it contains; the tax rate and owner
        /// are read from the durable <see cref="TerritoryRecord"/> at sale time, never cached.
        /// </summary>
        [SerializeField] private string _territoryId = string.Empty;

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

        private LocalPlayerRegistry _localPlayers;
        private SocialAccountResolver _accounts;
        private SocialHintClient _hints;
        private PresenceService _presence;
        private TerritoryService _territory;
        private MailService _mail;
        private FriendService _friends;
        private GuildService _guildService;
        private PartyService _partyService;
        private GuildBankService _guildBank;
        private RaidHandoffService _raidHandoff;
        private SocialReconciler _reconciler;
        private RaidIntentsService _raidIntents;
        private SocialBackendConnection _connection;
        private SocialRealtimeRouter _realtime;
        private SocialTickScheduler _ticker;
        private readonly SocialAuctionNotifier _auctionNotifier = new SocialAuctionNotifier();
        /// <summary>Server-wide single-writer authority for item instance ownership.</summary>
        private ItemOwnershipRegistry _ownershipRegistry;

        /// <summary>Access to the item ownership registry for production code.</summary>
        public ItemOwnershipRegistry OwnershipRegistry => _ownershipRegistry;

        private ISocialRepository _repository;
        private IRaidInstanceRepository _raidRepository;
        private IZoneRegistryRepository _zoneRepository;
        private NakamaRestClient _rest;
        private RaidInstanceService _raidService;
        private RaidOrchestrationService _orchestrationService;
        private IOrchestratorClient _orchestratorClient;
        private AuctionService _auctionService;
        private NetworkManager _manager;
        private bool _subscribed;

        /// <summary>Raised on the main thread for every inbound bus envelope (chat or notify).</summary>
        public event Action<SocialBusEnvelope> EnvelopeReceived
        {
            add => _realtime.EnvelopeReceived += value;
            remove => _realtime.EnvelopeReceived -= value;
        }

        /// <summary>Chat routing, coded against this hub as its roster and bus.</summary>
        public ChatService Chat => _realtime.Chat;

        /// <summary>The shared REST client the socket and the notify hook use, or null in tests.</summary>
        public NakamaRestClient Rest => _rest;

        /// <summary>The auction service for player-driven trading.</summary>
        public AuctionService Auction => _auctionService;

        bool ISocialBus.IsConnected => _realtime.IsConnected;

        bool ISocialBus.TrySend(SocialBusEnvelope envelope)
            => _realtime.TrySend(envelope);

        /// <summary>Raised with the authoritative party whenever any party this zone hosts changes.</summary>
        public event Action<PartyRecord> PartyChanged;

        /// <summary>Raised with the authoritative guild whenever a guild this zone hosts changes.</summary>
        public event Action<GuildRecord> GuildChanged;

        /// <summary>
        /// Raised with the authoritative bank whenever a guild bank this zone hosts changes (a local
        /// deposit/withdraw/edit, or the reconcile pass seeing a change made on another zone).
        /// </summary>
        public event Action<GuildBankRecord> BankChanged;

        /// <summary>Raised with the authoritative territory record after a winning capture.</summary>
        public event Action<TerritoryRecord> TerritoryChanged;

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
        public event Action<string, AuctionListingRecord> AuctionListingCreated
        {
            add => _auctionNotifier.ListingCreated += value;
            remove => _auctionNotifier.ListingCreated -= value;
        }

        /// <summary>Raised when an auction listing is sold (buyer and seller notified).</summary>
        public event Action<string, string, AuctionListingRecord, AuctionTransactionRecord> AuctionListingSold
        {
            add => _auctionNotifier.ListingSold += value;
            remove => _auctionNotifier.ListingSold -= value;
        }

        /// <summary>Raised when an auction listing is cancelled by the seller.</summary>
        public event Action<string, AuctionListingRecord> AuctionListingCancelled
        {
            add => _auctionNotifier.ListingCancelled += value;
            remove => _auctionNotifier.ListingCancelled -= value;
        }

        /// <summary>Raised when a purchase is completed and the buyer receives the item.</summary>
        public event Action<string, AuctionTransactionRecord> AuctionPurchaseCompleted
        {
            add => _auctionNotifier.PurchaseCompleted += value;
            remove => _auctionNotifier.PurchaseCompleted -= value;
        }

        /// <summary>Raised when a new buy order is created by a local player.</summary>
        public event Action<string, AuctionBuyOrderRecord> AuctionBuyOrderCreated
        {
            add => _auctionNotifier.BuyOrderCreated += value;
            remove => _auctionNotifier.BuyOrderCreated -= value;
        }

        /// <summary>Raised when a buy order is fulfilled (seller and buyer notified).</summary>
        public event Action<string, string, AuctionBuyOrderRecord, AuctionTransactionRecord> AuctionBuyOrderFulfilled
        {
            add => _auctionNotifier.BuyOrderFulfilled += value;
            remove => _auctionNotifier.BuyOrderFulfilled -= value;
        }

        /// <summary>Raised when a buy order is cancelled by the buyer.</summary>
        public event Action<string, AuctionBuyOrderRecord> AuctionBuyOrderCancelled
        {
            add => _auctionNotifier.BuyOrderCancelled += value;
            remove => _auctionNotifier.BuyOrderCancelled -= value;
        }

        /// <summary>The zone label stamped into presence so a member's host zone can be resolved.</summary>
        public string ZoneId
        {
            get => _zoneId;
            set => _zoneId = value ?? "local";
        }

        /// <summary>Whether the backend is currently reachable (fakes are considered always up).</summary>
        public bool IsConnected => _connection != null && _connection.IsConnected;

        /// <summary>Whether this zone has an auction house (is a central trading hub).</summary>
        public bool HasAuctionHouse => _auctionHouseZoneConfig != null && _auctionHouseZoneConfig.HasAuctionHouse(_zoneId);

        private void Awake()
        {
            Instance = this;
            EnsureInitialized();
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
            _realtime?.Detach();

            if (Instance == this)
                Instance = null;
        }

        /// <summary>
        /// Builds the local state the hub needs. EditMode tests add the component without a
        /// domain reload, so their <see cref="Awake"/> may not run; <see cref="Configure"/> calls
        /// this too so the fakes behave exactly like production.
        /// </summary>
        private void EnsureInitialized()
        {
            if (_localPlayers != null)
                return;

            _localPlayers = new LocalPlayerRegistry(() => _zoneId);
            _hints = new SocialHintClient(() => _rest);
            _connection = new SocialBackendConnection(() => _rest, _connectRetrySeconds);
            _realtime = new SocialRealtimeRouter(
                _localPlayers, () => _friends, () => _reconciler, () => _auctionService);
            _realtime.EnsureChat(this);
            _localPlayers.Registered += OnLocalRegistered;
            _localPlayers.Unregistered += OnLocalUnregistered;
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
            EnsureInitialized();
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _accounts = new SocialAccountResolver(_repository, _localPlayers, () => _zoneId);
            _presence = new PresenceService(
                () => _repository,
                () => _zoneRepository,
                _localPlayers,
                EnsureConnectedAsync,
                () => _zoneId,
                () => _instanceHostAddress,
                () => _instanceHostPort,
                () => _zoneTemplateId,
                () => _maxInstances);
            _territory = new TerritoryService(
                () => _repository,
                EnsureConnectedAsync,
                _casRetries,
                record => TerritoryChanged?.Invoke(record));
            _mail = new MailService(
                () => _repository,
                EnsureConnectedAsync,
                _localPlayers,
                _casRetries);
            _friends = new FriendService(
                () => _repository,
                EnsureConnectedAsync,
                _localPlayers,
                _hints,
                _casRetries);
            _guildService = new GuildService(
                () => _repository,
                EnsureConnectedAsync,
                _accounts,
                SyncAsync,
                (accountId, invite) => GuildInviteReceived?.Invoke(accountId, invite),
                _casRetries);
            _partyService = new PartyService(
                () => _repository,
                EnsureConnectedAsync,
                _accounts,
                TeardownRaidAsync,
                SyncAsync,
                (accountId, invite) => PartyInviteReceived?.Invoke(accountId, invite),
                _casRetries);
            _guildBank = new GuildBankService(
                _repository,
                EnsureConnectedAsync,
                _localPlayers,
                bank => BankChanged?.Invoke(bank),
                _casRetries);
            _raidRepository = raidRepository;
            _zoneRepository = zoneRepository;
            _raidHandoff = new RaidHandoffService(
                () => _repository,
                () => _raidRepository,
                _localPlayers,
                () => _zoneId,
                () => _instanceHostAddress,
                () => _instanceHostPort,
                _casRetries,
                record => RaidChanged?.Invoke(record),
                (accountId, record) => RaidTransferRequested?.Invoke(accountId, record),
                (accountId, anchor) => RaidReturnRequested?.Invoke(accountId, anchor));
            _reconciler = new SocialReconciler(
                () => _repository,
                EnsureConnectedAsync,
                _localPlayers,
                _friends,
                _raidHandoff,
                () => _raidRepository,
                record => PartyChanged?.Invoke(record),
                record => GuildChanged?.Invoke(record),
                record => BankChanged?.Invoke(record),
                (accountId, invite) => PartyInviteReceived?.Invoke(accountId, invite),
                (accountId, invite) => GuildInviteReceived?.Invoke(accountId, invite));

            if (repository is NakamaSocialRepository nakama)
            {
                _rest = nakama.Rest;
                _connection.SetInitialState(_rest.IsConnected);
            }
            else
            {
                _rest = null;
                _connection.SetInitialState(true);
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

            _raidIntents = new RaidIntentsService(
                _raidService,
                _orchestrationService,
                () => _repository,
                () => _raidRepository,
                SyncAsync);
        }

        #region Realtime backbone

        /// <summary>
        /// Wires the zone server's Nakama socket. Every inbound envelope is routed here: chat
        /// through <see cref="ChatService"/>, friend/party/guild hints as a cue to re-read and push
        /// snapshots. Called by <c>Net.NetworkManagerFactory</c>.
        /// </summary>
        public void AttachSocket(NakamaSocialSocket socket)
        {
            _realtime.Attach(this, socket);

            if (socket != null)
                _ = AnnounceLocalPresenceAsync();
        }

        /// <summary>Attaches the shared REST client the notify hook uses for <c>social_notify</c>.</summary>
        public void AttachRestClient(NakamaRestClient rest)
        {
            if (rest != null)
                _rest = rest;
        }

        /// <summary>Called by <see cref="PlayerChat"/> (server): routes this player's chat deliveries.</summary>
        public void AttachChat(string accountId, Action<string> deliverChat)
            => _localPlayers.AttachChat(accountId, deliverChat);

        /// <summary>Called by <see cref="PlayerChat"/> (server) when the object despawns.</summary>
        public void DetachChat(string accountId)
            => _localPlayers.DetachChat(accountId);

        /// <summary>Called by <see cref="PlayerFriends"/> (server): routes this player's friend pushes.</summary>
        public void AttachFriends(string accountId, Action<string> deliverSnapshot, Action<int> notifyEvent)
            => _localPlayers.AttachFriends(accountId, deliverSnapshot, notifyEvent);

        /// <summary>Called by <see cref="PlayerFriends"/> (server) when the object despawns.</summary>
        public void DetachFriends(string accountId)
            => _localPlayers.DetachFriends(accountId);

        /// <summary>Called by <see cref="PlayerGuild"/> (server): registers the local guild view for bank work.</summary>
        public void AttachGuildView(string accountId, PlayerGuild guild)
            => _localPlayers.AttachGuild(accountId, guild);

        /// <summary>Called by <see cref="PlayerGuild"/> (server) when the object despawns.</summary>
        public void DetachGuildView(string accountId)
            => _localPlayers.DetachGuild(accountId);

        /// <summary>Called by <see cref="PlayerWallet"/> (server): registers the local wallet for auction access.</summary>
        public void AttachWalletView(string accountId, PlayerWallet wallet)
            => _localPlayers.AttachWallet(accountId, wallet);

        /// <summary>Called by <see cref="PlayerWallet"/> (server) when the object despawns.</summary>
        public void DetachWalletView(string accountId)
            => _localPlayers.DetachWallet(accountId);

        /// <summary>Gets the wallet for a local player (for gold checks).</summary>
        public bool TryGetWallet(string accountId, out PlayerWallet wallet)
            => _localPlayers.TryGetWallet(accountId, out wallet);

        private async Task AnnounceLocalPresenceAsync()
        {
            foreach (LocalPlayerHandle local in _localPlayers.Local)
                await _presence.SavePresenceAsync(local);
        }

        #endregion

        #region ILocalRoster

        /// <summary>The zone label stamped into presence so a member's host zone can be resolved.</summary>
        string ILocalRoster.LocalZoneId => _localPlayers.LocalZoneId;

        IReadOnlyList<LocalPlayerHandle> ILocalRoster.Local => _localPlayers.Local;

        string ILocalRoster.PartyOf(string accountId) => _localPlayers.PartyOf(accountId);

        string ILocalRoster.NameOf(string accountId) => _localPlayers.NameOf(accountId);

        #endregion

        #region Registration + lifecycle

        private void Start()
        {
            _manager = GetComponentInParent<NetworkManager>();
            _ticker = new SocialTickScheduler(
                _syncInterval, _presenceInterval, _friendRefreshInterval, _mailSweepInterval);
            _ticker.Start(Time.time);
        }

        /// <summary>
        /// Called by <see cref="PlayerParty"/> (server) once the player object has spawned. The
        /// local FishNet views subscribe to <see cref="PartyChanged"/>/<see cref="GuildChanged"/>
        /// themselves; the hub only needs the account id and display name.
        /// </summary>
        public void RegisterLocal(string accountId, string playerName)
            => _localPlayers.Register(accountId, playerName);

        /// <summary>Called by <see cref="PlayerParty"/> (server) when the player object despawns.</summary>
        public void UnregisterLocal(string accountId)
            => _localPlayers.Unregister(accountId);

        private void OnLocalRegistered(LocalPlayerHandle local)
            => _ = AnnounceLocalAsync(local);

        private void OnLocalUnregistered(string accountId)
            => _raidHandoff?.Forget(accountId);

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
            if (_repository == null || _manager == null || !_manager.IsServerStarted || _connection == null || !_connection.IsConnected)
                return;

            _ticker.Tick(
                Time.time,
                _localPlayers.Count > 0,
                _zoneRepository != null,
                () => _ = _presence.HeartbeatZoneAsync(),
                () => _ = _presence.HeartbeatAsync(),
                () => _ = SyncAsync(),
                () => _ = _friends.RefreshLocalAsync(),
                () => _ = _mail.SweepAsync());
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
            if (_realtime.IsAttached || _rest == null)
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
            => _connection != null ? _connection.EnsureConnectedAsync() : Task.FromResult(false);

        #endregion

        #region Party intents

        /// <summary>Invites the named player to the inviter's party, creating one if needed.</summary>
        public Task<PartyRule> InviteAsync(string inviterId, string targetName)
            => _partyService.InviteAsync(inviterId, targetName);

        /// <summary>Accepts the party invite this account holds.</summary>
        public Task<PartyRule> AcceptAsync(string accountId)
            => _partyService.AcceptAsync(accountId);

        /// <summary>Declines the party invite this account holds.</summary>
        public Task<PartyRule> DeclineAsync(string accountId)
            => _partyService.DeclineAsync(accountId);

        /// <summary>Leaves the party this account belongs to.</summary>
        public Task<PartyRule> LeaveAsync(string accountId)
            => _partyService.LeaveAsync(accountId);

        /// <summary>Leader-only: removes the target from the leader's party.</summary>
        public Task<PartyRule> KickAsync(string leaderId, string targetId)
            => _partyService.KickAsync(leaderId, targetId);

        #endregion

        #region Guild intents

        /// <summary>Creates a guild with this account as its leader.</summary>
        public Task<GuildRule> CreateGuildAsync(string accountId, string guildName)
            => _guildService.CreateAsync(accountId, guildName);

        /// <summary>Leader/officer-only: invites the named player to the inviter's guild.</summary>
        public Task<GuildRule> InviteToGuildAsync(string inviterId, string targetName)
            => _guildService.InviteAsync(inviterId, targetName);

        /// <summary>Accepts the guild invite this account holds.</summary>
        public Task<GuildRule> AcceptGuildInviteAsync(string accountId)
            => _guildService.AcceptInviteAsync(accountId);

        /// <summary>Leaves the guild this account belongs to.</summary>
        public Task<GuildRule> LeaveGuildAsync(string accountId)
            => _guildService.LeaveAsync(accountId);

        /// <summary>Leader-only: promotes the target one rank.</summary>
        public Task<GuildRule> PromoteAsync(string leaderId, string targetId)
            => _guildService.PromoteAsync(leaderId, targetId);

        /// <summary>Leader/officer-only: removes the target from the guild.</summary>
        public Task<GuildRule> KickFromGuildAsync(string actorId, string targetId)
            => _guildService.KickAsync(actorId, targetId);

        #endregion

        #region Guild bank intents

        /// <summary>Moves the item at the bag cell into the guild bank's tab.</summary>
        public Task<GuildBankResult> DepositAsync(string accountId, int tabIndex, int bagX, int bagY)
            => _guildBank.DepositAsync(accountId, tabIndex, bagX, bagY);

        /// <summary>Moves the item at the bank cell into the member's bag.</summary>
        public Task<GuildBankResult> WithdrawAsync(string accountId, int tabIndex, int gridX, int gridY, int stackCount = -1)
            => _guildBank.WithdrawAsync(accountId, tabIndex, gridX, gridY, stackCount);

        /// <summary>Leader/officer-only: edits a rank's bank permissions.</summary>
        public Task<GuildBankRule> SetBankPermissionAsync(
            string accountId, GuildRank rank, bool allowDeposit, bool allowWithdraw, int withdrawLimit,
            long withdrawValueLimit = GuildBankRules.UnlimitedValue, long depositValueLimit = GuildBankRules.UnlimitedValue)
            => _guildBank.SetBankPermissionAsync(
                accountId, rank, allowDeposit, allowWithdraw, withdrawLimit, withdrawValueLimit, depositValueLimit);

        /// <summary>Moves currency from the member's wallet into the guild vault.</summary>
        public Task<GuildBankRule> DepositCurrencyAsync(string accountId, string currencyId, long amount)
            => _guildBank.DepositCurrencyAsync(accountId, currencyId, amount);

        /// <summary>Moves currency from the guild vault into the member's wallet.</summary>
        public Task<GuildBankRule> WithdrawCurrencyAsync(string accountId, string currencyId, long amount)
            => _guildBank.WithdrawCurrencyAsync(accountId, currencyId, amount);

        #endregion

        #region Territory intents

        /// <summary>Reads one territory's durable owner, or null when it has never been captured.</summary>
        public Task<TerritoryRecord> LoadTerritoryAsync(string territoryId)
            => _territory.LoadAsync(territoryId);

        /// <summary>
        /// Captures an unowned flag, or an owned one inside its vulnerability window, for the
        /// requesting player's guild.
        /// </summary>
        public Task<TerritoryRule> CaptureTerritoryAsync(
            string accountId, string territoryId, VulnerabilityWindow window, DateTimeOffset now)
            => _territory.CaptureAsync(accountId, territoryId, window, now);

        /// <summary>Leader-only: sets a claimed territory's tax rate.</summary>
        public Task<TerritoryRule> SetTerritoryTaxAsync(string accountId, string territoryId, int taxRateBp)
            => _territory.SetTaxAsync(accountId, territoryId, taxRateBp);

        /// <summary>The territory this zone's auction house belongs to; empty disables territory tax.</summary>
        public string TerritoryId
        {
            get => _territoryId;
            set => _territoryId = value;
        }

        /// <summary>
        /// Withholds territory tax from an auction seller's gross proceeds and returns the net the
        /// seller should actually receive. The tax is applied only for a sale in this zone's claimed
        /// territory (<see cref="TerritoryId"/>), at the rate stored on its record, and it is
        /// appended to the owning guild's ledger before the net is returned. If the ledger write
        /// fails the seller is paid in full: an outage must not silently confiscate proceeds.
        /// </summary>
        /// <param name="sellerZoneId">The zone the sale was listed in; null means this zone.</param>
        public async Task<long> ApplyTerritoryTaxAsync(
            string sellerAccountId,
            string sellerZoneId,
            long gross,
            CancellationToken ct = default)
        {
            if (gross <= 0)
                return 0;

            if (!string.IsNullOrEmpty(sellerZoneId) && sellerZoneId != _zoneId)
                return gross;

            if (string.IsNullOrEmpty(_territoryId) || _territory == null || _guildBank == null)
                return gross;

            TerritoryRecord territory = await _territory.LoadAsync(_territoryId);
            if (territory == null
                || string.IsNullOrEmpty(territory.OwnerGuildId)
                || territory.TaxRateBp <= 0)
            {
                return gross;
            }

            long tax = TaxationRules.TaxOn(gross, territory.TaxRateBp);
            if (tax <= 0 || tax >= gross)
                return gross;

            bool recorded = await _guildBank.AppendTerritoryTaxAsync(
                territory.OwnerGuildId,
                sellerAccountId,
                CurrencyIds.Gold,
                tax,
                DateTimeOffset.UtcNow.UtcTicks);

            if (!recorded)
                return gross;

            // Pay what was just recorded. Idempotent: the bank record remembers the entry id, so the
            // same pass running twice (or from another zone) cannot credit it twice.
            await _guildBank.SettleTerritoryRevenueAsync(
                territory.OwnerGuildId, DateTimeOffset.UtcNow.UtcTicks);

            return gross - tax;
        }

        #endregion

        #region Mail intents

        /// <summary>Finds a message by its exact body.</summary>
        public Task<string> FindMailIdByBodyAsync(string accountId, string body)
            => _mail.FindMailIdByBodyAsync(accountId, body);

        /// <summary>Delivers a message to the recipient's inbox, offline-first.</summary>
        public Task<MailRule> SendMailAsync(
            string toAccountId,
            string fromAccountId,
            string subject,
            string body,
            long currency = 0,
            IReadOnlyList<ItemSaveData> attachments = null,
            long expiresAtTicks = 0,
            bool ignoreInboxCap = false,
            CancellationToken ct = default)
            => _mail.SendAsync(
                toAccountId, fromAccountId, subject, body, currency, attachments, expiresAtTicks, ignoreInboxCap, ct);

        /// <summary>Claims a local player's message and grants its attachments.</summary>
        public Task<MailRule> ClaimMailAsync(string accountId, string mailId, CancellationToken ct = default)
            => _mail.ClaimAsync(accountId, mailId, ct);

        /// <summary>Claims every claimable message for a local player in one CAS write.</summary>
        public Task<int> ClaimAllMailAsync(string accountId, CancellationToken ct = default)
            => _mail.ClaimAllAsync(accountId, ct);

        #endregion

        #region Friend intents

        /// <summary>Sends a friend request from <paramref name="requesterId"/> to the named player.</summary>
        public Task<FriendRule> RequestFriendAsync(string requesterId, string targetName)
            => _friends.RequestAsync(requesterId, targetName);

        /// <summary>Accepts the pending friend request from <paramref name="fromAccountId"/>.</summary>
        public Task<FriendRule> AcceptFriendAsync(string accountId, string fromAccountId)
            => _friends.AcceptAsync(accountId, fromAccountId);

        /// <summary>Declines the pending friend request from <paramref name="fromAccountId"/>.</summary>
        public Task<FriendRule> DeclineFriendAsync(string accountId, string fromAccountId)
            => _friends.DeclineAsync(accountId, fromAccountId);

        /// <summary>Removes an accepted friend from both sides.</summary>
        public Task<FriendRule> RemoveFriendAsync(string accountId, string targetId)
            => _friends.RemoveAsync(accountId, targetId);

        /// <summary>Pushes a fresh snapshot to a local player's friend list.</summary>
        public Task PushFriendsAsync(string accountId)
            => _friends.PushAsync(accountId);

        #endregion

        #region Reconcile (cross-zone fanout)

        /// <summary>
        /// Diffs this zone's live players against the backend and pushes roster/invite changes to
        /// their FishNet objects. Public so tests can drive one pass without waiting a frame.
        /// </summary>
        public Task SyncAsync() => _reconciler.SyncAsync();

        /// <summary>A peer zone pushed a social change hint; re-run one reconcile pass.</summary>
        public Task OnSocialNotification(SocialNotification notification)
            => _reconciler.OnSocialNotification(notification);

        #endregion

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

        #region Presence

        private async Task AnnounceLocalAsync(LocalPlayerHandle local)
        {
            if (_presence == null || _accounts == null)
                return;

            if (!await _presence.AnnounceAsync(local))
                return;

            await _accounts.PublishNameAsync(local);
        }

        #endregion

        #region Helpers

        /// <summary>Gets the player's display name for auction listings.</summary>
        public string GetPlayerName(string accountId)
            => _localPlayers.GetPlayerName(accountId);

        /// <summary>Checks if a local player exists and gets their PlayerGuild (for bag access).</summary>
        public bool TryGetLocalPlayer(string accountId, out PlayerGuild player)
            => _localPlayers.TryGetGuild(accountId, out player);

        /// <summary>Notifies the seller's client that their listing was created.</summary>
        internal void NotifyAuctionListingCreated(string sellerAccountId, AuctionListingRecord listing)
            => _auctionNotifier.RaiseListingCreated(sellerAccountId, listing);

        /// <summary>Notifies both buyer and seller that a listing was sold.</summary>
        internal void NotifyAuctionListingSold(string buyerAccountId, string sellerAccountId, AuctionListingRecord listing, AuctionTransactionRecord transaction)
            => _auctionNotifier.RaiseListingSold(buyerAccountId, sellerAccountId, listing, transaction);

        /// <summary>Notifies the seller that their listing was cancelled.</summary>
        internal void NotifyAuctionListingCancelled(string sellerAccountId, AuctionListingRecord listing)
            => _auctionNotifier.RaiseListingCancelled(sellerAccountId, listing);

        /// <summary>Notifies the buyer that their purchase completed and item was delivered.</summary>
        internal void NotifyAuctionPurchaseCompleted(string buyerAccountId, AuctionTransactionRecord transaction)
            => _auctionNotifier.RaisePurchaseCompleted(buyerAccountId, transaction);

        /// <summary>Notifies the buyer's client that their buy order was created.</summary>
        internal void NotifyAuctionBuyOrderCreated(string buyerAccountId, AuctionBuyOrderRecord buyOrder)
            => _auctionNotifier.RaiseBuyOrderCreated(buyerAccountId, buyOrder);

        /// <summary>Notifies both seller and buyer that a buy order was fulfilled.</summary>
        internal void NotifyAuctionBuyOrderFulfilled(string sellerAccountId, string buyerAccountId, AuctionBuyOrderRecord buyOrder, AuctionTransactionRecord transaction)
            => _auctionNotifier.RaiseBuyOrderFulfilled(sellerAccountId, buyerAccountId, buyOrder, transaction);

        /// <summary>Notifies the buyer that their buy order was cancelled.</summary>
        internal void NotifyAuctionBuyOrderCancelled(string buyerAccountId, AuctionBuyOrderRecord buyOrder)
            => _auctionNotifier.RaiseBuyOrderCancelled(buyerAccountId, buyOrder);

        /// <summary>
        /// Best-effort MarketChanged hint so peer auction zones re-read the changed listing into
        /// their search cache. A lost hint is repaired by the periodic reconcile pass.
        /// </summary>
        internal void NotifyMarketChanged(string listingId)
        {
            if (string.IsNullOrEmpty(listingId) || _repository == null)
                return;

            _ = NotifyMarketChangedAsync(listingId);
        }

        private async Task NotifyMarketChangedAsync(string listingId)
        {
            try
            {
                await _repository.NotifySocialAsync(new SocialNotification
                {
                    Kind = SocialNotificationKind.MarketChanged,
                    ListingId = listingId,
                }, CancellationToken.None);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Auction] MarketChanged notify failed for {listingId}: {e.Message}");
            }
        }

        #endregion

        #region Raid intents

        /// <summary>Leader-only: allocates a raid instance for the requester's party.</summary>
        public Task<(RaidRule Rule, RaidInstanceRecord Instance)> AllocateRaidAsync(string requesterId)
            => _raidIntents.AllocateAsync(requesterId);

        /// <summary>Leader-only: joins the requester's party into an existing raid.</summary>
        public Task<RaidRule> JoinRaidAsync(string requesterId, string raidId)
            => _raidIntents.JoinAsync(requesterId, raidId);

        /// <summary>Leader-only: pulls the requester's whole party out of its raid.</summary>
        public Task<RaidRule> LeaveRaidAsync(string requesterId)
            => _raidIntents.LeaveAsync(requesterId);

        /// <summary>Raid-leader-only: hands raid leadership to another member.</summary>
        public Task<RaidRule> PromoteRaidLeaderAsync(string requesterId, string targetId)
            => _raidIntents.PromoteLeaderAsync(requesterId, targetId);

        /// <summary>Leader-only: completes the raid and tears its instance down.</summary>
        public Task<RaidRule> CompleteRaidAsync(string requesterId)
            => _raidIntents.CompleteAsync(requesterId);

        #endregion
    }
}
