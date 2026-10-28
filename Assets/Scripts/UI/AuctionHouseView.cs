using System;
using System.Collections.Generic;
using MMO.Auction;
using MMO.Inventory;
using UnityEngine;
using UnityEngine.UI;

namespace MMO.UI
{
    /// <summary>
    /// Auction House UI panel. Displays search results, my listings, buy orders, and purchase history.
    /// Binds to <see cref="PlayerAuctionHouse"/> for data and intents.
    /// </summary>
    public sealed class AuctionHouseView : MonoBehaviour
    {
        [Header("Tabs")]
        [SerializeField] private Button _browseTabButton;
        [SerializeField] private Button _buyOrdersTabButton;
        [SerializeField] private Button _myListingsTabButton;
        [SerializeField] private Button _myBuyOrdersTabButton;
        [SerializeField] private Button _historyTabButton;

        [Header("Browse Listings Tab")]
        [SerializeField] private GameObject _browsePanel;
        [SerializeField] private InputField _searchInput;
        [SerializeField] private InputField _maxPriceInput;
        [SerializeField] private Button _searchButton;
        [SerializeField] private Button _prevPageButton;
        [SerializeField] private Button _nextPageButton;
        [SerializeField] private Text _pageText;
        [SerializeField] private Transform _browseListContent;
        [SerializeField] private GameObject _listingItemPrefab;

        [Header("Browse Buy Orders Tab")]
        [SerializeField] private GameObject _buyOrdersPanel;
        [SerializeField] private InputField _buyOrderSearchInput;
        [SerializeField] private InputField _minPriceInput;
        [SerializeField] private Button _buyOrderSearchButton;
        [SerializeField] private Button _buyOrderPrevPageButton;
        [SerializeField] private Button _buyOrderNextPageButton;
        [SerializeField] private Text _buyOrderPageText;
        [SerializeField] private Transform _buyOrdersListContent;
        [SerializeField] private GameObject _buyOrderItemPrefab;

        [Header("My Listings Tab")]
        [SerializeField] private GameObject _myListingsPanel;
        [SerializeField] private Transform _myListingsContent;
        [SerializeField] private GameObject _myListingItemPrefab;
        [SerializeField] private Button _refreshMyListingsButton;

        [Header("My Buy Orders Tab")]
        [SerializeField] private GameObject _myBuyOrdersPanel;
        [SerializeField] private Transform _myBuyOrdersContent;
        [SerializeField] private GameObject _myBuyOrderItemPrefab;
        [SerializeField] private Button _refreshMyBuyOrdersButton;

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

        [Header("Create Buy Order")]
        [SerializeField] private GameObject _createBuyOrderPanel;
        [SerializeField] private InputField _createBuyOrderItemNameInput;
        [SerializeField] private InputField _createBuyOrderQuantityInput;
        [SerializeField] private InputField _createBuyOrderPriceInput;
        [SerializeField] private InputField _createBuyOrderDurationInput;
        [SerializeField] private Button _createBuyOrderButton;
        [SerializeField] private Button _cancelCreateBuyOrderButton;

        private PlayerAuctionHouse _auctionHouse;
        private PlayerInventory _inventory;
        private AuctionHouseTab _currentTab = AuctionHouseTab.Browse;
        private int _currentPage = 0;
        private int _buyOrderPage = 0;
        private int _pendingBagX = -1;
        private int _pendingBagY = -1;
        private ItemInstance _pendingItem;

        public enum AuctionHouseTab
        {
            Browse = 0,
            BuyOrders = 1,
            MyListings = 2,
            MyBuyOrders = 3,
            History = 4,
        }

        public void Initialize(PlayerAuctionHouse auctionHouse, PlayerInventory inventory)
        {
            _auctionHouse = auctionHouse ?? throw new ArgumentNullException(nameof(auctionHouse));
            _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));

            _auctionHouse.OnSearchResultsReceived += OnSearchResultsReceived;
            _auctionHouse.OnSearchBuyOrdersReceived += OnSearchBuyOrdersReceived;
            _auctionHouse.OnMyListingsReceived += OnMyListingsReceived;
            _auctionHouse.OnMyBuyOrdersReceived += OnMyBuyOrdersReceived;
            _auctionHouse.OnPurchaseHistoryReceived += OnPurchaseHistoryReceived;
            _auctionHouse.OnListingCreated += OnListingCreated;
            _auctionHouse.OnListingBought += OnListingBought;
            _auctionHouse.OnListingCancelled += OnListingCancelled;
            _auctionHouse.OnBuyOrderCreated += OnBuyOrderCreated;
            _auctionHouse.OnBuyOrderFulfilled += OnBuyOrderFulfilled;
            _auctionHouse.OnBuyOrderCancelled += OnBuyOrderCancelled;
            _auctionHouse.OnPurchaseCompleted += OnPurchaseCompleted;

