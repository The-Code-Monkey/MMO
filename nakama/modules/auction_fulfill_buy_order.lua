-- auction_fulfill_buy_order: fulfillment of a buy order.
--
-- Runs on the Nakama server (Lua runtime). Reads the buy order, validates it is active,
-- then claims the order first with a version-guarded (CAS) write so concurrent fulfillments
-- cannot double-spend the reserved quantity. Only the winner credits the seller's wallet;
-- that wallet write is the commit point. The transaction record and index updates that
-- follow are audit metadata: if any of them fails, the fulfillment is still valid and the
-- error is logged, not surfaced as a CONFLICT (which would strand the credited gold with
-- no recovery path for the caller).
--
-- The buy order and wallet objects are version-guarded: if another RPC call moves the
-- same object between our read and write, Nakama returns 409 and the caller retries.
local nk = require("nakama")

local BUY_ORDER_COLLECTION = "auction_buy_orders"
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

local function auction_fulfill_buy_order(context, payload)
    local args = nk.json_decode(payload or "{}")
    local seller_id = args.sellerAccountId
    local buy_order_id = args.buyOrderId
    local quantity = args.quantity

    if not seller_id or not buy_order_id or not quantity then
        return nk.json_encode({ success = false, errorCode = "INVALID_ARGS" })
    end

    if quantity < 1 then
        return nk.json_encode({ success = false, errorCode = "INVALID_ARGS" })
    end

    -- Read buy order (owned by service account)
    local buy_order_obj = read_object(BUY_ORDER_COLLECTION, buy_order_id, "")
    if not buy_order_obj or not buy_order_obj.value then
        return nk.json_encode({ success = false, errorCode = "NOT_FOUND" })
    end

    local buy_order = buy_order_obj.value

    -- Validate buy order is active
    if buy_order.Status ~= 0 and buy_order.Status ~= 4 then -- Active = 0, PartiallyFilled = 4
        return nk.json_encode({ success = false, errorCode = "BUY_ORDER_NOT_ACTIVE" })
    end

    -- Check expiration
    local now_ticks = os.time() * 10000000 + 621355968000000000 -- UTC ticks approximation
    if buy_order.ExpiresAtTicks > 0 and buy_order.ExpiresAtTicks < now_ticks then
        return nk.json_encode({ success = false, errorCode = "BUY_ORDER_NOT_ACTIVE" })
    end

    -- Check quantity available
    if buy_order.QuantityRemaining < quantity then
        return nk.json_encode({ success = false, errorCode = "INSUFFICIENT_QUANTITY" })
    end

    -- Prevent self-fulfillment
    if buy_order.BuyerAccountId == seller_id then
        return nk.json_encode({ success = false, errorCode = "NOT_OWNER" })
    end

    local total_price = buy_order.PricePerUnit * quantity

    -- Read seller's wallet
    local seller_wallet = get_wallet(seller_id)
    local seller_gold = seller_wallet.gold or 0

    -- 1. CLAIM THE BUY ORDER FIRST (version-guarded) - this is the exclusivity point.
    -- If this fails, no money has moved and we return CONFLICT cleanly.
    buy_order.QuantityRemaining = buy_order.QuantityRemaining - quantity
    if buy_order.QuantityRemaining == 0 then
        buy_order.Status = 1 -- Filled = 1
    else
        buy_order.Status = 4 -- PartiallyFilled = 4
    end
    local buy_order_version = write_object(BUY_ORDER_COLLECTION, buy_order_id, buy_order, buy_order_obj.version, "", 1, 1)
    if not buy_order_version then
        return nk.json_encode({ success = false, errorCode = "CONFLICT" })
    end
    buy_order.StorageVersion = buy_order_version

    -- 2. MOVE MONEY (only the winner reaches here)
    -- Gold comes from the buy order (already reserved from buyer), not from seller.
    -- We just add to seller's wallet.
    seller_wallet.gold = seller_gold + total_price

    -- Write seller wallet
    local seller_version = update_wallet(seller_id, seller_wallet, seller_wallet.version)
    if not seller_version then
        -- Rollback buy order
        buy_order.QuantityRemaining = buy_order.QuantityRemaining + quantity
        buy_order.Status = buy_order.QuantityRemaining == 0 and 1 or 4
        write_object(BUY_ORDER_COLLECTION, buy_order_id, buy_order, buy_order_version, "", 1, 1)
        return nk.json_encode({ success = false, errorCode = "CONFLICT" })
    end

    -- 3. Create transaction record
    local transaction_id = nk.uuid_v4()
    local transaction = {
        TransactionId = transaction_id,
        ListingId = "",
        BuyOrderId = buy_order_id,
        SellerAccountId = seller_id,
        BuyerAccountId = buy_order.BuyerAccountId,
        ItemName = buy_order.ItemName,
        StackCount = quantity,
        PricePerUnit = buy_order.PricePerUnit,
        TotalPrice = total_price,
        CompletedAtTicks = now_ticks,
    }
    local tx_version = write_object(TRANSACTION_COLLECTION, transaction_id, transaction, "*", "", 1, 1)
    if not tx_version then
        nk.logger_error("auction_fulfill_buy_order: transaction record write failed for " .. buy_order_id .. " (audit gap)")
    else
        transaction.StorageVersion = tx_version
    end

    -- 4. Update buyer's index (add to purchase history, remove buy order if filled)
    local buyer_index_obj = read_object(INDEX_COLLECTION, buy_order.BuyerAccountId, "")
    local buyer_index = buyer_index_obj and buyer_index_obj.value or { AccountId = buy_order.BuyerAccountId, ActiveListingIds = {}, ActiveBuyOrderIds = {}, PurchaseHistoryIds = {} }
    if not buyer_index.PurchaseHistoryIds then
        buyer_index.PurchaseHistoryIds = {}
    end
    table.insert(buyer_index.PurchaseHistoryIds, 1, transaction_id) -- prepend for recent-first
    if #buyer_index.PurchaseHistoryIds > 100 then
        local new_history = {}
        for i = 1, 100 do
            new_history[i] = buyer_index.PurchaseHistoryIds[i]
        end
        buyer_index.PurchaseHistoryIds = new_history
    end
    if buy_order.Status == 1 then -- Filled
        if buyer_index.ActiveBuyOrderIds then
            local new_buy_orders = {}
            for _, id in ipairs(buyer_index.ActiveBuyOrderIds) do
                if id ~= buy_order_id then
                    table.insert(new_buy_orders, id)
                end
            end
            buyer_index.ActiveBuyOrderIds = new_buy_orders
        end
    end
    local buyer_index_version = write_object(INDEX_COLLECTION, buy_order.BuyerAccountId, buyer_index, buyer_index_obj and buyer_index_obj.version or "*", "", 1, 1)
    if not buyer_index_version then
        nk.logger_error("auction_fulfill_buy_order: buyer index write failed for " .. buy_order.BuyerAccountId .. " (audit gap)")
    end

    -- 5. Update seller's index (add to purchase history)
    local seller_index_obj = read_object(INDEX_COLLECTION, seller_id, "")
    local seller_index = seller_index_obj and seller_index_obj.value or { AccountId = seller_id, ActiveListingIds = {}, ActiveBuyOrderIds = {}, PurchaseHistoryIds = {} }
    if not seller_index.PurchaseHistoryIds then
        seller_index.PurchaseHistoryIds = {}
    end
    table.insert(seller_index.PurchaseHistoryIds, 1, transaction_id)
    if #seller_index.PurchaseHistoryIds > 100 then
        local new_history = {}
        for i = 1, 100 do
            new_history[i] = seller_index.PurchaseHistoryIds[i]
        end
        seller_index.PurchaseHistoryIds = new_history
    end
    local seller_index_version = write_object(INDEX_COLLECTION, seller_id, seller_index, seller_index_obj and seller_index_obj.version or "*", "", 1, 1)
    if not seller_index_version then
        nk.logger_error("auction_fulfill_buy_order: seller index write failed for " .. seller_id .. " (audit gap)")
    end

    return nk.json_encode({
        success = true,
        buyOrder = buy_order,
        transaction = transaction,
    })
end

nk.register_rpc(auction_fulfill_buy_order, "auction_fulfill_buy_order")