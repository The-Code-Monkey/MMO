using System.Collections.Generic;
using MMO.Inventory;
using UnityEngine;
using UnityEngine.UI;

namespace MMO.UI
{
    /// <summary>
    /// The inventory grid: one cell per bag square, each carrying an
    /// <see cref="InventorySlotView"/> so a right-click still reaches the server, and a label the
    /// grid paints item names into.
    ///
    /// <para>
    /// The whole screen is one subscription. <see cref="Bind"/> attaches to the source's
    /// <c>ContainersChanged</c> event — which FishNet's <c>SyncList.OnChange</c> callbacks raise
    /// through <see cref="PlayerInventory"/> — and <see cref="Repaint"/> redraws the labels from
    /// the live list contents. There is no polling anywhere: a bag nobody is touching repaints
    /// zero times per frame, however long the player stares at it.
    /// </para>
    ///
    /// <para>
    /// Cells are built on bind, because only the source knows how big its grid is. A multi-cell
    /// item names only the cell it originates in (its <see cref="ItemInstance.GridX"/>/
    /// <see cref="ItemInstance.GridY"/>) while every cell it covers still tints as occupied —
    /// so a two-handed sword reads as one item, not four.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("MMO/UI/Inventory Grid View")]
    public class InventoryGridView : MonoBehaviour
    {
        /// <summary>Edge length of one cell, in canvas units.</summary>
        public const float CellSize = 40f;

        /// <summary>Tint of a cell with nothing in it.</summary>
        public static readonly Color EmptyCellColor = new(1f, 1f, 1f, 0.04f);

        /// <summary>Tint of a cell covered by an item.</summary>
        public static readonly Color OccupiedCellColor = new(0.55f, 0.45f, 0.2f, 0.55f);

        /// <summary>
        /// One built cell: its coordinates, the slot view that answers right-clicks, and the
        /// graphics the repaint touches. Exposed so a bag screen can highlight or query cells
        /// without rebuilding the grid's internals.
        /// </summary>
        public sealed class Cell
        {
            internal Cell(int gridX, int gridY, InventorySlotView slot, Image background, Text label)
            {
                GridX = gridX;
                GridY = gridY;
                Slot = slot;
                Background = background;
                Label = label;
            }

            /// <summary>Column this cell represents.</summary>
            public int GridX { get; }

            /// <summary>Row this cell represents.</summary>
            public int GridY { get; }

            /// <summary>The view that turns this cell's right-click into an equip request.</summary>
            public InventorySlotView Slot { get; }

            /// <summary>The cell background, tinted when something covers the cell.</summary>
            public Image Background { get; }

            /// <summary>The label painted with an item name when this cell starts a footprint.</summary>
            public Text Label { get; }
        }

        [SerializeField] private RectTransform _cellContainer;

        private readonly List<Cell> _cells = new();
        private IInventorySource _source;

        /// <summary>Rows and columns of built cells live under this transform, when one was set.</summary>
        public RectTransform CellContainer => _cellContainer;

        /// <summary>The built cells, row-major (left to right, top to bottom).</summary>
        public IReadOnlyList<Cell> Cells => _cells;

        /// <summary>The source currently bound, or null.</summary>
        public IInventorySource Source => _source;

        /// <summary>Declares where cells are parented; public so a factory can hand over a panel.</summary>
        public void SetContainer(RectTransform cellContainer) => _cellContainer = cellContainer;

        /// <summary>
        /// Binds the grid: unsubscribes any previous source, subscribes to
        /// <c>ContainersChanged</c>, builds the cells the source's geometry calls for (keeping
        /// existing ones when the geometry already matches) and repaints once. Binding null is
        /// an unbind.
        /// </summary>
        public void Bind(IInventorySource source)
        {
            if (_source != null)
                _source.ContainersChanged -= OnContainersChanged;

            _source = source;

            if (_source != null)
            {
                _source.ContainersChanged += OnContainersChanged;
                EnsureCells();
            }

            Repaint();
        }

        /// <summary>Unsubscribes from the bound source. Built cells stay on screen as they were.</summary>
        public void Unbind()
        {
            if (_source != null)
                _source.ContainersChanged -= OnContainersChanged;

            _source = null;
        }

        /// <summary>
        /// Redraws every cell from the source's current contents. Public and virtual so a test
        /// can count repaints and prove they only ever come from <see cref="Bind"/> or a
        /// container change event.
        /// </summary>
        public virtual void Repaint()
        {
            if (_source == null)
                return;

            for (int i = 0; i < _cells.Count; i++)
            {
                Cell cell = _cells[i];
                ItemInstance item = _source.GetItemAt(cell.GridX, cell.GridY);
                bool occupied = !item.IsEmpty;
                bool startsFootprint = occupied &&
                                       item.GridX == cell.GridX &&
                                       item.GridY == cell.GridY;

                if (cell.Background != null)
                    cell.Background.color = occupied ? OccupiedCellColor : EmptyCellColor;

                if (cell.Label != null)
                    cell.Label.text = startsFootprint ? item.ItemName : string.Empty;
            }
        }

        /// <summary>
        /// Builds the cell matrix for the bound source's geometry, or does nothing when the
        /// right number of cells already exists. Cells are row-major so index arithmetic
        /// (row * width + column) matches what a test or a highlighter would expect.
        /// </summary>
        private void EnsureCells()
        {
            int width = _source.GridWidth;
            int height = _source.GridHeight;

            if (width < 1 || height < 1)
                return;

            if (_cells.Count == width * height)
                return;

            ClearCells();

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                    _cells.Add(CreateCell(x, y));
            }
        }

        private Cell CreateCell(int gridX, int gridY)
        {
            // A GameObject can carry only one Graphic, so the background Image and the label
            // cannot share this transform — the label gets its own child, which draws on top.
            var cellObject = new GameObject(
                $"Cell {gridX},{gridY}", typeof(RectTransform), typeof(Image));

            RectTransform rect = (RectTransform)cellObject.transform;
            rect.SetParent(_cellContainer != null ? _cellContainer : transform, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(gridX * CellSize, -gridY * CellSize);
            rect.sizeDelta = new Vector2(CellSize, CellSize);

            Image background = cellObject.GetComponent<Image>();
            background.color = EmptyCellColor;

            var labelObject = new GameObject("Label", typeof(RectTransform));
            RectTransform labelRect = (RectTransform)labelObject.transform;
            labelRect.SetParent(cellObject.transform, false);
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            Text label = labelObject.AddComponent<Text>();
            label.font = UIFont.Default;
            label.fontSize = 10;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.white;
            label.raycastTarget = false;
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 6;
            label.resizeTextMaxSize = 12;
            label.text = string.Empty;

            // The cell needs a raycastable graphic of its own for the slot view to receive the
            // click at all; the background Image serves, which is why the label opts out.
            InventorySlotView slot = cellObject.AddComponent<InventorySlotView>();
            slot.SetInventory(_source);
            slot.SetCell(gridX, gridY);

            return new Cell(gridX, gridY, slot, background, label);
        }

        private void ClearCells()
        {
            for (int i = 0; i < _cells.Count; i++)
            {
                InventorySlotView slot = _cells[i].Slot;
                if (slot != null)
                    ViewObject.Destroy(slot.gameObject);
            }

            _cells.Clear();
        }

        private void OnContainersChanged() => Repaint();

        private void OnDestroy() => Unbind();
    }
}
