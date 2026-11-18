# 16 — Direct trade and guild territory

Two small systems that reuse the existing social rules, repository and player shell patterns:
player-to-player trade, and weekly guild territory capture. Neither adds a backend transaction
primitive, and neither can take an item or a flag outside the server's authority.

## 1. Direct trade

### The split

| Layer | Type | Responsibility |
|---|---|---|
| Pure rules | `TradeSession` (`Assets/Scripts/Social/TradeSession.cs:52`) | Offers, locks, derived state, duplicate-instance refusal, exchange routing. |
| Pure guard | `TradeRules.OffersStillHeld` (`Assets/Scripts/Social/TradeRules.cs:7`) | Re-checks every offered instance against the live bag. |
| Network shell | `PlayerTrade` (`Assets/Scripts/Social/PlayerTrade.cs:35`) | Owner-checked RPCs, inventory guard, the atomic swap, the replicated `TradeView`. |
| UI source | `TradeOfferSource` (`Assets/Scripts/Social/TradeOfferSource.cs:12`) | Read-only grid over one offer. |
| UI | `TradeFrameView` / `TradeFrameFactory` | Grids, Lock / Cancel, the `T` key picker. |

### No escrow

Items stay in their bags until both sides lock. At that point the server re-validates both offers
against the live `PlayerInventory` lists and moves both sides in one tick. If the bag no longer
holds an offered instance, or either bag cannot accept the incoming items, nothing is swapped and
the session is cancelled. This is deliberate: the save trigger persists inventory changes
immediately, so an in-memory escrow would write "items missing mid-trade" to the backend.

Any inventory change while a session is open cancels it. That includes equipping, moving, looting
or another social write touching either bag. The lock therefore always describes the offer on
screen.

### The swap

1. `TradeSession.Lock` succeeds for the second side and the session becomes `BothLocked`.
2. Both offers are checked with `TradeRules.OffersStillHeld`.
3. `TradeSession.TryBuildExchange` routes each offer to the other account and refuses a shared
   `InstanceId`.
4. The shell calls `PlayerInventory.TryRemoveItem` on both sides, then `TryAddExistingItem` on the
   other side.
5. Every moved item is tracked. A failure removes anything already added and restores both original
   bags before the session is cancelled.
6. Only after both bags moved does `TradeSession.Complete()` run.

`TryRemoveItem` (`Assets/Scripts/Inventory/PlayerInventory.cs:437`) resolves by `InstanceId` rather
than by bag cell, and releases the item's `ItemOwnershipRegistry` entry — the same single-writer
rule every other inventory mutation follows.

Gold is not traded in this version.

## 2. Guild territory

### The flag

`ClaimFlag` (`Assets/Scripts/Gameplay/ClaimFlag.cs:11`) is a plain scene `MonoBehaviour` with a
`BoxCollider`. It is not a `NetworkObject` and has no prefab: territory is zone geometry, like
`ZoneRiskConfig`. The server owns its `OwnerGuildId`; clients receive the value through
`TerritoryStateBroadcast`.

`Assets/Editor/ZoneSceneBaker.cs` authors the Wilderness flag with id `wilderness-keep` and a
Saturday/Sunday 18:00–22:00 UTC window.

### The rules

`TerritoryRules` (`Assets/Scripts/Social/TerritoryRules.cs:32`) is pure:

- An empty day list, invalid minutes, or `StartMinuteUtc == EndMinuteUtc` fails closed.
- `Start < End` is a same-day window.
- `Start > End` crosses midnight; the listed day is the day the window starts.
- An unowned flag is capturable at any time.
- An owned flag is capturable only inside the window; the current owner is refused.
- A guild-less player is refused. War declarations and territory effects are out of scope.

### Capture

`PlayerTerritory` (`Assets/Scripts/Social/PlayerTerritory.cs:18`) sends an intent with no target.
The server finds the `ClaimFlag` containing the player's authoritative position
(`PlayerMover.LastAuthoritativePosition`), then calls
`SocialHubService.CaptureTerritoryAsync` (`Assets/Scripts/Social/SocialHubService.cs:1422`).

The service confirms the player is a member of their indexed guild, then runs a bounded CAS loop:
load the territory, run `TerritoryRules.CanCapture`, write the owner and raise
`TerritoryChanged`. The record is `TerritoryRecord` in the `territories` collection
(`NakamaSocialRepository.cs:27`), keyed by territory id, versioned exactly like a guild bank.

On server spawn the shell broadcasts every flag's owner so late joiners are current; a capture
broadcasts the changed flag to every client, and each local `ClaimFlag` updates its cached owner.

### Tax and treasury settlement

