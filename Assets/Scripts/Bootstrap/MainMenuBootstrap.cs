using FishNet.Managing;
using FishNet.Transporting;
using MMO.Net;
using MMO.Persistence;
using MMO.UI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MMO.Bootstrap
{
    /// <summary>
    /// The scene glue for the Main Menu: builds the whole menu with
    /// <see cref="MainMenuFactory"/>, wires it to a <see cref="LoginConnector"/>, and replies to the
    /// login form, to <see cref="LoginConnector.StatusChanged"/>, and to the server's join
    /// rejections (<see cref="JoinResultBroadcast"/>, which no other client code listens to).
    /// Lives in the Main Menu scene next to the NetworkManager.
    ///
    /// <para>
    /// Lazy by design: <see cref="Start"/> wires everything it can find when the scene runs, while
    /// <see cref="Initialize"/> lets a test inject a fake gateway and/or a real view and still get
    /// the same wiring. Whatever starts first wins, and wiring is idempotent either way.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("MMO/Bootstrap/Main Menu Bootstrap")]
    public sealed class MainMenuBootstrap : MonoBehaviour
    {
        [SerializeField] private NetworkManager _networkManager;
        [SerializeField] private string _defaultZoneIp = "127.0.0.1";
        [SerializeField] private ushort _defaultZonePort = MMOConnectionSettings.DefaultPort;

        private LoginView _view;
        private IApiGateway _gateway;
        private LoginConnector _connector;
        private bool _wired;

        public LoginView View => _view;
        public LoginConnector Connector => _connector;

        public void SetNetworkManager(NetworkManager manager) => _networkManager = manager;

        /// <summary>
        /// The zone to join when the player has no stored zone yet (first-ever login). Serialized
        /// on the scene object; the editor baker stamps deployment values here rather than in code.
        /// </summary>
        public void SetDefaultZone(string ip, ushort port)
        {
            _defaultZoneIp = string.IsNullOrWhiteSpace(ip) ? "127.0.0.1" : ip;
            _defaultZonePort = port;
        }

        /// <summary>
        /// Injects the pieces a live scene normally discovers on its own. Calling it before the
        /// first frame lets a test drive the full flow without the scene's own menu or backend.
        /// </summary>
        public void Initialize(LoginView view, IApiGateway gateway)
        {
            _view = view;
            _gateway = gateway ?? new NakamaApiGateway();
            EnsureWired();
        }

        private void Start()
        {
            if (TryRedirectToZone())
                return;

            if (_networkManager == null)
                _networkManager = FindFirstObjectByType<NetworkManager>();

            if (_networkManager == null)
            {
                Debug.LogError($"{nameof(MainMenuBootstrap)}: no NetworkManager found in the scene.");
                return;
            }

            _gateway ??= new NakamaApiGateway();
            EnsureWired();
        }

        /// <summary>
        /// When the process is launched with <c>-zone &lt;name&gt;</c> this loads the named zone
        /// scene and skips all menu wiring — the binary is acting as a dedicated zone server, not
        /// a client. The zone scene's own bootstrap then starts the FishNet listener.
        /// </summary>
        private bool TryRedirectToZone()
        {
            string zone = DedicatedServerArgs.ParseZone(System.Environment.GetCommandLineArgs());
            if (zone == null)
                return false;

            if (!ApplicationCanLoadZone(zone))
            {
                Debug.LogError($"{nameof(MainMenuBootstrap)}: unknown zone '{zone}'. " +
                               "Expected one of: SafeZone, Wilderness, Tutorial.");
                Application.Quit(1);
                return true;
            }

            DedicatedServerMode.Enable(zone);

            // This process is a dedicated server, not a client. The main menu is a purely
            // client-side login surface, and its NetworkManager is marked DontDestroyOnLoad — so
            // it would survive into the zone scene and leave two NetworkManagers (and two
            // transports) alive at once, which stops the zone's own listener from ever starting.
            // Tear it down here; the zone scene owns its server from this point on.
            DestroyMainMenuNetworkManager();

            Debug.Log($"[DedicatedServer] Loading zone scene '{zone}'.");
            SceneManager.LoadScene(zone);
            return true;
        }

        /// <summary>
        /// Destroys the main menu's NetworkManager GameObject when the process is acting as a
        /// dedicated server. Client runs never call this — the menu keeps its manager to connect
        /// to a zone after login. Nulls the field first so <see cref="OnDestroy"/> does not touch
        /// a destroyed manager when the menu scene unloads.
        /// </summary>
        private void DestroyMainMenuNetworkManager()
        {
            if (_networkManager == null)
                return;

            NetworkManager toDestroy = _networkManager;
            _networkManager = null;
            Destroy(toDestroy.gameObject);
        }

        private static bool ApplicationCanLoadZone(string zone)
        {
            for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
            {
                string path = SceneUtility.GetScenePathByBuildIndex(i);
                string name = System.IO.Path.GetFileNameWithoutExtension(path);
                if (name.Equals(zone, System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private void EnsureWired()
        {
            if (_wired)
                return;

            if (_networkManager == null)
                return;

            _view ??= MainMenuFactory.Create();

            _connector = new LoginConnector(_networkManager, _gateway);
            _view.LoginSubmitted += OnLoginSubmitted;
            _connector.StatusChanged += SetStatus;

            // Nobody else on the client listens for the join gate's rejection broadcast; the menu
            // is the one screen that needs it. Must be registered before the connection starts, so
            // like every Start-time subscription here.
            _networkManager.ClientManager.RegisterBroadcast<JoinResultBroadcast>(OnJoinResultBroadcast);

            _wired = true;
        }

        private void OnLoginSubmitted(string username, string password)
            => _ = HandleLoginAsync(username, password);

        private async System.Threading.Tasks.Task HandleLoginAsync(string username, string password)
        {
            _view.SetBusy(true);
            try
            {
                var fallback = string.IsNullOrWhiteSpace(_defaultZoneIp)
                    ? null
                    : LastZoneInfo.Create(_defaultZoneIp, _defaultZonePort);
                await _connector.LoginAndConnectAsync(username, password, fallback);
            }
            finally
            {
                _view.SetBusy(false);
            }
        }

        private void OnJoinResultBroadcast(JoinResultBroadcast message, Channel channel)
        {
            if (!message.Approved)
            {
                // The server's refusal reason must reach the log: the disconnect that follows
                // overwrites the status line with the generic "could not reach" message, so this
                // is the only place the real cause survives.
                Debug.LogWarning($"[JoinGate] Zone refused the connection: {message.Reason}");
                SetStatus(message.Reason ?? "The zone refused the connection.");
            }
        }

        private void SetStatus(string message) => _view?.SetStatus(message);

        private void OnDestroy()
        {
            if (_connector != null)
            {
                _connector.StatusChanged -= SetStatus;
                _connector = null;
            }

            if (_view != null)
            {
                _view.LoginSubmitted -= OnLoginSubmitted;
                _view = null;
            }

            if (_networkManager != null && _networkManager.ClientManager != null)
                _networkManager.ClientManager.UnregisterBroadcast<JoinResultBroadcast>(OnJoinResultBroadcast);
        }
    }
}