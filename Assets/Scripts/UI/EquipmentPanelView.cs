using System.Collections.Generic;
using MMO.Inventory;
using UnityEngine;
using UnityEngine.UI;

namespace MMO.UI
{
    /// <summary>
    /// The paper-doll window: one row per <see cref="EquipmentSlot"/> showing what is worn, with a
    /// right-click to unequip (the input path <see cref="EquipmentSlotView"/> already provided).
    ///
    /// Like the bag grid, the whole panel is one subscription to
    /// <see cref="IEquipmentSource.ContainersChanged"/>, so it repaints only when the server
    /// actually moves something. The rows are built from <see cref="EquipmentSlots.All"/>, so a
    /// future slot appears here without this view knowing its name.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("MMO/UI/Equipment Panel")]
    public sealed class EquipmentPanelView : MonoBehaviour
    {
        /// <summary>Row tint when the slot is empty.</summary>
        public static readonly Color EmptyRowColor = new(1f, 1f, 1f, 0.04f);

        /// <summary>Row tint when something is worn.</summary>
        public static readonly Color OccupiedRowColor = new(0.55f, 0.45f, 0.2f, 0.55f);

        /// <summary>Height of one slot row, in canvas units.</summary>
        public const float RowHeight = 32f;

        /// <summary>One built row: the slot, the unequip view, and the graphics a repaint touches.</summary>
        public sealed class Row
        {
            internal Row(EquipmentSlot slot, GameObject gameObject, EquipmentSlotView view, Image background, Text label)
            {
                Slot = slot;
                GameObject = gameObject;
                View = view;
                Background = background;
                Label = label;
            }

            public EquipmentSlot Slot { get; }
            public GameObject GameObject { get; }
            public EquipmentSlotView View { get; }
            public Image Background { get; }
            public Text Label { get; }
        }

        [SerializeField] private RectTransform _rowContainer;

        private readonly List<Row> _rows = new();
        private IEquipmentSource _source;

        /// <summary>The built rows, in <see cref="EquipmentSlots.All"/> order.</summary>
        public IReadOnlyList<Row> Rows => _rows;

        /// <summary>The source currently bound, or null.</summary>
        public IEquipmentSource Source => _source;

        /// <summary>Declares where the rows are parented; public so the factory can hand over a panel.</summary>
        public void SetContainer(RectTransform rowContainer) => _rowContainer = rowContainer;

        /// <summary>
        /// Binds the panel: unsubscribes the previous source, builds the rows on first use, points
        /// every row at the new source and repaints once.
        /// </summary>
        public void Bind(IEquipmentSource source)
        {
            if (_source != null)
                _source.ContainersChanged -= OnContainersChanged;

            _source = source;
            EnsureRows();

            for (int i = 0; i < _rows.Count; i++)
                _rows[i].View.SetInventory(source);

            if (_source != null)
                _source.ContainersChanged += OnContainersChanged;

            Repaint();
        }

        /// <summary>Unsubscribes from the bound source. The built rows stay on screen.</summary>
        public void Unbind()
        {
            if (_source != null)
                _source.ContainersChanged -= OnContainersChanged;

            _source = null;
        }

        /// <summary>Redraws every row from the source's current equipment.</summary>
        public void Repaint()
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                Row row = _rows[i];
                ItemInstance item = _source != null ? _source.GetEquipped(row.Slot) : ItemInstance.Empty;
                bool occupied = !item.IsEmpty;

                if (row.Background != null)
                    row.Background.color = occupied ? OccupiedRowColor : EmptyRowColor;

                if (row.Label != null)
                {
                    string slotName = SlotLabel(row.Slot);
                    row.Label.text = occupied ? $"{slotName}: {item.ItemName}" : slotName;
                }
            }
        }

        private void EnsureRows()
        {
            if (_rows.Count == EquipmentSlots.All.Count)
                return;

            ClearRows();

            for (int i = 0; i < EquipmentSlots.All.Count; i++)
                _rows.Add(CreateRow(EquipmentSlots.All[i], i));
        }

        private Row CreateRow(EquipmentSlot slot, int index)
        {
            // A GameObject holds one Graphic, so the row carries the background Image and the
            // label lives on a child that does not steal the right-click raycast.
            var rowObject = new GameObject($"{slot} Row", typeof(RectTransform), typeof(Image));

            RectTransform rect = (RectTransform)rowObject.transform;
            rect.SetParent(_rowContainer != null ? _rowContainer : transform, false);
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -index * RowHeight);
            rect.sizeDelta = new Vector2(0f, RowHeight - 2f);

            Image background = rowObject.GetComponent<Image>();
            background.color = EmptyRowColor;

            EquipmentSlotView view = rowObject.AddComponent<EquipmentSlotView>();
            view.SetSlot(slot);

            var labelObject = new GameObject("Label", typeof(RectTransform));
            RectTransform labelRect = (RectTransform)labelObject.transform;
            labelRect.SetParent(rowObject.transform, false);
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(8f, 0f);
            labelRect.offsetMax = new Vector2(-8f, 0f);

            Text label = labelObject.AddComponent<Text>();
            label.font = UIFont.Default;
            label.fontSize = 12;
            label.alignment = TextAnchor.MiddleLeft;
            label.color = Color.white;
            label.raycastTarget = false;
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 8;
            label.resizeTextMaxSize = 12;
            label.text = string.Empty;

            return new Row(slot, rowObject, view, background, label);
        }

        private void ClearRows()
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i]?.GameObject == null)
                    continue;

                if (Application.isPlaying)
                    Destroy(_rows[i].GameObject);
                else
                    DestroyImmediate(_rows[i].GameObject);
            }

            _rows.Clear();
        }

        private void OnContainersChanged() => Repaint();

        private void OnDestroy()
        {
            if (_source != null)
                _source.ContainersChanged -= OnContainersChanged;
        }

        private static string SlotLabel(EquipmentSlot slot) => slot switch
        {
            EquipmentSlot.MainHand => "Main Hand",
            EquipmentSlot.OffHand => "Off Hand",
            _ => slot.ToString(),
        };
    }
}
