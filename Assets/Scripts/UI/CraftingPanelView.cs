using System;
using System.Collections.Generic;
using MMO.Crafting;
using UnityEngine;
using UnityEngine.UI;

namespace MMO.UI
{
    /// <summary>
    /// The one unified crafting window: a scroll of recipe rows fed by whatever offer payload the
    /// server last sent. The panel is intentionally ignorant on two counts.
    ///
    /// <para>
    /// It does not know what recipes exist — it renders <see cref="ICraftingSource.AvailableRecipes"/>
    /// from the <c>StationOpened</c> payload. An anvil, an alchemy lab and a loom all open this
    /// same component; the only difference on screen is the list the server sent.
    /// </para>
    ///
    /// <para>
    /// It does not decide anything — a row is a click, and a click forwards
    /// <see cref="ICraftingSource.RequestCraft"/>. A row whose offer reports
    /// <see cref="RecipeOffer.IngredientsMet"/> false is greyed and its button disabled, but that
    /// is a courtesy: the server re-validates every craft, so the hint is never the gate.
    /// </para>
    ///
    /// <para>
    /// Like every view in this namespace it owns no <c>Update</c>; it repaints from
    /// <c>StationOpened</c>/<c>RecipesChanged</c> and from binding, and does nothing in between.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("MMO/UI/Crafting Panel View")]
    public class CraftingPanelView : MonoBehaviour
    {
        /// <summary>Height of one recipe row, in canvas units.</summary>
        public const float RowHeight = 30f;

        /// <summary>Background of a row the player can afford.</summary>
        public static readonly Color RowColor = new(0.1f, 0.1f, 0.12f, 0.8f);

        /// <summary>Background of a row the player cannot afford yet.</summary>
        public static readonly Color LockedRowColor = new(0.24f, 0.1f, 0.1f, 0.8f);

        /// <summary>One built row: the offer it shows and the widgets a repaint touches.</summary>
        public sealed class Row
        {
            internal Row(RecipeOffer offer, GameObject gameObject, Text nameLabel, Text countLabel, Button craftButton)
            {
                Offer = offer;
                GameObject = gameObject;
                NameLabel = nameLabel;
                CountLabel = countLabel;
                CraftButton = craftButton;
            }

            /// <summary>The offer this row was built from.</summary>
            public RecipeOffer Offer { get; }

            /// <summary>The row object, parented to the container.</summary>
            public GameObject GameObject { get; }

            /// <summary>The recipe name label.</summary>
            public Text NameLabel { get; }

            /// <summary>The output-count label.</summary>
            public Text CountLabel { get; }

            /// <summary>The button that forwards a craft intent; disabled when unaffordable.</summary>
            public Button CraftButton { get; }
        }

        [SerializeField] private RectTransform _rowContainer;
        [SerializeField] private Text _titleLabel;

        private readonly List<Row> _rows = new();
        private ICraftingSource _source;
        private CraftingStation _station;
        private IReadOnlyList<RecipeOffer> _offers = Array.Empty<RecipeOffer>();

        /// <summary>Where built rows are parented, when one was set.</summary>
        public RectTransform RowContainer => _rowContainer;

        /// <summary>The title label, when one was set.</summary>
        public Text TitleLabel => _titleLabel;

        /// <summary>The built rows, top to bottom.</summary>
        public IReadOnlyList<Row> Rows => _rows;

        /// <summary>The station currently open, or null.</summary>
        public CraftingStation Station => _station;

        /// <summary>The source currently bound, or null.</summary>
        public ICraftingSource Source => _source;

        /// <summary>True while a station is open and the panel is on screen.</summary>
        public bool IsOpen => _station != null && gameObject.activeSelf;

        /// <summary>Declares where rows are parented; public so a factory can hand over a scroll view.</summary>
        public void SetContainer(RectTransform rowContainer) => _rowContainer = rowContainer;

        /// <summary>Declares the title label; public so a factory can hand over its header.</summary>
        public void SetTitle(Text titleLabel) => _titleLabel = titleLabel;

        /// <summary>
        /// Binds the panel: unsubscribes any previous source, subscribes to
        /// <c>RecipesChanged</c>/<c>StationOpened</c>, and shows whatever the source already
        /// holds. Binding null is an unbind.
        /// </summary>
        public void Bind(ICraftingSource source)
        {
            Unbind();
            _source = source;
            if (_source == null)
                return;

            _source.RecipesChanged += Repaint;
            _source.StationOpened += OnStationOpened;

            _offers = _source.AvailableRecipes ?? Array.Empty<RecipeOffer>();
            _station = _source.OpenedStation;
            gameObject.SetActive(_station != null);

            Repaint();
        }

        /// <summary>Unsubscribes from the bound source. Built rows stay on screen as they were.</summary>
        public void Unbind()
        {
            if (_source != null)
            {
                _source.RecipesChanged -= Repaint;
                _source.StationOpened -= OnStationOpened;
                _source = null;
            }
        }

        /// <summary>Hides the panel and forgets the station it was showing.</summary>
        public void Conceal()
        {
            gameObject.SetActive(false);
            _station = null;
            _offers = Array.Empty<RecipeOffer>();
            ClearRows();
        }

        /// <summary>
        /// Rebuilds every row from the offers last received. Public and virtual so a test can
        /// count repaints and prove they only come from <see cref="Bind"/> or a change event.
        /// </summary>
        public virtual void Repaint()
        {
            ClearRows();

            if (_source == null || _station == null)
                return;

            if (_titleLabel != null)
                _titleLabel.text = _station.StationType.ToString();

            for (int i = 0; i < _offers.Count; i++)
                _rows.Add(CraftingRowFactory.Create(
                    _offers[i],
                    _rowContainer != null ? _rowContainer : transform,
                    _rows.Count,
                    HandleRowClicked));
        }

        /// <summary>
        /// Forwards a row click as a craft intent. Virtual so a test can watch clicks arrive
        /// without synthesizing a pointer event, and guarded so an unbound or concealed panel, an
        /// out-of-range row, an unaffordable row, or a source whose GameObject has since been
        /// destroyed is a no-op rather than an exception inside uGUI's dispatch.
        /// </summary>
        public virtual void HandleRowClicked(int row)
        {
            if (_source == null || _station == null || row < 0 || row >= _rows.Count)
                return;

            if (_source is UnityEngine.Object unityObject && unityObject == null)
                return;

            Row entry = _rows[row];
            if (!entry.Offer.IngredientsMet)
                return;

            _source.RequestCraft(_station, entry.Offer.RecipeName);
        }

        private void OnStationOpened(CraftingStation station, IReadOnlyList<RecipeOffer> offers)
        {
            _station = station;
            _offers = offers ?? Array.Empty<RecipeOffer>();
            gameObject.SetActive(station != null);

            Repaint();
        }

        private void ClearRows()
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].GameObject != null)
                    ViewObject.Destroy(_rows[i].GameObject);
            }

            _rows.Clear();
        }

        private void OnDestroy() => Unbind();
    }
}
