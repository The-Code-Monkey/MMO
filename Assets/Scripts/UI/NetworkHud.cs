using MMO.Gameplay;
using MMO.Inventory;
using UnityEngine;

namespace MMO.UI
{
    /// <summary>
    /// The root of the client HUD: the four screens built by
    /// <see cref="NetworkHudFactory"/> and the bind points gameplay code drives.
    ///
    /// <para>
    /// Binding is deliberately explicit and one-way. Whoever owns the local player's spawned
    /// components calls <see cref="BindPlayer"/> once they exist; whoever selects a target calls
    /// <see cref="BindTarget"/>. From then on every repaint on screen is a response to a
    /// FishNet change callback — this component runs no <c>Update</c>, and neither does anything
    /// it holds.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("MMO/UI/Network HUD")]
    public sealed class NetworkHud : MonoBehaviour
    {
        private PlayerHudView _playerHud;
        private TargetFrameView _targetFrame;
        private InventoryGridView _inventoryGrid;
        private ActionBarView _actionBar;

        /// <summary>The health/mana pair, or null before views were assigned.</summary>
        public PlayerHudView PlayerHud => _playerHud;

        /// <summary>The target frame, or null before views were assigned.</summary>
        public TargetFrameView TargetFrame => _targetFrame;

        /// <summary>The bag grid, or null before views were assigned.</summary>
        public InventoryGridView InventoryGrid => _inventoryGrid;

        /// <summary>The action bar, or null before views were assigned.</summary>
        public ActionBarView ActionBar => _actionBar;

        /// <summary>Hands the assembled screens to the HUD; called by the factory.</summary>
        public void SetViews(
            PlayerHudView playerHud,
            TargetFrameView targetFrame,
            InventoryGridView inventoryGrid,
            ActionBarView actionBar)
        {
            _playerHud = playerHud;
            _targetFrame = targetFrame;
            _inventoryGrid = inventoryGrid;
            _actionBar = actionBar;
        }

        /// <summary>
        /// Binds the player's screens to their networked sources. Any argument may be null — a
        /// screen bound to nothing simply stays quiet until a later call binds it, so a HUD can
        /// exist before the object it describes has spawned.
        /// </summary>
        public void BindPlayer(
            IVitalsSource vitals,
            IInventorySource inventory = null,
            IAbilityBarSource abilities = null)
        {
            if (_playerHud != null)
                _playerHud.Bind(vitals);

            if (_inventoryGrid != null)
                _inventoryGrid.Bind(inventory);

            if (_actionBar != null)
                _actionBar.Bind(abilities);
        }

        /// <summary>Shows <paramref name="target"/> in the frame; null hides the frame.</summary>
        public void BindTarget(ITargetSource target)
        {
            if (_targetFrame != null)
                _targetFrame.Bind(target);
        }

        /// <summary>Detaches every screen from every source. The HUD stops repainting entirely.</summary>
        public void Unbind()
        {
            if (_playerHud != null)
                _playerHud.Unbind();

            if (_targetFrame != null)
                _targetFrame.Unbind();

            if (_inventoryGrid != null)
                _inventoryGrid.Unbind();

            if (_actionBar != null)
                _actionBar.Unbind();
        }

        private void OnDestroy() => Unbind();
    }
}