            _browseTabButton.onClick.AddListener(() => SwitchTab(AuctionHouseTab.Browse));
            _buyOrdersTabButton.onClick.AddListener(() => SwitchTab(AuctionHouseTab.BuyOrders));
            _myListingsTabButton.onClick.AddListener(() => SwitchTab(AuctionHouseTab.MyListings));
            _myBuyOrdersTabButton.onClick.AddListener(() => SwitchTab(AuctionHouseTab.MyBuyOrders));
            _historyTabButton.onClick.AddListener(() => SwitchTab(AuctionHouseTab.History));

            _searchButton.onClick.AddListener(OnSearchClicked);
            _prevPageButton.onClick.AddListener(() => ChangePage(-1));
            _nextPageButton.onClick.AddListener(() => ChangePage(1));

            _buyOrderSearchButton.onClick.AddListener(OnBuyOrderSearchClicked);
            _buyOrderPrevPageButton.onClick.AddListener(() => ChangeBuyOrderPage(-1));
            _buyOrderNextPageButton.onClick.AddListener(() => ChangeBuyOrderPage(1));

            _refreshMyListingsButton.onClick.AddListener(RequestMyListings);
            _refreshMyBuyOrdersButton.onClick.AddListener(RequestMyBuyOrders);
            _refreshHistoryButton.onClick.AddListener(RequestHistory);

            _createListingButton.onClick.AddListener(OnCreateListingConfirmed);
            _cancelCreateButton.onClick.AddListener(HideCreateListingPanel);

            _createBuyOrderButton.onClick.AddListener(OnCreateBuyOrderConfirmed);
            _cancelCreateBuyOrderButton.onClick.AddListener(HideCreateBuyOrderPanel);

