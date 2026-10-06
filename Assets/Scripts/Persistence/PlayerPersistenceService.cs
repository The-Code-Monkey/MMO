using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FishNet.Managing;
using FishNet.Transporting;
using MMO.Net;
using UnityEngine;

namespace MMO.Persistence
{
    /// <summary>
    /// Server-side owner of the database connection and the per-player save loop. Lives on the
    /// "NetworkManager" GameObject next to the join gate (wired by
    /// <see cref="Net.NetworkManagerFactory"/>), connects to the backend when the server starts, and
    /// owns everything a player save touches:
    ///
    /// <list type="bullet">
    /// <item>the <see cref="IPlayerLoader"/> the gate awaits during the join handshake, plus the
    /// clientId → accountId → record cache that hands a loaded record to the player object when it
    /// spawns;</item>
    /// <item>a debounced dirty queue driven by the gameplay events in <see cref="PlayerSaveTrigger"/>
    /// (level up saves immediately, container changes are debounced to avoid write storms), with the
    /// same 30&nbsp;s autosave cadence the old file writer had as a safety net;</item>
    /// <item>a full flush on server stop and on quit, so a disconnect or shutdown never loses the
    /// last state.</item>
    /// </list>
    ///
    /// If the backend is unreachable the server still starts — joins load fresh records and writes
    /// are queued but fail open — and the optimistic version check this service relies on keeps a
    /// reconnected backend from ever being clobbered by a stale local snapshot.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("MMO/Persistence/Player Persistence Service")]
    public sealed class PlayerPersistenceService : MonoBehaviour, IPlayerLoader
    {
        /// <summary>Only one service per process; triggers and the gate find it through this.</summary>
        public static PlayerPersistenceService Instance { get; private set; }

        [SerializeField, Min(0f)] private float _autosaveInterval = 30f;
        [SerializeField, Min(0f)] private float _saveDebounce = 2f;

        private readonly Dictionary<int, string> _accountByClientId = new();
        private readonly Dictionary<string, PlayerSaveRecord> _spawnRecords = new();
        private readonly Dictionary<string, Func<PlayerSaveRecord>> _livePlayers = new();
        private readonly Dictionary<string, PlayerSaveRecord> _finalRecords = new();
        private readonly HashSet<string> _dirty = new();
        private readonly Dictionary<string, double> _debounceUntil = new();

        private IPlayerRepository _repository;
        private bool _connected;
        private bool _saving;
        private float _nextAutosaveTime;
        private NetworkManager _manager;
        private bool _subscribed;

