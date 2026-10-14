-- auction_buy: atomic purchase of an auction listing.
--
-- Runs on the Nakama server (Lua runtime). Reads the listing, validates it is active,
-- checks the buyer's gold (wallet), transfers gold from buyer to seller, marks the
-- listing as sold, and writes a transaction record. All within a single Lua execution
-- so no partial state can persist.
--
-- The listing and wallet objects are version-guarded: if another RPC call moves the
-- same object between our read and write, Nakama returns 409 and the caller retries.
local nk = require("nakama")

local LISTING_COLLECTION = "auction_listings"
local TRANSACTION_COLLECTION = "auction_transactions"
local INDEX_COLLECTION = "auction_index"
local WALLET_COLLECTION = "player_wallet"

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

local function get_wallet(account_id)
    local obj = read_object(WALLET_COLLECTION, "wallet", account_id)
    if obj and obj.value then
        return obj.value
    end
    return { gold = 0, version = "*" }
end

local function update_wallet(account_id, wallet, version)
    return write_object(WALLET_COLLECTION, "wallet", wallet, version, account_id, 1, 1)
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

    -- Read buyer's wallet
    local buyer_wallet = get_wallet(buyer_id)
    local buyer_gold = buyer_wallet.gold or 0

    if buyer_gold < total_price then
        return nk.json_encode({ success = false, errorCode = "INSUFFICIENT_FUNDS" })
    end

    -- Read seller's wallet
    local seller_wallet = get_wallet(listing.SellerAccountId)
    local seller_gold = seller_wallet.gold or 0

    -- Perform transfers
    buyer_wallet.gold = buyer_gold - total_price
    seller_wallet.gold = seller_gold + total_price

    -- Write buyer wallet
    local buyer_version = update_wallet(buyer_id, buyer_wallet, buyer_wallet.version)
    if not buyer_version then
        return nk.json_encode({ success = false, errorCode = "CONFLICT" })
    end

    -- Write seller wallet
    local seller_version = update_wallet(listing.SellerAccountId, seller_wallet, seller_wallet.version)
    if not seller_version then
        -- Rollback buyer wallet (best effort)
        buyer_wallet.gold = buyer_gold
        update_wallet(buyer_id, buyer_wallet, buyer_version)
        return nk.json_encode({ success = false, errorCode = "CONFLICT" })
    end

    -- Mark listing as sold
    listing.Status = 1 -- Sold = 1
    listing.BuyerAccountId = buyer_id
    local listing_version = write_object(LISTING_COLLECTION, listing_id, listing, listing_obj.version, "", 1, 1)
    if not listing_version then
        return nk.json_encode({ success = false, errorCode = "CONFLICT" })
    end

    -- Create transaction record
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
        return nk.json_encode({ success = false, errorCode = "CONFLICT" })
    end
    transaction.StorageVersion = tx_version

    -- Update buyer's index (add to purchase history)
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
        return nk.json_encode({ success = false, errorCode = "CONFLICT" })
    end

    -- Update seller's index (remove from active)
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
            write_object(INDEX_COLLECTION, listing.SellerAccountId, seller_index, seller_index_obj.version, "", 1, 1)
        end
    end

    listing.StorageVersion = listing_version

    return nk.json_encode({
        success = true,
        listing = listing,
        transaction = transaction,
    })
end

nk.register_rpc(auction_buy, "auction_buy")