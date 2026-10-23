using FishNet.Connection;
using FishNet.Object;
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
        /// Builds and binds this player's HUD, if this player is ours to describe.
        /// </summary>
        /// <returns>The HUD, or null when this object is a remote player or a dedicated server.</returns>
        public NetworkHud TryBuildHud()
        {
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
                createGuildBankFrame: false,
                crafting: GetComponent<PlayerCrafting>(),
                mount: GetComponent<PlayerMount>(),
                dialogue: GetComponent<PlayerDialogue>());

            _hud.BindChat(GetComponent<PlayerChat>(), GetComponent<PlayerFriends>());

            return _hud;
        }

        /// <summary>
        /// Destroys the HUD this object built, if any. A player that never built one leaves the
        /// screen alone.
        /// </summary>
        private void DestroyHud()
        {
            if (_hud == null)
                return;

            // Destroying the root runs NetworkHud.OnDestroy, which unbinds every screen first; the
            // reference is cleared so a later ownership change can build a fresh HUD.
            Destroy(_hud.gameObject);
            _hud = null;
        }
    }
}
