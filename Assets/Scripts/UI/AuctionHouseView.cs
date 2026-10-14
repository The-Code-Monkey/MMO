using System;
using System.Collections.Generic;
using MMO.Auction;
using MMO.Inventory;
using UnityEngine;
using UnityEngine.UI;

namespace MMO.UI
{
    /// <summary>
    /// Auction House UI panel. Displays search results, my listings, and purchase history.
    /// Binds to <see cref="PlayerAuctionHouse"/> for data and intents.
    /// </summary>
    public sealed class AuctionHouseView : MonoBehaviour
    {
        [Header("Tabs")]
        [SerializeField] private Button _browseTabButton;
        [SerializeField] private Button _myListingsTabButton;
        [SerializeField] private Button _historyTabButton;

        [Header("Browse Tab")]
        [SerializeField] private GameObject _browsePanel;
        [SerializeField] private InputField _searchInput;
        [SerializeField] private InputField _maxPriceInput;
        [SerializeField] private Button _searchButton;
        [SerializeField] private Button _prevPageButton;
        [SerializeField] private Button _nextPageButton;
        [SerializeField] private Text _pageText;
        [SerializeField] private Transform _browseListContent;
        [SerializeField] private GameObject _listingItemPrefab;

        [Header("My Listings Tab")]
        [SerializeField] private GameObject _myListingsPanel;
        [SerializeField] private Transform _myListingsContent;
        [SerializeField] private GameObject _myListingItemPrefab;
        [SerializeField] private Button _refreshMyListingsButton;

        [Header("History Tab")]
        [SerializeField] private GameObject _historyPanel;
        [SerializeField] private Transform _historyContent;
        [SerializeField] private GameObject _historyItemPrefab;
        [SerializeField] private Button _refreshHistoryButton;

        [Header("Create Listing")]
        [SerializeField] private GameObject _createListingPanel;
        [SerializeField] private InputField _createPriceInput;
        [SerializeField] private InputField _createStackInput;
        [SerializeField] private InputField _createDurationInput;
        [SerializeField] private Button _createListingButton;
        [SerializeField] private Button _cancelCreateButton;
        [SerializeField] private Text _createItemNameText;
        [SerializeField] private Text _createItemValueText;

        private PlayerAuctionHouse _auctionHouse;
        private PlayerInventory _inventory;
        private AuctionHouseTab _currentTab = AuctionHouseTab.Browse;
        private int _currentPage = 0;
        private int _pendingBagX = -1;
        private int _pendingBagY = -1;
        private ItemInstance _pendingItem;

        public enum AuctionHouseTab
        {
            Browse = 0,
            MyListings = 1,
            History = 2,
        }

        public void Initialize(PlayerAuctionHouse auctionHouse, PlayerInventory inventory)
        {
            _auctionHouse = auctionHouse ?? throw new ArgumentNullException(nameof(auctionHouse));
            _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));

            _auctionHouse.OnSearchResultsReceived += OnSearchResultsReceived;
            _auctionHouse.OnMyListingsReceived += OnMyListingsReceived;
            _auctionHouse.OnPurchaseHistoryReceived += OnPurchaseHistoryReceived;
            _auctionHouse.OnListingCreated += OnListingCreated;
            _auctionHouse.OnListingBought += OnListingBought;
            _auctionHouse.OnListingCancelled += OnListingCancelled;
            _auctionHouse.OnPurchaseCompleted += OnPurchaseCompleted;

            _browseTabButton.onClick.AddListener(() => SwitchTab(AuctionHouseTab.Browse));
            _myListingsTabButton.onClick.AddListener(() => SwitchTab(AuctionHouseTab.MyListings));
            _historyTabButton.onClick.AddListener(() => SwitchTab(AuctionHouseTab.History));

            _searchButton.onClick.AddListener(OnSearchClicked);
            _prevPageButton.onClick.AddListener(() => ChangePage(-1));
            _nextPageButton.onClick.AddListener(() => ChangePage(1));
            _refreshMyListingsButton.onClick.AddListener(RequestMyListings);
            _refreshHistoryButton.onClick.AddListener(RequestHistory);

            _createListingButton.onClick.AddListener(OnCreateListingConfirmed);
            _cancelCreateButton.onClick.AddListener(HideCreateListingPanel);

