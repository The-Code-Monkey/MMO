using MMO.Auction;
using MMO.Client;
using MMO.Inventory;
using MMO.Social;
using UnityEngine;
using UInput = UnityEngine.Input;

namespace MMO.UI
{
    /// <summary>
    /// Simple component to open the auction house UI from a key press or NPC interaction.
    /// Only works in zones that have an auction house (central trading hubs).
    /// </summary>
    public sealed class AuctionHouseOpener : MonoBehaviour
    {
        [SerializeField] private KeyCode _openKey = KeyCode.B;
        [SerializeField] private AuctionHouseView _auctionHouseView;

        private PlayerAuctionHouse _auctionHouse;
        private PlayerInventory _inventory;

        private void Start()
        {
            _auctionHouse = GetComponent<PlayerAuctionHouse>();
            _inventory = GetComponent<PlayerInventory>();

            if (_auctionHouse == null)
                Debug.LogWarning("[AuctionHouseOpener] No PlayerAuctionHouse component found on this object.");
            if (_inventory == null)
                Debug.LogWarning("[AuctionHouseOpener] No PlayerInventory component found on this object.");
            if (_auctionHouseView == null)
                Debug.LogWarning("[AuctionHouseOpener] No AuctionHouseView assigned.");
        }

        private void Update()
        {
            bool pressed = ClientInput.Current != null
                ? ClientInput.Current.WasPressed(GameAction.Auction)
                : UInput.GetKeyDown(_openKey);

            if (pressed)
            {
                ToggleAuctionHouse();
            }
        }

        public void ToggleAuctionHouse()
        {
            if (_auctionHouseView == null || _auctionHouse == null || _inventory == null)
                return;

            if (!IsInAuctionHouseZone())
            {
                Debug.Log("[AuctionHouse] This zone does not have an auction house. Travel to a central trading hub.");
                return;
            }

            if (_auctionHouseView.gameObject.activeSelf)
            {
                _auctionHouseView.gameObject.SetActive(false);
            }
            else
            {
                _auctionHouseView.gameObject.SetActive(true);
                _auctionHouseView.Initialize(_auctionHouse, _inventory);
            }
        }

        /// <summary>
        /// Opens the auction house create listing panel for a specific bag cell.
        /// Called by the inventory UI when right-clicking an item.
        /// </summary>
        public void OpenCreateListing(int bagX, int bagY)
        {
            if (!IsInAuctionHouseZone())
            {
                Debug.Log("[AuctionHouse] This zone does not have an auction house. Travel to a central trading hub.");
                return;
            }

            if (_auctionHouseView == null || !_auctionHouseView.gameObject.activeSelf)
            {
                ToggleAuctionHouse();
            }

            _auctionHouseView.OpenCreateListing(bagX, bagY);
        }

        private bool IsInAuctionHouseZone()
        {
            return SocialHubService.Instance != null && SocialHubService.Instance.HasAuctionHouse;
        }
    }
}