A claimed territory can charge a tax on auction sales made inside it. The zone's
`SocialHubService.TerritoryId` names the territory its auction house stands in (empty means
untaxed); the owner guild and rate are read from the durable `TerritoryRecord` on each sale, never
cached. `ApplyTerritoryTaxAsync` withholds `TaxationRules.TaxOn(gross, rate)` from the seller's side
and returns the net, which `AuctionDeliveryService.CreditSellerAsync` credits instead of the gross.
A sale whose seller is listed in another zone is not taxed by this zone's auction house.

The withheld amount becomes a `GuildBankLedgerEntry` with `Action = Tax` and a stable `EntryId`.
`SettleTerritoryRevenueAsync` pays every due entry into `GuildBankRecord.Vault` through
`GuildBankRules.DepositCurrency`, and records the entry id on the bank's
`SettledTerritoryEntries`. That id list is the idempotence marker: a retry after a lost ledger write,
or a second zone running the same pass, sees the id and pays nothing again. The bank's
`TerritoryRevenue` total is shown read-only on the guild bank frame. Direct trade still carries no
gold, so only auctions — the one real-money flow inside a territory — are taxed.

## 3. Wallet and bank vaults

Currency is a first-class wallet, not a stat on the player. `WalletAccount`
(`Assets/Scripts/Economy/WalletAccount.cs`) holds a list of `CurrencyBalance` entries keyed by
currency id (`gold`, `silver`, `faction_marks` — `CurrencyIds`). The list, not a dictionary,
because `JsonUtility` cannot serialize a map and the wallet is replicated and saved as JSON.

`WalletRules` (`Assets/Scripts/Economy/WalletRules.cs`) is the pure heart: `Debit`, `Credit`,
`Transfer` (with a compensating rollback), `Set` and `Sanitize`. A balance is never negative and a
credit never wraps. `PlayerWallet` (`Assets/Scripts/Auction/PlayerWallet.cs`) is the server-only
shell; it publishes the wallet and the personal bank vault as JSON `SyncVar`s and keeps the legacy
gold accessors (`Gold`, `AddGold`, `RemoveGold`, `TrySpend`) writing `CurrencyIds.Gold`, so the
auction house and death penalties are unchanged.

**Currency items.** A `CurrencyData` asset (`Assets/Scripts/Economy/CurrencyData.cs`) names a
currency id, a display name, the `ItemDefinition` that converts into it, and the units granted per
item. `CurrencyRegistry` resolves it by id or by item name. `PlayerWallet.ConvertItem` is the
server flow: verify the carried item and the trusted `CurrencyData`, remove the item, then credit
the wallet, restoring the item if the credit is refused. The client only sends an instance id.

**Guild vault.** `GuildBankRecord.Vault` is a `List<CurrencyBalance>` on the existing bank record.
Deposits and withdrawals reuse the item bank's rank permissions and value quota
(`GuildBankRules.CanDepositCurrency` / `CanWithdrawCurrency`, `DepositCurrency` / `WithdrawCurrency`).
`SocialHubService.DepositCurrencyAsync` / `WithdrawCurrencyAsync` write the vault first under the
bank CAS loop, then move the wallet; a lost wallet step is compensated so currency is never created
or destroyed. The ledger records the move with `ItemName` set to the currency id.

**Personal bank vault.** `PlayerSaveRecord.BankVault` is a separate balance list. `PlayerWallet`
moves currency between the carried wallet and the vault (`BankDeposit` / `BankWithdraw`,
`RequestBankDeposit` / `RequestBankWithdraw`). Banked currency is not carried and is not lost on
death.

Inter-player loans and credit are **not** part of the game; only deposits, withdrawals and vaults
exist.

### Bank tellers

A `BankTeller` (`Assets/Scripts/Banking/BankTeller.cs`) is a capsule ("pill") placeholder standing
in any zone. Clicking one sends the open intent; the server refuses it outside
`BankTeller.InteractRange`, and the pointer adds the "Out of range." advice while still asking. The
prefab is generic: `BankTeller.Kind` decides whether the server opens the personal or the guild bank.

`PlayerBank` (`Assets/Scripts/Banking/PlayerBank.cs`) owns the personal item grid and the open
flow. It checks the player is within `BankTeller.InteractRange` of the teller on open **and** on
every deposit/withdraw, so a forged RPC cannot move items from across the map. Items are stored in
`PlayerSaveRecord.BankItems` and replicated to the owner as JSON.

