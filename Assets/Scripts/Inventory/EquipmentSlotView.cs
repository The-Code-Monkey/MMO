using UnityEngine;
using UnityEngine.EventSystems;

namespace MMO.Inventory
{
    /// <summary>
    /// One slot of a paper-doll view (Main Hand, Off Hand, Head, Chest, Feet). A right-click
    /// asks the server to unequip whatever is worn there — the mirror image of
    /// <see cref="InventorySlotView"/>'s equip request.
    ///
    /// Like its bag counterpart it holds no authority: it names a slot, not an item, and the
    /// server decides whether anything is there and whether it has bag space to come home to.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("MMO/Inventory/Equipment Slot View")]
    public class EquipmentSlotView : MonoBehaviour, IPointerClickHandler
    {
        private IEquipmentSource _inventory;
        [SerializeField] private EquipmentSlot _slot;

        public IEquipmentSource Inventory => _inventory;
        public EquipmentSlot Slot => _slot;

        /// <summary>Points this view at a specific equipment source; public for runtime wiring and tests.</summary>
        public void SetInventory(IEquipmentSource inventory) => _inventory = inventory;

        /// <summary>Declares which equipment slot this view represents.</summary>
        public void SetSlot(EquipmentSlot slot) => _slot = slot;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData == null || eventData.button != PointerEventData.InputButton.Right)
                return;

            HandleRightClick();
        }

        /// <summary>
        /// The unequip request this view exists to make — virtual so a test can observe that a
        /// right-click reached it; public so tests can drive it without a pointer event.
        /// </summary>
        public virtual void HandleRightClick()
        {
            // An interface reference cannot use Unity's null overload; a destroyed inventory
            // behind it would otherwise look alive and throw.
            if (_inventory == null || (_inventory is UnityEngine.Object unityObject && unityObject == null))
                return;

            _inventory.RequestUnequip(_slot);
        }
    }
}