            SwitchTab(AuctionHouseTab.Browse);
            RequestSearch();
            RequestMyListings();
            RequestMyBuyOrders();
            RequestHistory();
        }

        private void OnDestroy()
        {
            if (_auctionHouse != null)
            {
                _auctionHouse.OnSearchResultsReceived -= OnSearchResultsReceived;
                _auctionHouse.OnSearchBuyOrdersReceived -= OnSearchBuyOrdersReceived;
                _auctionHouse.OnMyListingsReceived -= OnMyListingsReceived;
                _auctionHouse.OnMyBuyOrdersReceived -= OnMyBuyOrdersReceived;
                _auctionHouse.OnPurchaseHistoryReceived -= OnPurchaseHistoryReceived;
                _auctionHouse.OnListingCreated -= OnListingCreated;
                _auctionHouse.OnListingBought -= OnListingBought;
                _auctionHouse.OnListingCancelled -= OnListingCancelled;
                _auctionHouse.OnBuyOrderCreated -= OnBuyOrderCreated;
                _auctionHouse.OnBuyOrderFulfilled -= OnBuyOrderFulfilled;
                _auctionHouse.OnBuyOrderCancelled -= OnBuyOrderCancelled;
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

        private void HideCreateBuyOrderPanel()
        {
            _createBuyOrderPanel.SetActive(false);
        }

        private void OnCreateBuyOrderConfirmed()
        {
            if (!long.TryParse(_createBuyOrderPriceInput.text, out long pricePerUnit) || pricePerUnit < 1)
            {
                Debug.LogWarning("[AuctionHouse] Invalid buy order price");
                return;
            }

            if (!int.TryParse(_createBuyOrderQuantityInput.text, out int quantity) || quantity < 1)
            {
                Debug.LogWarning("[AuctionHouse] Invalid buy order quantity");
                return;
            }

            if (!int.TryParse(_createBuyOrderDurationInput.text, out int durationHours) || durationHours < 1)
            {
                durationHours = AuctionService.DefaultListingDurationHours;
            }

            string itemName = _createBuyOrderItemNameInput.text.Trim();
            if (string.IsNullOrEmpty(itemName))
            {
                Debug.LogWarning("[AuctionHouse] Invalid buy order item name");
                return;
            }

            _auctionHouse.RequestCreateBuyOrder(itemName, quantity, pricePerUnit, durationHours);
            HideCreateBuyOrderPanel();
        }

        private void SwitchTab(AuctionHouseTab tab)
        {
            _currentTab = tab;
            _browsePanel.SetActive(tab == AuctionHouseTab.Browse);
            _buyOrdersPanel.SetActive(tab == AuctionHouseTab.BuyOrders);
            _myListingsPanel.SetActive(tab == AuctionHouseTab.MyListings);
            _myBuyOrdersPanel.SetActive(tab == AuctionHouseTab.MyBuyOrders);
            _historyPanel.SetActive(tab == AuctionHouseTab.History);

            _browseTabButton.interactable = tab != AuctionHouseTab.Browse;
            _buyOrdersTabButton.interactable = tab != AuctionHouseTab.BuyOrders;
            _myListingsTabButton.interactable = tab != AuctionHouseTab.MyListings;
            _myBuyOrdersTabButton.interactable = tab != AuctionHouseTab.MyBuyOrders;
            _historyTabButton.interactable = tab != AuctionHouseTab.History;

            if (tab == AuctionHouseTab.Browse)
            {
                _currentPage = 0;
                RequestSearch();
            }
            else if (tab == AuctionHouseTab.BuyOrders)
            {
                _buyOrderPage = 0;
                RequestSearchBuyOrders();
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

        private void RequestSearchBuyOrders()
        {
            string filter = _buyOrderSearchInput != null ? _buyOrderSearchInput.text : null;
            long minPrice = 0;
            if (_minPriceInput != null && long.TryParse(_minPriceInput.text, out long mp))
                minPrice = mp;

            _auctionHouse.RequestSearchBuyOrders(filter, minPrice, _buyOrderPage, 20);
        }

        private void OnBuyOrderSearchClicked()
        {
            _buyOrderPage = 0;
            RequestSearchBuyOrders();
        }

        private void ChangeBuyOrderPage(int delta)
        {
            _buyOrderPage = Math.Max(0, _buyOrderPage + delta);
            RequestSearchBuyOrders();
        }

        private void RequestMyListings()
        {
            _auctionHouse.RequestMyListings();
        }

        private void RequestMyBuyOrders()
        {
            _auctionHouse.RequestMyBuyOrders();
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

        private void OnSearchBuyOrdersReceived(List<AuctionBuyOrderRecord> buyOrders)
        {
            ClearList(_buyOrdersListContent);
            _buyOrderPageText.text = $"Page {_buyOrderPage + 1}";

            foreach (AuctionBuyOrderRecord buyOrder in buyOrders)
            {
                GameObject item = Instantiate(_buyOrderItemPrefab, _buyOrdersListContent);
                SetupBrowseBuyOrderItem(item, buyOrder);
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

        private void OnMyBuyOrdersReceived(List<AuctionBuyOrderRecord> buyOrders)
        {
            ClearList(_myBuyOrdersContent);

            foreach (AuctionBuyOrderRecord buyOrder in buyOrders)
            {
                GameObject item = Instantiate(_myBuyOrderItemPrefab, _myBuyOrdersContent);
                SetupMyBuyOrderItem(item, buyOrder);
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

        private void OnBuyOrderCreated(AuctionBuyOrderRecord buyOrder)
        {
            RequestMyBuyOrders();
        }

        private void OnBuyOrderFulfilled(AuctionBuyOrderRecord buyOrder, AuctionTransactionRecord transaction)
        {
            RequestSearchBuyOrders();
            RequestMyBuyOrders();
            RequestHistory();
        }

        private void OnBuyOrderCancelled(AuctionBuyOrderRecord buyOrder)
        {
            RequestMyBuyOrders();
        }

        private void OnPurchaseCompleted(AuctionTransactionRecord transaction)
        {
            RequestHistory();
            RequestSearch();
            RequestSearchBuyOrders();
        }

        private void SetupBrowseListingItem(GameObject item, AuctionListingRecord listing)
        {
            string displayName = AuctionUiFormat.DisplayName(listing.ItemName);

            Text nameText = item.transform.Find("NameText")?.GetComponent<Text>();
            Text priceText = item.transform.Find("PriceText")?.GetComponent<Text>();
            Text sellerText = item.transform.Find("SellerText")?.GetComponent<Text>();
            Text stackText = item.transform.Find("StackText")?.GetComponent<Text>();
            Button buyButton = item.transform.Find("BuyButton")?.GetComponent<Button>();

            if (nameText != null) nameText.text = displayName;
            if (priceText != null) priceText.text = AuctionUiFormat.ListingPrice(listing);
            if (sellerText != null) sellerText.text = $"Seller: {listing.SellerPlayerName}";
            if (stackText != null) stackText.text = AuctionUiFormat.StackLabel(listing.StackCount);

            if (buyButton != null)
            {
                buyButton.onClick.RemoveAllListeners();
                buyButton.onClick.AddListener(() => _auctionHouse.RequestBuyListing(listing.ListingId));
            }
        }

        private void SetupMyListingItem(GameObject item, AuctionListingRecord listing)
        {
            string displayName = AuctionUiFormat.DisplayName(listing.ItemName);

            Text nameText = item.transform.Find("NameText")?.GetComponent<Text>();
            Text priceText = item.transform.Find("PriceText")?.GetComponent<Text>();
            Text stackText = item.transform.Find("StackText")?.GetComponent<Text>();
            Text statusText = item.transform.Find("StatusText")?.GetComponent<Text>();
            Button cancelButton = item.transform.Find("CancelButton")?.GetComponent<Button>();

            if (nameText != null) nameText.text = displayName;
            if (priceText != null) priceText.text = AuctionUiFormat.ListingPrice(listing);
            if (stackText != null) stackText.text = AuctionUiFormat.StackLabel(listing.StackCount);

            if (statusText != null)
            {
                statusText.text = AuctionUiFormat.ListingStatus(listing.Status);
                statusText.color = AuctionUiFormat.ListingStatusColor(listing.Status);
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
            string displayName = AuctionUiFormat.DisplayName(tx.ItemName);

            Text nameText = item.transform.Find("NameText")?.GetComponent<Text>();
            Text priceText = item.transform.Find("PriceText")?.GetComponent<Text>();
            Text countText = item.transform.Find("CountText")?.GetComponent<Text>();
            Text dateText = item.transform.Find("DateText")?.GetComponent<Text>();

            if (nameText != null) nameText.text = displayName;
            if (priceText != null) priceText.text = $"{tx.TotalPrice:N0} gold";
            if (countText != null) countText.text = AuctionUiFormat.StackLabel(tx.StackCount);
            if (dateText != null)
                dateText.text = AuctionUiFormat.Date(tx.CompletedAtTicks);
        }

        private void ClearList(Transform content)
        {
            if (content == null) return;
            for (int i = content.childCount - 1; i >= 0; i--)
            {
                Destroy(content.GetChild(i).gameObject);
            }
        }

        private void SetupBrowseBuyOrderItem(GameObject item, AuctionBuyOrderRecord buyOrder)
        {
            string displayName = AuctionUiFormat.DisplayName(buyOrder.ItemName);

            Text nameText = item.transform.Find("NameText")?.GetComponent<Text>();
            Text priceText = item.transform.Find("PriceText")?.GetComponent<Text>();
            Text buyerText = item.transform.Find("BuyerText")?.GetComponent<Text>();
            Text quantityText = item.transform.Find("QuantityText")?.GetComponent<Text>();
            Button fulfillButton = item.transform.Find("FulfillButton")?.GetComponent<Button>();

            if (nameText != null) nameText.text = displayName;
            if (priceText != null) priceText.text = AuctionUiFormat.BuyOrderPrice(buyOrder);
            if (buyerText != null) buyerText.text = $"Buyer: {buyOrder.BuyerPlayerName}";
            if (quantityText != null) quantityText.text = AuctionUiFormat.Quantity(buyOrder);

            if (fulfillButton != null)
            {
                fulfillButton.onClick.RemoveAllListeners();
                fulfillButton.onClick.AddListener(() => OpenFulfillBuyOrder(buyOrder));
            }
        }

        private void SetupMyBuyOrderItem(GameObject item, AuctionBuyOrderRecord buyOrder)
        {
            string displayName = AuctionUiFormat.DisplayName(buyOrder.ItemName);

            Text nameText = item.transform.Find("NameText")?.GetComponent<Text>();
            Text priceText = item.transform.Find("PriceText")?.GetComponent<Text>();
            Text quantityText = item.transform.Find("QuantityText")?.GetComponent<Text>();
            Text statusText = item.transform.Find("StatusText")?.GetComponent<Text>();
            Button cancelButton = item.transform.Find("CancelButton")?.GetComponent<Button>();

            if (nameText != null) nameText.text = displayName;
            if (priceText != null) priceText.text = AuctionUiFormat.BuyOrderUnitPrice(buyOrder);
            if (quantityText != null) quantityText.text = AuctionUiFormat.Quantity(buyOrder);

            if (statusText != null)
            {
                statusText.text = AuctionUiFormat.BuyOrderStatus(buyOrder.Status);
                statusText.color = AuctionUiFormat.BuyOrderStatusColor(buyOrder.Status);
            }

            if (cancelButton != null)
            {
                cancelButton.gameObject.SetActive(buyOrder.Status == AuctionBuyOrderStatus.Active || buyOrder.Status == AuctionBuyOrderStatus.PartiallyFilled);
                cancelButton.onClick.RemoveAllListeners();
                cancelButton.onClick.AddListener(() => _auctionHouse.RequestCancelBuyOrder(buyOrder.BuyOrderId));
            }
        }

        private void OpenFulfillBuyOrder(AuctionBuyOrderRecord buyOrder)
        {
            // Open the create listing panel but pre-fill with buy order info
            // The user needs to have the item in their bag to fulfill
            // For now, we'll just log - the fulfillment requires a bag item selection
            Debug.Log($"[AuctionHouse] Fulfill buy order {buyOrder.BuyOrderId} - select item in inventory with Shift+RightClick");
            // Could open a fulfillment panel here
        }
    }
}