The window is one `BankFrameView` (`Assets/Scripts/UI/BankFrameView.cs`) bound to an
`IBankSource`: `PersonalBankSource` (personal items + `PlayerWallet.BankVault`) or
`GuildBankSource` (guild tabs + `GuildBankRecord.Vault`, with the rank editor). Left = the bank's
8x6 item grid and its currency strip (`"100 Gold - 10 Silver - 2 Copper"`, display-only); right =
the backpack. Right-click a bank cell to withdraw, a backpack cell to deposit. A guild bank's
"more storage" is its tabs, and its permissions/quotas are unchanged.

Tellers are scene `BankTellerMarker`s spawned by `WorldBootstrap`; `ZoneSceneBaker` places one
personal and one guild teller in SafeZone.

### Auction delivery

Both auction paths hand the item to its buyer through the same rule
(`AuctionDeliveryRules.Choose`, `Assets/Scripts/Auction/AuctionDeliveryRules.cs`): the bag when the
buyer is on this zone with room, otherwise a claimable mail. The rule is pure, so the EditMode
suite covers every branch.

Auction mail is a system delivery: `SendMailAsync(..., ignoreInboxCap: true)` lets it land in a full
inbox, so the buyer can always take an item they have paid for.

Both auction RPCs are atomic at the backend. `auction_buy.lua` and
`auction_fulfill_buy_order.lua` claim the listing/order and write the buyer's delivery mail in the
same RPC, before they return success. If the mail write fails, the script restores the claim and
returns `DELIVERY_FAILED`, so a claim can never exist without a durable item. The zone then removes
the item from the seller's bag and claims the mail into the bag when the buyer is local; a buyer who
is away or has no room keeps the mail for the sweep or the mailbox UI. A zone failure after the RPC
cannot lose the item.

Each attempt carries an id in its mail body. If the RPC response itself is lost, the zone looks for
that attempt's mail and finishes the settlement (claim into the bag, seller credit, seller item
removal) instead of refunding or delivering twice. When the mail is absent, the call really failed
and the normal failure path runs.

Buyer debits are saved with a `PendingWalletReservations` marker before the claim or buy-order write.
After a restart, the joining zone checks listing/order state and clears committed reservations; it
refunds uncommitted purchases and the remaining value of cancelled or expired buy orders.

The zone-side helper `DeliverItemAsync` remains only as a fallback for a backend that predates the
atomic `deliveryMailId`; it uses `AuctionDeliveryRules` and a C# mail write that bypasses the inbox
cap and retries a transient backend failure.

### Auction search cache

Realm-wide search reads `MarketSearchIndex` (`Assets/Scripts/Persistence/SearchIndex.cs`), one shared
record in the `market_search_index` collection keyed `realm`. It is a cache: `auction_listings` stays
the truth and expiry/price are re-checked on purchase. Creating, cancelling or selling a listing
upserts or removes its `SearchRow`; a `MarketChanged` hint then tells peer auction zones to re-read
that one row. When the cache is missing or older than five minutes, `MarketIndexService` rebuilds it
from the authoritative listings. `MarketIndexRules` is the pure kernel, so the maintenance and
reconcile paths are covered offline.

The browse UI is a thin client over that kernel. `AuctionFilterBar` cycles the category and sort,
takes text/seller/tier/price boxes, and builds an `AuctionQuery` through `AuctionQueryMapper`; blank
boxes mean "no bound", never zero. `AuctionHouseView` sends the query through
`PlayerAuctionHouse.RequestSearch(AuctionQuery)`, maps the returned `SearchRow`s onto the existing
listing row binder, and never filters locally. Saved searches are a per-account
`AuctionSavedSearchRecord` in `auction_saved_searches`, capped at ten and de-duplicated by
`AuctionSavedSearchRules` (paging is normalised away), with one-click chips in the bar.

## Common tasks

- **Tune trade range.** Edit `PlayerTrade.Range`. The client picker uses the same constant, but the
  server check is the one that decides.
- **Reject a trade that references a missing item.** No code change: `OffersStillHeld` runs before
  every lock and again before the swap.
- **Add a new claim flag.** Place a scene object with a `BoxCollider` and `ClaimFlag`, set the id
  and window through `Configure`, and give it a unique `TerritoryId`. No prefab or marker is needed.
- **Change the vulnerability window.** Edit the baker's `CreateClaimFlag`, or configure the scene
  object directly. The server reads the window from the flag it finds.
- **Read the current owner.** `SocialHubService.LoadTerritoryAsync(id)` returns the record;
  `ClaimFlag.OwnerGuildId` is the scene's cached copy.
- **Test trade or territory offline.** `TradeSessionTests` and `TerritoryRulesTests` are pure.
  `SocialHubServiceTests` covers capture CAS retry and refusal with the in-memory repository.
