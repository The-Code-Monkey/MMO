using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MMO.Inventory;
using MMO.Persistence;
using MMO.Social;
using UnityEngine;

namespace MMO.Auction
{
    /// <summary>
    /// Post-commit auction delivery: grants an item into the buyer's bag or system mail, and credits
    /// a seller's wallet or mailbox. Runs only after the authoritative gold/item move succeeded.
    /// </summary>
    internal sealed class AuctionDeliveryService
    {
        /// <summary>Post-commit mail deliveries retry a transient backend failure this many times.</summary>
        private const int DeliveryMailAttempts = 3;

        private readonly SocialHubService _socialHub;
        private readonly Func<string, PlayerInventory> _getBag;

        public AuctionDeliveryService(SocialHubService socialHub, Func<string, PlayerInventory> getBag)
        {
            _socialHub = socialHub;
            _getBag = getBag;
        }

        /// <summary>
        /// Grants the won item to the buyer: same-zone, with bag space, straight into the bag;
        /// otherwise into a claimable mail. An item is always exactly one holder — escrow, then bag
        /// or mailbox, never both.
        /// </summary>
        public async Task DeliverPurchaseAsync(
            string buyerAccountId, AuctionListingRecord listing, CancellationToken ct)
        {
            ItemDefinition def = ItemDefinitionRegistry.Resolve(listing.ItemName);
            if (def == null)
            {
                Debug.LogError($"[Auction] Cannot deliver listing {listing.ListingId}: unknown item {listing.ItemName}.");
                return;
            }

            ItemInstance item = ItemInstance.Create(def, 0, 0, listing.StackCount);
            AuctionDeliveryTarget target = ResolveDeliveryTarget(buyerAccountId, item);

            await DeliverItemAsync(
                buyerAccountId,
                item,
                target,
                "Auction purchase",
                $"You won listing {listing.ListingId}.");
        }

        /// <summary>
        /// Decides where the buyer can receive an item: their bag when they are on this zone and
        /// have room, otherwise their mailbox. Auction mail bypasses the inbox cap.
        /// </summary>
        public AuctionDeliveryTarget ResolveDeliveryTarget(string accountId, ItemInstance item)
        {
            PlayerInventory bag = _getBag(accountId);
            bool buyerOnThisZone = bag != null;
            bool hasBagSpace = buyerOnThisZone && HasBagSpace(bag, item);
            return AuctionDeliveryRules.Choose(buyerOnThisZone, hasBagSpace);
        }

        /// <summary>
        /// Hands the item to the buyer at the resolved target, falling back to system mail when a
        /// bag grant loses a race. Mail retries a transient backend failure and ignores cancellation
        /// because this runs after the gold has already moved.
        /// </summary>
        public async Task<bool> DeliverItemAsync(
            string accountId,
            ItemInstance item,
            AuctionDeliveryTarget target,
            string subject,
            string body)
        {
            if (target == AuctionDeliveryTarget.Bag && TryGrantToBag(accountId, item))
                return true;

            var attachments = new List<ItemSaveData> { ToSaveData(item) };

            for (int attempt = 0; attempt < DeliveryMailAttempts; attempt++)
            {
                MailRule rule;
                try
                {
                    rule = await _socialHub.SendMailAsync(
                        accountId,
                        "auction",
                        subject,
                        body,
                        currency: 0,
                        attachments: attachments,
                        ignoreInboxCap: true,
                        ct: CancellationToken.None);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Auction] Auction mail delivery threw for {accountId}: {e.Message}");
                    rule = MailRule.BackendUnavailable;
                }

                if (rule == MailRule.Ok)
                    return true;

                if (rule != MailRule.BackendUnavailable && rule != MailRule.Conflict)
                {
                    Debug.LogError($"[Auction] Could not deliver {item.ItemName} x{item.StackCount} to {accountId}: {rule}.");
                    return false;
                }

                if (attempt + 1 < DeliveryMailAttempts)
                    await Task.Delay(50 * (1 << attempt));
            }

            Debug.LogError($"[Auction] Auction mail delivery failed after {DeliveryMailAttempts} attempts for {accountId}: {item.ItemName} x{item.StackCount} is stranded.");
            return false;
        }

        public async Task CreditSellerAsync(
            string sellerAccountId,
            long gold,
            CancellationToken ct,
            string sellerZoneId = null)
        {
            if (string.IsNullOrEmpty(sellerAccountId) || gold <= 0)
                return;

            // Territory tax comes off the seller's side, before the credit: the buyer paid the
            // listing price and the owning guild's cut is withheld from what the seller receives.
            // A tax failure must never block the sale: the seller is paid in full and the guild
            // loses one fee, which is far better than a payer whose gold vanishes.
            long proceeds = gold;
            try
            {
                proceeds = await _socialHub.ApplyTerritoryTaxAsync(sellerAccountId, sellerZoneId, gold, ct);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Auction] Territory tax for {sellerAccountId} failed; paying gross: {e.Message}");
                proceeds = gold;
            }

            if (proceeds <= 0)
                return;

            if (_socialHub.TryGetWallet(sellerAccountId, out PlayerWallet sellerWallet))
            {
                sellerWallet.AddGold(proceeds);
                return;
            }

            MailRule result = await _socialHub.SendMailAsync(
                sellerAccountId,
                "auction",
                "Auction sale",
                "Your auction sale was completed.",
                currency: proceeds,
                attachments: null,
                ignoreInboxCap: true,
                ct: ct);

            if (result != MailRule.Ok)
                Debug.LogError($"[Auction] Could not credit seller {sellerAccountId} with {proceeds} gold by mail: {result}.");
        }

        private bool HasBagSpace(PlayerInventory bag, ItemInstance item)
        {
            if (bag == null || item.IsEmpty)
                return false;

            var grid = new InventoryGrid(PlayerInventory.GridWidth, PlayerInventory.GridHeight);
            return grid.TryFindFreeSpace(bag.Items, item.Width, item.Height, out _, out _);
        }

        private bool TryGrantToBag(string accountId, ItemInstance item)
        {
            PlayerInventory bag = _getBag(accountId);
            if (bag == null || item.IsEmpty)
                return false;

            var grid = new InventoryGrid(PlayerInventory.GridWidth, PlayerInventory.GridHeight);
            if (!grid.TryFindFreeSpace(bag.Items, item.Width, item.Height, out int x, out int y))
                return false;

            item.GridX = x;
            item.GridY = y;
            return bag.TryAddExistingItem(item, x, y);
        }

        private static ItemSaveData ToSaveData(ItemInstance item)
        {
            return new ItemSaveData
            {
                ItemName = item.ItemName,
                InstanceId = string.IsNullOrEmpty(item.InstanceId) ? ItemInstance.NewInstanceId() : item.InstanceId,
                Width = item.Width,
                Height = item.Height,
                StackCount = item.StackCount,
                Durability = item.Durability,
                EquipmentSlot = -1,
            };
        }
    }
}