            SwitchTab(AuctionHouseTab.Browse);
            RequestSearch();
            RequestMyListings();
            RequestHistory();
        }

        private void OnDestroy()
        {
            if (_auctionHouse != null)
            {
                _auctionHouse.OnSearchResultsReceived -= OnSearchResultsReceived;
                _auctionHouse.OnMyListingsReceived -= OnMyListingsReceived;
                _auctionHouse.OnPurchaseHistoryReceived -= OnPurchaseHistoryReceived;
                _auctionHouse.OnListingCreated -= OnListingCreated;
                _auctionHouse.OnListingBought -= OnListingBought;
                _auctionHouse.OnListingCancelled -= OnListingCancelled;
                _auctionHouse.OnPurchaseCompleted -= OnPurchaseCompleted;
            }
        }

        public void OpenCreateListing(int bagX, int bagY)
        {
            _pendingBagX = bagX;
            _pendingBagY = bagY;
            _pendingItem = _inventory.GetItemAt(bagX, bagY);

            if (_pendingItem.IsEmpty)
            {
                _pendingBagX = -1;
                _pendingBagY = -1;
                return;
            }

            ItemDefinition def = ItemDefinitionRegistry.Resolve(_pendingItem.ItemName);
            _createItemNameText.text = def != null ? def.ItemName : _pendingItem.ItemName;
            _createItemValueText.text = $"Value: {(_pendingItem.GetTotalValue(def))} gold each";
            _createPriceInput.text = (def != null ? def.Value : 1).ToString();
            _createStackInput.text = (_pendingItem.StackCount < 1 ? 1 : _pendingItem.StackCount).ToString();
            _createDurationInput.text = AuctionService.DefaultListingDurationHours.ToString();

            _createListingPanel.SetActive(true);
        }

        private void HideCreateListingPanel()
        {
            _createListingPanel.SetActive(false);
            _pendingBagX = -1;
            _pendingBagY = -1;
            _pendingItem = default;
        }

        private void OnCreateListingConfirmed()
        {
            if (_pendingItem.IsEmpty || _pendingBagX < 0 || _pendingBagY < 0)
                return;

            if (!long.TryParse(_createPriceInput.text, out long pricePerUnit) || pricePerUnit < 1)
            {
                Debug.LogWarning("[AuctionHouse] Invalid price");
                return;
            }

            if (!int.TryParse(_createStackInput.text, out int stackCount) || stackCount < 1)
            {
                stackCount = -1; // sell all
            }

            if (!int.TryParse(_createDurationInput.text, out int durationHours) || durationHours < 1)
            {
                durationHours = AuctionService.DefaultListingDurationHours;
            }

            _auctionHouse.RequestCreateListing(_pendingBagX, _pendingBagY, pricePerUnit, stackCount, durationHours);
            HideCreateListingPanel();
        }

        private void SwitchTab(AuctionHouseTab tab)
        {
            _currentTab = tab;
            _browsePanel.SetActive(tab == AuctionHouseTab.Browse);
            _myListingsPanel.SetActive(tab == AuctionHouseTab.MyListings);
            _historyPanel.SetActive(tab == AuctionHouseTab.History);

            _browseTabButton.interactable = tab != AuctionHouseTab.Browse;
            _myListingsTabButton.interactable = tab != AuctionHouseTab.MyListings;
            _historyTabButton.interactable = tab != AuctionHouseTab.History;

            if (tab == AuctionHouseTab.Browse)
            {
                _currentPage = 0;
                RequestSearch();
            }
        }

        private void RequestSearch()
        {
            string filter = _searchInput != null ? _searchInput.text : null;
            long maxPrice = 0;
            if (_maxPriceInput != null && long.TryParse(_maxPriceInput.text, out long mp))
                maxPrice = mp;

            _auctionHouse.RequestSearch(filter, maxPrice, _currentPage, 20);
        }

        private void OnSearchClicked()
        {
            _currentPage = 0;
            RequestSearch();
        }

        private void ChangePage(int delta)
        {
            _currentPage = Math.Max(0, _currentPage + delta);
            RequestSearch();
        }

        private void RequestMyListings()
        {
            _auctionHouse.RequestMyListings();
        }

        private void RequestHistory()
        {
            _auctionHouse.RequestPurchaseHistory(20);
        }

        private void OnSearchResultsReceived(List<AuctionListingRecord> listings)
        {
            ClearList(_browseListContent);
            _pageText.text = $"Page {_currentPage + 1}";

            foreach (AuctionListingRecord listing in listings)
            {
                GameObject item = Instantiate(_listingItemPrefab, _browseListContent);
                SetupBrowseListingItem(item, listing);
            }
        }

        private void OnMyListingsReceived(List<AuctionListingRecord> listings)
        {
            ClearList(_myListingsContent);

            foreach (AuctionListingRecord listing in listings)
            {
                GameObject item = Instantiate(_myListingItemPrefab, _myListingsContent);
                SetupMyListingItem(item, listing);
            }
        }

        private void OnPurchaseHistoryReceived(List<AuctionTransactionRecord> transactions)
        {
            ClearList(_historyContent);

            foreach (AuctionTransactionRecord tx in transactions)
            {
                GameObject item = Instantiate(_historyItemPrefab, _historyContent);
                SetupHistoryItem(item, tx);
            }
        }

        private void OnListingCreated(AuctionListingRecord listing)
        {
            // Refresh my listings
            RequestMyListings();
        }

        private void OnListingBought(AuctionListingRecord listing, AuctionTransactionRecord transaction)
        {
            // Refresh both browse and my listings
            RequestSearch();
            RequestMyListings();
            RequestHistory();
        }

        private void OnListingCancelled(AuctionListingRecord listing)
        {
            RequestMyListings();
        }

        private void OnPurchaseCompleted(AuctionTransactionRecord transaction)
        {
            RequestHistory();
            RequestSearch();
        }

        private void SetupBrowseListingItem(GameObject item, AuctionListingRecord listing)
        {
            ItemDefinition def = ItemDefinitionRegistry.Resolve(listing.ItemName);
            string displayName = def != null ? def.ItemName : listing.ItemName;

            Text nameText = item.transform.Find("NameText")?.GetComponent<Text>();
            Text priceText = item.transform.Find("PriceText")?.GetComponent<Text>();
            Text sellerText = item.transform.Find("SellerText")?.GetComponent<Text>();
            Text stackText = item.transform.Find("StackText")?.GetComponent<Text>();
            Button buyButton = item.transform.Find("BuyButton")?.GetComponent<Button>();

            if (nameText != null) nameText.text = displayName;
            if (priceText != null) priceText.text = $"{listing.TotalPrice:N0} gold ({listing.PricePerUnit:N0} each)";
            if (sellerText != null) sellerText.text = $"Seller: {listing.SellerPlayerName}";
            if (stackText != null) stackText.text = listing.StackCount > 1 ? $"x{listing.StackCount}" : "";

            if (buyButton != null)
            {
                buyButton.onClick.RemoveAllListeners();
                buyButton.onClick.AddListener(() => _auctionHouse.RequestBuyListing(listing.ListingId));
            }
        }

        private void SetupMyListingItem(GameObject item, AuctionListingRecord listing)
        {
            ItemDefinition def = ItemDefinitionRegistry.Resolve(listing.ItemName);
            string displayName = def != null ? def.ItemName : listing.ItemName;

            Text nameText = item.transform.Find("NameText")?.GetComponent<Text>();
            Text priceText = item.transform.Find("PriceText")?.GetComponent<Text>();
            Text stackText = item.transform.Find("StackText")?.GetComponent<Text>();
            Text statusText = item.transform.Find("StatusText")?.GetComponent<Text>();
            Button cancelButton = item.transform.Find("CancelButton")?.GetComponent<Button>();

            if (nameText != null) nameText.text = displayName;
            if (priceText != null) priceText.text = $"{listing.TotalPrice:N0} gold ({listing.PricePerUnit:N0} each)";
            if (stackText != null) stackText.text = listing.StackCount > 1 ? $"x{listing.StackCount}" : "";

            if (statusText != null)
            {
                switch (listing.Status)
                {
                    case AuctionListingStatus.Active:
                        statusText.text = "Active";
                        statusText.color = Color.green;
                        break;
                    case AuctionListingStatus.Sold:
                        statusText.text = "Sold";
                        statusText.color = Color.blue;
                        break;
                    case AuctionListingStatus.Cancelled:
                        statusText.text = "Cancelled";
                        statusText.color = Color.gray;
                        break;
                    case AuctionListingStatus.Expired:
                        statusText.text = "Expired";
                        statusText.color = Color.red;
                        break;
                }
            }

            if (cancelButton != null)
            {
                cancelButton.gameObject.SetActive(listing.Status == AuctionListingStatus.Active);
                cancelButton.onClick.RemoveAllListeners();
                cancelButton.onClick.AddListener(() => _auctionHouse.RequestCancelListing(listing.ListingId));
            }
        }

        private void SetupHistoryItem(GameObject item, AuctionTransactionRecord tx)
        {
            ItemDefinition def = ItemDefinitionRegistry.Resolve(tx.ItemName);
            string displayName = def != null ? def.ItemName : tx.ItemName;

            Text nameText = item.transform.Find("NameText")?.GetComponent<Text>();
            Text priceText = item.transform.Find("PriceText")?.GetComponent<Text>();
            Text countText = item.transform.Find("CountText")?.GetComponent<Text>();
            Text dateText = item.transform.Find("DateText")?.GetComponent<Text>();

            if (nameText != null) nameText.text = displayName;
            if (priceText != null) priceText.text = $"{tx.TotalPrice:N0} gold";
            if (countText != null) countText.text = tx.StackCount > 1 ? $"x{tx.StackCount}" : "";
            if (dateText != null)
            {
                DateTime date = new DateTime(tx.CompletedAtTicks, DateTimeKind.Utc).ToLocalTime();
                dateText.text = date.ToString("yyyy-MM-dd HH:mm");
            }
        }

        private void ClearList(Transform content)
        {
            if (content == null) return;
            for (int i = content.childCount - 1; i >= 0; i--)
            {
                Destroy(content.GetChild(i).gameObject);
            }
        }
    }
}