-- auction_buy: purchase of an auction listing.
--
-- Runs on the Nakama server (Lua runtime). Reads the listing, validates it is active, then
-- claims it first with a version-guarded (CAS) write to "Sold" so exactly one buyer wins.
-- Gold settlement is not done here: the buyer's reserve is held by the buyer's own zone and
-- the seller is credited by the auction zone after the claim wins (see AuctionService). The
-- transaction record and index updates that follow are audit metadata: if any of them fails,
-- the purchase is still valid and the error is logged, not surfaced as a CONFLICT.
--
-- The listing object is version-guarded: if another RPC call moves the same object between
-- our read and write, Nakama returns 409 and the caller retries.
local nk = require("nakama")

local LISTING_COLLECTION = "auction_listings"
local TRANSACTION_COLLECTION = "auction_transactions"
local INDEX_COLLECTION = "auction_index"

local function read_object(collection, key, user_id)
    local objects = nk.storage_read({
        { collection = collection, key = key, user_id = user_id }
    })
    if objects and #objects > 0 and objects[1].value then
        local obj = objects[1]
        obj.value = nk.json_decode(obj.value)
        return obj
    end
    return nil
end

local function write_object(collection, key, value, version, user_id, permission_read, permission_write)
    local obj = {
        collection = collection,
        key = key,
        value = nk.json_encode(value),
        version = version or "*",
        permission_read = permission_read or 1,
        permission_write = permission_write or 1,
    }
    if user_id then
        obj.user_id = user_id
    end
    local result = nk.storage_write({ obj })
    if result and #result > 0 then
        return result[1].version
    end
    return nil
end

local function delete_object(collection, key, user_id)
    nk.storage_delete({
        { collection = collection, key = key, user_id = user_id }
    })
end

local function auction_buy(context, payload)
    local args = nk.json_decode(payload or "{}")
    local buyer_id = args.buyerAccountId
    local listing_id = args.listingId

    if not buyer_id or not listing_id then
        return nk.json_encode({ success = false, errorCode = "INVALID_ARGS" })
    end

    -- Read listing (owned by service account)
    local listing_obj = read_object(LISTING_COLLECTION, listing_id, "")
    if not listing_obj or not listing_obj.value then
        return nk.json_encode({ success = false, errorCode = "NOT_FOUND" })
    end

    local listing = listing_obj.value

    -- Validate listing is active
    if listing.Status ~= 0 then -- Active = 0
        return nk.json_encode({ success = false, errorCode = "NOT_ACTIVE" })
    end

    -- Check expiration
    local now_ticks = os.time() * 10000000 + 621355968000000000 -- UTC ticks approximation
    if listing.ExpiresAtTicks > 0 and listing.ExpiresAtTicks < now_ticks then
        return nk.json_encode({ success = false, errorCode = "NOT_ACTIVE" })
    end

    -- Prevent self-purchase
    if listing.SellerAccountId == buyer_id then
        return nk.json_encode({ success = false, errorCode = "NOT_OWNER" })
    end

    local total_price = listing.PricePerUnit * listing.StackCount

    -- 1. CLAIM THE LISTING FIRST (version-guarded) - this is the exclusivity point.
    -- If this fails, nothing has moved and we return CONFLICT cleanly.
    listing.Status = 1 -- Sold = 1
    listing.BuyerAccountId = buyer_id
    local listing_version = write_object(LISTING_COLLECTION, listing_id, listing, listing_obj.version, "", 1, 1)
    if not listing_version then
        return nk.json_encode({ success = false, errorCode = "CONFLICT" })
    end
    listing.StorageVersion = listing_version

    -- 2. Create transaction record
    local transaction_id = nk.uuid_v4()
    local transaction = {
        TransactionId = transaction_id,
        ListingId = listing_id,
        SellerAccountId = listing.SellerAccountId,
        BuyerAccountId = buyer_id,
        ItemName = listing.ItemName,
        StackCount = listing.StackCount,
        PricePerUnit = listing.PricePerUnit,
        TotalPrice = total_price,
        CompletedAtTicks = now_ticks,
    }
    local tx_version = write_object(TRANSACTION_COLLECTION, transaction_id, transaction, "*", "", 1, 1)
    if not tx_version then
        nk.logger_error("auction_buy: transaction record write failed for " .. listing_id .. " (audit gap)")
    else
        transaction.StorageVersion = tx_version
    end

    -- 3. Update buyer's index (add to purchase history)
    local buyer_index_obj = read_object(INDEX_COLLECTION, buyer_id, "")
    local buyer_index = buyer_index_obj and buyer_index_obj.value or { AccountId = buyer_id, ActiveListingIds = {}, PurchaseHistoryIds = {} }
    if not buyer_index.PurchaseHistoryIds then
        buyer_index.PurchaseHistoryIds = {}
    end
    table.insert(buyer_index.PurchaseHistoryIds, 1, transaction_id) -- prepend for recent-first
    -- Keep only last 100
    if #buyer_index.PurchaseHistoryIds > 100 then
        local new_history = {}
        for i = 1, 100 do
            new_history[i] = buyer_index.PurchaseHistoryIds[i]
        end
        buyer_index.PurchaseHistoryIds = new_history
    end
    local buyer_index_version = write_object(INDEX_COLLECTION, buyer_id, buyer_index, buyer_index_obj and buyer_index_obj.version or "*", "", 1, 1)
    if not buyer_index_version then
        nk.logger_error("auction_buy: buyer index write failed for " .. buyer_id .. " (audit gap)")
    end

    -- 4. Update seller's index (remove from active)
    local seller_index_obj = read_object(INDEX_COLLECTION, listing.SellerAccountId, "")
    if seller_index_obj and seller_index_obj.value then
        local seller_index = seller_index_obj.value
        if seller_index.ActiveListingIds then
            local new_active = {}
            for _, id in ipairs(seller_index.ActiveListingIds) do
                if id ~= listing_id then
                    table.insert(new_active, id)
                end
            end
            seller_index.ActiveListingIds = new_active
            local seller_index_version = write_object(INDEX_COLLECTION, listing.SellerAccountId, seller_index, seller_index_obj.version, "", 1, 1)
            if not seller_index_version then
                nk.logger_error("auction_buy: seller index write failed for " .. listing.SellerAccountId .. " (audit gap)")
            end
        end
    end

    return nk.json_encode({
        success = true,
        listing = listing,
        transaction = transaction,
    })
end

nk.register_rpc(auction_buy, "auction_buy")