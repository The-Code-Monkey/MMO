using FishNet.Connection;
using FishNet.Object;
using MMO.Auction;
using MMO.Banking;
using MMO.Content;
using MMO.Crafting;
using MMO.Gameplay;
using MMO.Inventory;
using MMO.Social;
using MMO.UI;
using UnityEngine;

namespace MMO.Client
{
    /// <summary>
    /// The script that gives a client a HUD. Sits on the player prefab and, when the object it is
    /// on belongs to this client, builds <see cref="NetworkHudFactory.Create"/>'s screen and binds
    /// it to this player's own networked sources.
    ///
    /// <para>
    /// Every client receives every player, so this component exists on other people's capsules too.
    /// The <c>if (!IsOwner) return;</c> in <see cref="TryBuildHud"/> is the entire MMO logic of the
    /// feature: drop the check and every remote player would build a second HUD over the first,
    /// each one bound to somebody else's stats.
    /// </para>
    ///
    /// <para>
    /// The crafting panel the HUD carries is built concealed; it opens when the server sends this
    /// player's <see cref="PlayerCrafting"/> an offer payload, which is what clicking a
    /// <see cref="CraftingStation"/> triggers. That payload lists every recipe the station offers,
    /// affordable or not, so a player can see what a station makes before gathering the materials.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class LocalPlayerHud : NetworkBehaviour
    {
        private NetworkHud _hud;
        private PlayerBank _bank;
        private IBankSource _bankSource;
        private ClientInput _input;
        private ClientEffects _effects;
        private SettingsPanelView _settingsPanel;

        /// <summary>This client's HUD, or null when none was built (a remote player, or a server).</summary>
        public NetworkHud Hud => _hud;

        /// <summary>
        /// Builds the HUD when the object starts and is ours. Runs on every client, for every
        /// player, which is exactly why the ownership check inside matters.
        ///
        /// <para>
        /// A HUD failure must never escape this callback: FishNet invokes <c>OnStartNetwork</c>
        /// for every behaviour <em>before</em> invoking <c>OnStartClient</c>, so an exception
        /// here aborts the whole spawn chain — including <c>NetworkTransform.OnStartClient</c>,
        /// which leaves the transform unable to apply server updates and the player frozen.
        /// The HUD is cosmetic; the spawn chain is not.
        /// </para>
        /// </summary>
        public override void OnStartNetwork()
        {
            try
            {
                TryBuildHud();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[LocalPlayerHud] HUD build failed (non-fatal): {e}");
            }
        }

        /// <summary>
        /// Re-evaluates the HUD when ownership moves. FishNet raises this on the client that gains
        /// ownership as well as the ones that lose it, so one handler covers both directions:
        /// ownership arriving means the HUD is ours to build, and ownership leaving means it is
        /// ours to tear down.
        /// </summary>
        public override void OnOwnershipClient(NetworkConnection prevOwner)
        {
            if (IsOwner)
                TryBuildHud();
            else
                DestroyHud();
        }

        /// <summary>
        /// Tears the HUD down on the way out. Without this a disconnect would leave a screen bound
        /// to components that are being destroyed.
        /// </summary>
        public override void OnStopNetwork() => DestroyHud();

        /// <summary>
        /// The trade window hides itself while closed, so its own component cannot poll the key.
        /// The HUD stays active and forwards the press to the window's nearest-partner picker.
        /// </summary>
        private void Update()
        {
            if (_input != null && _input.WasPressed(GameAction.OpenSettings))
                _settingsPanel?.Toggle();

            if (_hud == null)
                return;

            if (_input != null)
            {
                for (int i = 0; i < GameActions.Abilities.Length; i++)
                {
                    if (_input.WasPressed(GameActions.Abilities[i]))
                        _hud.ActionBar?.HandleSlotClicked(i);
                }
            }

            if (_hud.TradeFrame == null)
                return;

            bool tradePressed = _input != null
                ? _input.WasPressed(GameAction.Trade)
                : Input.GetKeyDown(_hud.TradeFrame.OpenKey);

            if (tradePressed)
                _hud.TradeFrame.OpenNearest();

            // The mailbox window hides itself while closed, so the persistent HUD owns its key.
            if (_hud.MailFrame != null && Input.GetKeyDown(_hud.MailFrame.OpenKey))
                _hud.MailFrame.Toggle();
        }

        /// <summary>
        /// Builds and binds this player's HUD, if this player is ours to describe.
        /// </summary>
        /// <returns>The HUD, or null when this object is a remote player or a dedicated server.</returns>
        public NetworkHud TryBuildHud()        {
            // THE guard. Every client spawns every player's prefab, so this component runs for
            // every player in the world. Only the object this client owns may build a HUD;
            // without the check, joining as a second player stacks HUDs bound to the wrong stats.
            if (!IsOwner)
                return _hud;

            // A dedicated server is a server, not a client: it owns nothing local and has no screen
            // to draw on, so it must not build a HUD for itself.
            if (!IsClientStarted)
                return _hud;

            if (_hud != null)
                return _hud;

            _hud = NetworkHudFactory.Create(
                vitals: GetComponent<PlayerStats>(),
                inventory: GetComponent<PlayerInventory>(),
                abilities: GetComponent<PlayerAbilities>(),
                target: null,
                createEventSystem: true,
                createBankFrame: true,
                crafting: GetComponent<PlayerCrafting>(),
                mount: GetComponent<PlayerMount>(),
                dialogue: GetComponent<PlayerDialogue>(),
                createTradeFrame: true);

            // Presentation for every accepted cast this client sees, built on the owner's player
            // object so a dedicated server and remote copies never create a pool.
            _effects = ClientEffects.Ensure(transform);
            _input = ClientInput.Ensure(transform);
            _settingsPanel = SettingsPanelFactory.Create(_hud.transform, _input);

            // Ability clicks and hotkeys now aim at the mouse rather than the world origin.
            _hud.ActionBar?.SetCastTarget(AimPoint);

            _hud.BindChat(GetComponent<PlayerChat>(), GetComponent<PlayerFriends>());
            _hud.BindTrade(GetComponent<PlayerTrade>(), GetComponent<PlayerInventory>());
            _hud.MailFrame?.Bind(GetComponent<PlayerMail>());

            _bank = GetComponent<PlayerBank>();
            if (_bank != null)
                _bank.BankOpened += OnBankOpened;

            return _hud;
        }

        /// <summary>
        /// The server accepted a teller open: build the matching bank source and show the window.
        /// A guild teller also passes the guild so the rank editor appears.
        /// </summary>
        private Vector3 AimPoint()
        {
            PlayerClickToMove clickToMove = GetComponent<PlayerClickToMove>();
            return clickToMove != null
                ? clickToMove.AimPoint()
                : transform.position + transform.forward * 5f;
        }

        private void OnBankOpened(BankKind kind)
        {
            if (_hud == null)
                return;

            _bankSource?.Unsubscribe();

            PlayerInventory bag = GetComponent<PlayerInventory>();
            PlayerGuild guild = GetComponent<PlayerGuild>();

            if (kind == BankKind.Guild)
            {
                _bankSource = new GuildBankSource(guild);
                _hud.OpenBank(_bankSource, bag, guild);
            }
            else
            {
                _bankSource = new PersonalBankSource(_bank, GetComponent<PlayerWallet>());
                _hud.OpenBank(_bankSource, bag);
            }
        }

        /// <summary>
        /// Destroys the HUD this object built, if any. A player that never built one leaves the
        /// screen alone.
        /// </summary>
        private void DestroyHud()
        {
            if (_hud == null)
                return;

            if (_bank != null)
                _bank.BankOpened -= OnBankOpened;

            _bankSource?.Unsubscribe();
            _bankSource = null;

            _settingsPanel = null;
            _hud.MailFrame?.Unbind();

            if (_input != null)
            {
                Destroy(_input.gameObject);
                _input = null;
            }

            if (_effects != null)
            {
                Destroy(_effects.gameObject);
                _effects = null;
            }

            // Destroying the root runs NetworkHud.OnDestroy, which unbinds every screen first; the
            // reference is cleared so a later ownership change can build a fresh HUD.
            Destroy(_hud.gameObject);
            _hud = null;
        }
    }
}
