using System;
using System.Collections.Generic;
using MMO.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace MMO.UI
{
    /// <summary>
    /// The action bar: one clickable slot per ability, labelled from the replicated slot names
    /// and wired to cast on click.
    ///
    /// <para>
    /// Labels are event-driven like everything else in this namespace: <see cref="Bind"/>
    /// subscribes to the source's <c>SlotsChanged</c> event — raised by the networked
    /// implementation's six <c>SyncVar.OnChange</c> callbacks — and <see cref="Repaint"/> reads
    /// the names back through <see cref="IAbilityBarSource.GetSlotName"/>. Equipment reshaping
    /// the bar therefore relabels it the moment the server says so, and an untouched bar
    /// repaints nothing at all.
    /// </para>
    ///
    /// <para>
    /// Slots are built on bind, because only the source knows how wide it is. Clicks are intent,
    /// not action: a slot asks its owner view to forward
    /// <see cref="IAbilityBarSource.RequestCast"/>, and the server stays the only thing that
    /// decides whether a cast happens. Cooldown overlays are deliberately absent — readiness is
    /// server-side state that is not replicated, and a bar that guessed at it would be showing
    /// invented numbers.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("MMO/UI/Action Bar View")]
    public class ActionBarView : MonoBehaviour
    {
        /// <summary>Edge length of one slot, in canvas units.</summary>
        public const float SlotSize = 48f;

        /// <summary>Background tint of every slot.</summary>
        public static readonly Color SlotColor = new(0.1f, 0.1f, 0.12f, 0.75f);

        /// <summary>
        /// One built slot: its index, the view that answers clicks, and the graphics the repaint
        /// touches.
        /// </summary>
        public sealed class Slot
        {
            internal Slot(int index, ActionBarSlotView view, Image background, Text label)
            {
                Index = index;
                View = view;
                Background = background;
                Label = label;
            }

            /// <summary>Zero-based slot index this entry represents.</summary>
            public int Index { get; }

            /// <summary>The view that turns a left-click into a cast request.</summary>
            public ActionBarSlotView View { get; }

            /// <summary>The slot background.</summary>
            public Image Background { get; }

            /// <summary>The label painted with the slot's ability name.</summary>
            public Text Label { get; }
        }

        [SerializeField] private RectTransform _slotContainer;

        private readonly List<Slot> _slots = new();
        private IAbilityBarSource _source;
        private Func<Vector3> _castTarget = DefaultCastTarget;

        /// <summary>The transform built slots are parented to, when one was set.</summary>
        public RectTransform SlotContainer => _slotContainer;

        /// <summary>The built slots, left to right.</summary>
        public IReadOnlyList<Slot> Slots => _slots;

        /// <summary>The source currently bound, or null.</summary>
        public IAbilityBarSource Source => _source;

        /// <summary>Declares where slots are parented; public so a factory can hand over a panel.</summary>
        public void SetContainer(RectTransform slotContainer) => _slotContainer = slotContainer;

        /// <summary>
        /// Where a clicked slot aims: a provider called at click time, so the aim point is the
        /// live one rather than whatever it was when the bar was built. Null resets the default.
        /// </summary>
        public void SetCastTarget(Func<Vector3> provider) => _castTarget = provider ?? DefaultCastTarget;

        /// <summary>
        /// Binds the bar: unsubscribes any previous source, subscribes to
        /// <c>SlotsChanged</c>, builds the slots the source declares (keeping existing ones when
        /// the count already matches) and repaints once. Binding null is an unbind.
        /// </summary>
        public void Bind(IAbilityBarSource source)
        {
            if (_source != null)
                _source.SlotsChanged -= OnSlotsChanged;

            _source = source;

            if (_source != null)
            {
                _source.SlotsChanged += OnSlotsChanged;
                EnsureSlots();
            }

            Repaint();
        }

        /// <summary>Unsubscribes from the bound source. Built slots stay on screen as they were.</summary>
        public void Unbind()
        {
            if (_source != null)
                _source.SlotsChanged -= OnSlotsChanged;

            _source = null;
        }

        /// <summary>
        /// Redraws every slot label from the source. Public and virtual so a test can count
        /// repaints and prove they only come from <see cref="Bind"/> or a change event.
        /// </summary>
        public virtual void Repaint()
        {
            if (_source == null)
                return;

            for (int i = 0; i < _slots.Count; i++)
            {
                Text label = _slots[i].Label;
                if (label != null)
                    label.text = _source.GetSlotName(i) ?? string.Empty;
            }
        }

        /// <summary>
        /// Forwards a slot click as a cast intent at the current aim point. Virtual so a test
        /// can watch clicks arrive without synthesizing a pointer event, and guarded so an
        /// unbound bar, an out-of-range index, or a source whose GameObject has since been
        /// destroyed is a no-op rather than an exception inside uGUI's dispatch.
        /// </summary>
        public virtual void HandleSlotClicked(int slot)
        {
            if (_source == null || slot < 0 || slot >= _source.SlotCount)
                return;

            if (_source is UnityEngine.Object unityObject && unityObject == null)
                return;

            _source.RequestCast(slot, _castTarget());
        }

        /// <summary>Builds the slot row for the bound source's width, if the geometry differs.</summary>
        private void EnsureSlots()
        {
            int count = _source.SlotCount;

            if (count < 1)
                return;

            if (_slots.Count == count)
                return;

            ClearSlots();

            for (int i = 0; i < count; i++)
                _slots.Add(CreateSlot(i));
        }

        private Slot CreateSlot(int index)
        {
            // A GameObject can carry only one Graphic, so the background Image and the label
            // cannot share this transform — the label gets its own child, which draws on top.
            var slotObject = new GameObject(
                $"Slot {index}", typeof(RectTransform), typeof(Image));

            RectTransform rect = (RectTransform)slotObject.transform;
            rect.SetParent(_slotContainer != null ? _slotContainer : transform, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(index * SlotSize, 0f);
            rect.sizeDelta = new Vector2(SlotSize, SlotSize);

            Image background = slotObject.GetComponent<Image>();
            background.color = SlotColor;

            var labelObject = new GameObject("Label", typeof(RectTransform));
            RectTransform labelRect = (RectTransform)labelObject.transform;
            labelRect.SetParent(slotObject.transform, false);
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            Text label = labelObject.AddComponent<Text>();
            label.font = UIFont.Default;
            label.fontSize = 11;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.white;
            label.raycastTarget = false;
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 6;
            label.resizeTextMaxSize = 14;
            label.text = string.Empty;

            ActionBarSlotView view = slotObject.AddComponent<ActionBarSlotView>();
            view.SetOwner(this);
            view.SetSlot(index);
            view.SetLabel(label);

            return new Slot(index, view, background, label);
        }

        private void ClearSlots()
        {
            for (int i = 0; i < _slots.Count; i++)
            {
                ActionBarSlotView view = _slots[i].View;
                if (view != null)
                    ViewObject.Destroy(view.gameObject);
            }

            _slots.Clear();
        }

        private static Vector3 DefaultCastTarget() => Vector3.zero;

        private void OnSlotsChanged() => Repaint();

        private void OnDestroy() => Unbind();
    }
}
