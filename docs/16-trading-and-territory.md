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

`TryRemoveItem` (`Assets/Scripts/Inventory/PlayerInventory.cs:450`) resolves by `InstanceId` rather
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
