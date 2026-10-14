using MMO.Auction;
using MMO.UI;
using UnityEngine;
using UnityEngine.EventSystems;

namespace MMO.Inventory
{
    /// <summary>
    /// One cell of a bag view. Wires the cell's coordinates to an <see cref="IInventorySource"/>
    /// and turns a right-click into the owner-side equip request — the issue's
    /// "right-click to equip" path.
    ///
    /// The component owns no rules: it does not check whether the cell is occupied, whether the
    /// wearer is high enough level, or whether the slot is free, because the server answers all
    /// of those questions itself and its answer replicates back through the SyncList. A right
    /// -click on an empty cell is simply a request the server declines.
    ///
    /// Coordinates are stored rather than an item reference: by the time the server reads them
    /// the bag may have shifted, and <c>PlayerInventory.TryEquip</c> re-resolves the cell against
    /// its own authoritative list, so a stale click can never move the wrong item.
    ///
    /// The source is held as the interface rather than the concrete component, so a grid screen
    /// can wire its cells to whatever backs it — the networked inventory in a live game, a plain
    /// fake in a test. The reference is code-wired through <see cref="SetInventory"/>; Unity
    /// cannot serialize an interface field, and cells are built at runtime by the grid view.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("MMO/Inventory/Inventory Slot View")]
    public class InventorySlotView : MonoBehaviour, IPointerClickHandler
    {
        private IInventorySource _inventory;
        [SerializeField] private int _gridX;
        [SerializeField] private int _gridY;

        public IInventorySource Inventory => _inventory;
        public int GridX => _gridX;
        public int GridY => _gridY;

        /// <summary>
        /// Points this view at a specific inventory; public so a bag screen can populate cells
        /// at runtime and so tests can wire a fixture without going through the Inspector.
        /// </summary>
        public void SetInventory(IInventorySource inventory) => _inventory = inventory;

        /// <summary>Declares which bag cell this view represents.</summary>
        public void SetCell(int gridX, int gridY)
        {
            _gridX = gridX;
            _gridY = gridY;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            // Only right-click equips; a left-click is left free for selection/drag later, and a
            // null event (possible from synthetic calls) must not throw inside uGUI's dispatch.
            if (eventData == null || eventData.button != PointerEventData.InputButton.Right)
                return;

            bool shiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (shiftHeld)
            {
                HandleAuctionListing();
            }
            else
            {
                HandleRightClick();
            }
        }

        /// <summary>
        /// The equip request this view exists to make — virtual so a test can observe that a
        /// right-click reached it without synthesizing a full network round trip, and separately
        /// callable so tests can drive it without a pointer event at all.
        /// </summary>
        public virtual void HandleRightClick()
        {
            // An interface reference cannot use Unity's null overload, so a destroyed inventory
            // behind it would otherwise look alive and throw when the request touches the
            // despawned NetworkBehaviour. Check both null states before asking for anything.
            if (_inventory == null || (_inventory is UnityEngine.Object unityObject && unityObject == null))
                return;

            _inventory.RequestEquip(_gridX, _gridY);
        }

        /// <summary>
        /// Opens the auction house create listing panel for this cell (Shift+RightClick).
        /// </summary>
        public virtual void HandleAuctionListing()
        {
            if (_inventory == null || (_inventory is UnityEngine.Object unityObject && unityObject == null))
                return;

            AuctionHouseOpener opener = FindObjectOfType<AuctionHouseOpener>();
            if (opener != null)
            {
                opener.OpenCreateListing(_gridX, _gridY);
            }
        }
    }
}
