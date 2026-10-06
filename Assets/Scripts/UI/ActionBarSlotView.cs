using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MMO.UI
{
    /// <summary>
    /// One slot of the action bar. A left-click asks the owning <see cref="ActionBarView"/> to
    /// forward a cast request for this slot — intent only, never a decision about whether the
    /// cast succeeds.
    ///
    /// Like every view input path in this project the handler is <c>virtual</c> (so a test can
    /// count clicks without a network round trip), null-event-safe (uGUI can dispatch a null
    /// <see cref="PointerEventData"/> in synthetic calls), and a no-op when the slot was never
    /// wired to a bar.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("MMO/UI/Action Bar Slot View")]
    public class ActionBarSlotView : MonoBehaviour, IPointerClickHandler
    {
        [SerializeField] private int _slotIndex;

        private ActionBarView _owner;
        private Text _label;

        /// <summary>The bar this slot reports clicks to, or null.</summary>
        public ActionBarView Owner => _owner;

        /// <summary>Zero-based index this slot casts.</summary>
        public int SlotIndex => _slotIndex;

        /// <summary>The label this slot displays, if any.</summary>
        public Text Label => _label;

        /// <summary>Points this slot at its bar; public so a bar can wire the slots it builds.</summary>
        public void SetOwner(ActionBarView owner) => _owner = owner;

        /// <summary>Declares which slot this view represents.</summary>
        public void SetSlot(int slotIndex) => _slotIndex = slotIndex;

        /// <summary>Points this slot at its label; public so a bar or a test can wire it.</summary>
        public void SetLabel(Text label) => _label = label;

        public void OnPointerClick(PointerEventData eventData)
        {
            // Left-click casts — right-click stays reserved for the inventory's equip gesture —
            // and a null event must not throw inside uGUI's dispatch.
            if (eventData == null || eventData.button != PointerEventData.InputButton.Left)
                return;

            HandleClick();
        }

        /// <summary>
        /// The cast request this view exists to make — virtual so a test can observe that a
        /// click reached it without a pointer event at all.
        /// </summary>
        public virtual void HandleClick()
        {
            if (_owner != null)
                _owner.HandleSlotClicked(_slotIndex);
        }
    }
}