        private void Awake()
        {
            Instance = this;
            if (_repository == null)
                Configure(new NakamaPlayerRepository(new BackendSettings()));
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        private void Start()
        {
            _manager = GetComponentInParent<NetworkManager>();
            _nextAutosaveTime = Time.time + _autosaveInterval;
        }

        /// <summary>
        /// Points the service at a repository. Production reaches the Nakama-backed default in
        /// <see cref="Awake"/>; tests inject an in-memory fake here. A repository with no connection
        /// step (i.e. not an <see cref="INakamaConnected"/>) counts as immediately connected.
        /// </summary>
        public void Configure(IPlayerRepository repository)
        {
            _repository = repository ?? new NakamaPlayerRepository(new BackendSettings());
            _connected = !(_repository is INakamaConnected);
        }

        private void Update()
        {
            if (_manager == null)
                _manager = GetComponentInParent<NetworkManager>();

            if (_manager != null && !_subscribed && _manager.ServerManager != null)
                SubscribeToServerEvents();

            // Autosave safety net: touch every live player on the cadence even when nothing was
            // marked dirty, mirroring the old file writer's periodic full save.
            if (_connected && _livePlayers.Count > 0 && _autosaveInterval > 0f &&
                Time.time >= _nextAutosaveTime)
            {
                foreach (string accountId in _livePlayers.Keys)
                    MarkDirty(accountId, immediate: false);

                _nextAutosaveTime = Time.time + _autosaveInterval;
            }

            if (_connected && _dirty.Count > 0 && !_saving)
                _ = ProcessDueSavesAsync(Time.unscaledTimeAsDouble);
        }

        private void SubscribeToServerEvents()
        {
            _manager.ServerManager.OnServerConnectionState += OnServerConnectionState;
            _subscribed = true;

            if (_manager.IsServerStarted)
                _ = ConnectAsync();
        }

        private void OnServerConnectionState(ServerConnectionStateArgs args)
        {
            if (args.ConnectionState == LocalConnectionState.Started)
            {
                _ = ConnectAsync();
            }
            else if (args.ConnectionState == LocalConnectionState.Stopped)
            {
                // Must not destroy the server session before every dirty player is written, so
                // flush first, then drop the session.
                _ = StopAsync();
            }
        }

        private async Task ConnectAsync()
        {
            if (_connected)
                return;

            try
            {
                if (_repository is INakamaConnected nakama)
                    await nakama.ConnectAsync(CancellationToken.None);

                _connected = true;
                Debug.Log("[Persistence] Connected to the backend.");
            }
            catch (Exception e)
            {
                // Fails open: joins proceed with fresh records, and every write's optimistic
                // version check keeps them from ever overwriting a real save once the backend is
                // reachable again.
                Debug.LogError($"[Persistence] Backend unavailable; players will load from defaults. {e.Message}");
                _connected = false;
            }
        }

        private async Task StopAsync()
        {
            await FlushAsync();
            if (_repository is INakamaConnected nakama)
                await nakama.DisconnectAsync();

            _connected = false;
        }

        private void OnDisable()
        {
            if (_manager != null && _manager.ServerManager != null)
                _manager.ServerManager.OnServerConnectionState -= OnServerConnectionState;

            _subscribed = false;
        }

        #region IPlayerLoader + join-handshake cache

        /// <summary>
        /// Loads a player's record during the join handshake. Returns null (a fresh player) when the
        /// backend is not connected or the read fails, rather than failing the join.
        /// </summary>
        public async Task<PlayerSaveRecord> LoadAsync(string accountId, CancellationToken ct)
        {
            if (!_connected || string.IsNullOrEmpty(accountId))
                return null;

            try
            {
                return await _repository.LoadAsync(accountId, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Persistence] Failed to load '{accountId}'; spawning fresh. {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Attaches this service to the join gate: becomes its loader and starts receiving
        /// (clientId, accountId, record) once each handshake resolves.
        /// </summary>
        public void AttachTo(MMOConnectionGate gate)
        {
            if (gate == null)
                return;

            gate.PlayerLoader = this;
            gate.PlayerJoinLoaded += OnJoinLoaded;
        }

        private void OnJoinLoaded(int clientId, string accountId, PlayerSaveRecord record)
        {
            _accountByClientId[clientId] = accountId;
            _spawnRecords[accountId] = record;
        }

        /// <summary>
        /// Hands the spawn-time player object its account id and loaded record. Both are kept (not
        /// consumed) so PlayerStats and PlayerInventory inside one player can each read the same
        /// record without caring which runs first.
        /// </summary>
        public bool TryGetJoin(int clientId, out string accountId, out PlayerSaveRecord record)
        {
            accountId = null;
            record = null;

            if (!_accountByClientId.TryGetValue(clientId, out accountId))
                return false;

            _spawnRecords.TryGetValue(accountId, out record);
            return true;
        }

        /// <summary>Forgets a disconnected client's join cache.</summary>
        public void ForgetClient(int clientId)
        {
            if (_accountByClientId.TryGetValue(clientId, out string accountId))
            {
                _accountByClientId.Remove(clientId);
                _spawnRecords.Remove(accountId);
            }
        }

        #endregion

        #region Live players + dirty queue

        /// <summary>
        /// Registers a live, spawned player under its account so the save loop can capture its state
        /// on demand.
        /// </summary>
        public void RegisterLivePlayer(string accountId, Func<PlayerSaveRecord> capture)
            => _livePlayers[accountId] = capture ?? throw new ArgumentNullException(nameof(capture));

        public void UnregisterLivePlayer(string accountId)
            => _livePlayers.Remove(accountId);

        /// <summary>
        /// Marks a player dirty. <paramref name="immediate"/> skips the debounce window (used for
        /// level-ups and disconnects); otherwise the save waits out <see cref="_saveDebounce"/> so a
        /// burst of inventory changes flushes once.
        /// </summary>
        public void MarkDirty(string accountId, bool immediate)
        {
            if (string.IsNullOrEmpty(accountId))
                return;

            _dirty.Add(accountId);
            _debounceUntil[accountId] = immediate
                ? Time.unscaledTimeAsDouble
                : Time.unscaledTimeAsDouble + _saveDebounce;
        }

        /// <summary>
        /// Queues the final state of a player that is about to leave (disconnect / despawn). The
        /// snapshot is captured now, while its SyncVars are still live, and takes priority over the
        /// registered live capture for the one save that follows.
        /// </summary>
        public void QueueFinalSave(string accountId, PlayerSaveRecord record)
        {
            if (string.IsNullOrEmpty(accountId) || record == null)
                return;

            _finalRecords[accountId] = record;
            MarkDirty(accountId, immediate: true);
        }

        /// <summary>
        /// Saves every dirty player whose debounce window has passed. Public so tests can drive the
        /// queue with a fake clock instead of waiting real frames.
        /// </summary>
        public async Task<int> ProcessDueSavesAsync(double now)
        {
            if (_saving || !_connected || _dirty.Count == 0)
                return 0;

            List<string> due = null;
            foreach (string accountId in _dirty)
            {
                if (_debounceUntil.TryGetValue(accountId, out double deadline) && now >= deadline)
                    (due ??= new List<string>()).Add(accountId);
            }

            if (due == null)
                return 0;

            int flushed = 0;
            _saving = true;
            try
            {
                foreach (string accountId in due)
                {
                    bool ok = await SaveOneAsync(accountId);
                    if (ok)
                    {
                        _dirty.Remove(accountId);
                        _debounceUntil.Remove(accountId);
                        flushed++;
                    }
                    else
                    {
                        // Keep dirty for a later retry; re-debounce so a dead backend cannot
                        // hot-loop the save loop.
                        _dirty.Add(accountId);
                        _debounceUntil[accountId] = Time.unscaledTimeAsDouble + _saveDebounce;
                    }
                }
            }
            finally
            {
                _saving = false;
            }

            return flushed;
        }

        /// <summary>
        /// Force-saves every dirty player now, ignoring debounce — the shutdown path.
        /// </summary>
        public async Task<int> FlushAsync()
        {
            if (_repository == null)
                return 0;

            int flushed = 0;
            foreach (string accountId in new List<string>(_dirty))
            {
                if (await SaveOneAsync(accountId))
                {
                    _dirty.Remove(accountId);
                    _debounceUntil.Remove(accountId);
                    flushed++;
                }
                else
                {
                    _dirty.Add(accountId);
                }
            }

            return flushed;
        }

        private async Task<bool> SaveOneAsync(string accountId)
        {
            if (!_connected || string.IsNullOrEmpty(accountId))
                return true;

            PlayerSaveRecord record;
            if (_finalRecords.TryGetValue(accountId, out record))
            {
                _finalRecords.Remove(accountId);
            }
            else
            {
                record = null;
                if (_livePlayers.TryGetValue(accountId, out Func<PlayerSaveRecord> capture))
                {
                    try
                    {
                        record = capture();
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[Persistence] Failed to capture '{accountId}': {e.Message}");
                        return false;
                    }
                }
            }

            if (record == null)
                return true;

            try
            {
                await _repository.SaveAsync(record, CancellationToken.None);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Persistence] Failed to save '{accountId}': {e.Message}");
                return false;
            }
        }

        #endregion

        private async void OnApplicationQuit()
        {
            // Best-effort final flush; ConnectionButtons already triggers one before the transport
            // stops, so this is only a safety net for hard quits.
            if (_connected)
                await StopAsync();
        }
    }
}