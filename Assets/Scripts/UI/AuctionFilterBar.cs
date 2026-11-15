using System;
using System.Collections.Generic;
using MMO.Auction;
using UnityEngine;
using UnityEngine.UI;

namespace MMO.UI
{
    /// <summary>
    /// The browse filter strip: category and sort cyclers, text/seller/tier/price boxes, and the
    /// one-click saved-search chips. It only builds an <see cref="AuctionQuery"/>; the house runs it
    /// and the server applies the kernel, so the client never filters or pages a result locally.
    ///
    /// <para>
    /// Cycle buttons replace dropdowns because the project has no dropdown template anywhere else;
    /// a label plus a button is one built object and behaves the same on every input device.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("MMO/UI/Auction Filter Bar")]
    public sealed class AuctionFilterBar : MonoBehaviour
    {
        public const string GameObjectName = "AuctionFilterBar";

        private InputField _text;
        private InputField _seller;
        private InputField _minTier;
        private InputField _maxTier;
        private InputField _minPrice;
        private InputField _maxPrice;
        private Button _categoryButton;
        private Text _categoryLabel;
        private Button _sortButton;
        private Text _sortLabel;
        private Button _saveButton;
        private Button _searchButton;
        private RectTransform _savedContent;

        private readonly List<GameObject> _chips = new();
        private readonly IReadOnlyList<string> _categories = AuctionQueryMapper.Categories();
        private int _categoryIndex;
        private AuctionSort _sort = AuctionSort.Relevance;
        private IReadOnlyList<AuctionQuery> _saved = Array.Empty<AuctionQuery>();

        /// <summary>Raised when the player asks for the current filter to be run.</summary>
        public event Action<AuctionQuery> SearchRequested;

        /// <summary>Raised when the player asks to keep the current filter as a chip.</summary>
        public event Action<AuctionQuery> SaveRequested;

        public string Category => _categoryIndex == 0 ? string.Empty : _categories[_categoryIndex - 1];

        public AuctionSort SortBy => _sort;

        /// <summary>Declares the widgets a factory (or a test) built.</summary>
        public void SetHosts(
            InputField text,
            InputField seller,
            InputField minTier,
            InputField maxTier,
            InputField minPrice,
            InputField maxPrice,
            Button categoryButton,
            Text categoryLabel,
            Button sortButton,
            Text sortLabel,
            Button saveButton,
            Button searchButton,
            RectTransform savedContent)
        {
            _text = text;
            _seller = seller;
            _minTier = minTier;
            _maxTier = maxTier;
            _minPrice = minPrice;
            _maxPrice = maxPrice;
            _categoryButton = categoryButton;
            _categoryLabel = categoryLabel;
            _sortButton = sortButton;
            _sortLabel = sortLabel;
            _saveButton = saveButton;
            _searchButton = searchButton;
            _savedContent = savedContent;

            if (_categoryButton != null)
                _categoryButton.onClick.AddListener(CycleCategory);
            if (_sortButton != null)
                _sortButton.onClick.AddListener(CycleSort);
            if (_searchButton != null)
                _searchButton.onClick.AddListener(OnSearchClicked);
            if (_saveButton != null)
                _saveButton.onClick.AddListener(OnSaveClicked);

            RefreshLabels();
        }

        /// <summary>The current filter bar as a query for <paramref name="page"/>.</summary>
        public AuctionQuery BuildQuery(int page)
            => AuctionQueryMapper.Build(
                _text != null ? _text.text : string.Empty,
                Category,
                _minTier != null ? _minTier.text : string.Empty,
                _maxTier != null ? _maxTier.text : string.Empty,
                _seller != null ? _seller.text : string.Empty,
                _minPrice != null ? _minPrice.text : string.Empty,
                _maxPrice != null ? _maxPrice.text : string.Empty,
                _sort,
                page);

        /// <summary>Populates the controls from a saved chip so it can be edited before searching.</summary>
        public void ApplyQuery(AuctionQuery query)
        {
            if (query == null)
                return;

            if (_text != null)
                _text.text = query.Text ?? string.Empty;
            if (_seller != null)
                _seller.text = query.SellerId ?? string.Empty;
            if (_minTier != null)
                _minTier.text = query.MinTier > 0 ? query.MinTier.ToString() : string.Empty;
            if (_maxTier != null)
                _maxTier.text = query.MaxTier < int.MaxValue ? query.MaxTier.ToString() : string.Empty;
            if (_minPrice != null)
                _minPrice.text = query.MinPrice > 0 ? query.MinPrice.ToString() : string.Empty;
            if (_maxPrice != null)
                _maxPrice.text = query.MaxPrice < long.MaxValue ? query.MaxPrice.ToString() : string.Empty;

            int category = -1;
            for (int i = 0; i < _categories.Count; i++)
            {
                if (string.Equals(_categories[i], query.Category, StringComparison.OrdinalIgnoreCase))
                {
                    category = i;
                    break;
                }
            }

            _categoryIndex = category + 1;
            _sort = query.SortBy;
            RefreshLabels();
        }

        /// <summary>Replaces the chip row from a server snapshot.</summary>
        public void SetSavedSearches(IReadOnlyList<AuctionQuery> queries)
        {
            _saved = queries ?? Array.Empty<AuctionQuery>();
            ClearChips();

            if (_savedContent == null)
                return;

            for (int i = 0; i < _saved.Count; i++)
                _chips.Add(CreateChip(_saved[i], i));
        }

        public void CycleCategory()
        {
            _categoryIndex = (_categoryIndex + 1) % (_categories.Count + 1);
            RefreshLabels();
        }

        public void CycleSort()
        {
            Array values = Enum.GetValues(typeof(AuctionSort));
            int index = Array.IndexOf(values, _sort);
            _sort = (AuctionSort)values.GetValue((index + 1) % values.Length);
            RefreshLabels();
        }

        private void OnSearchClicked() => SearchRequested?.Invoke(BuildQuery(0));

        private void OnSaveClicked() => SaveRequested?.Invoke(BuildQuery(0));

        private GameObject CreateChip(AuctionQuery query, int index)
        {
            var chipObject = new GameObject($"Saved {index}", typeof(RectTransform), typeof(Image));
            RectTransform rect = (RectTransform)chipObject.transform;
            rect.SetParent(_savedContent, false);
            rect.sizeDelta = new Vector2(150f, 24f);

            Image background = chipObject.GetComponent<Image>();
            background.color = new Color(0.3f, 0.3f, 0.3f, 0.9f);

            Button button = chipObject.AddComponent<Button>();
            button.targetGraphic = background;
            button.onClick.AddListener(() =>
            {
                ApplyQuery(query);
                SearchRequested?.Invoke(BuildQuery(0));
            });

            var labelObject = new GameObject("Label", typeof(RectTransform), typeof(Text));
            RectTransform labelRect = (RectTransform)labelObject.transform;
            labelRect.SetParent(rect, false);
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(6f, 0f);
            labelRect.offsetMax = new Vector2(-6f, 0f);

            Text label = labelObject.GetComponent<Text>();
            label.font = UIFont.Default;
            label.fontSize = 12;
            label.alignment = TextAnchor.MiddleLeft;
            label.color = Color.white;
            label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.text = ChipLabel(query);

            return chipObject;
        }

        private static string ChipLabel(AuctionQuery query)
        {
            if (query == null)
                return "Saved";

            if (!string.IsNullOrEmpty(query.Text))
                return query.Text;

            if (!string.IsNullOrEmpty(query.Category))
                return query.Category;

            return query.SortBy.ToString();
        }

        private void RefreshLabels()
        {
            if (_categoryLabel != null)
                _categoryLabel.text = _categoryIndex == 0 ? "All categories" : Category;

            if (_sortLabel != null)
                _sortLabel.text = _sort.ToString();
        }

        private void ClearChips()
        {
            for (int i = 0; i < _chips.Count; i++)
            {
                if (_chips[i] != null)
                    ViewObject.Destroy(_chips[i]);
            }

            _chips.Clear();
        }

        private void OnDestroy() => ClearChips();
    }
}
