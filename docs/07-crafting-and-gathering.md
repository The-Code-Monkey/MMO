# 07 — Crafting and gathering

Two related systems: **gathering** (harvest resource nodes for materials) and **crafting**
(consume materials at a station to make gear). Both are server-authoritative and both keep their
decision logic in pure, testable classes.

## 1. Crafting data: RecipeData

A recipe is a `ScriptableObject` created with `Assets > Create > MMO > Recipe Data`.
`RecipeData` (`Assets/Scripts/Crafting/RecipeData.cs:16`) has these fields:

| Field | Meaning | Line |
|---|---|---|
| `RecipeName` | Registry key and display name. | `:18` |
| `Ingredients` | An array of `RecipeIngredient` (item + count). | `:19` |
| `Output` | The `ItemDefinition` produced. | `:20` |
| `OutputCount` | How many of the output are produced (min 1). | `:21` |
| `StationType` | Which station is required. | `:22` |
| `RequiredCraftingLevel` | Minimum crafting level. | `:23` |
| `CraftingXp` | XP awarded on success. | `:24` |
| `CraftDurationSeconds` | Seconds a craft takes; `0` means instant and is the default. | `:25` |

`RecipeIngredient` (`RecipeData.cs:8`) is a small serialisable struct: `Item` (an `ItemDefinition`)
and `Count`.

### Self-registration

Like items, recipes self-register. `RecipeData.OnEnable` (`:44`) registers in `RecipeRegistry` when
named, and `OnDisable` (`:50`) unregisters. `RecipeRegistry`
(`Assets/Scripts/Crafting/RecipeRegistry.cs:6`) mirrors `ItemDefinitionRegistry`: `Register` (`:12`),
`Unregister` (`:31`), `Resolve` (`:40`, falling back to `Resources.Load<RecipeData>` under
`Resources/Recipes`) and `Clear` (`:57`). `All` (`:65`) returns a snapshot `IReadOnlyList<RecipeData>`
of every registered recipe (nulls skipped) — the server uses it to build the crafting payload below
without ever handing assets to a client.

Self-registration alone only fires for a recipe something else has already loaded, so the server
walks the folder once at start-up: `RecipeRegistry.LoadAllFromResources()` (`:101`) loads every
`Resources/Recipes/*.asset` (via `Resources.LoadAll`, folder name in `RecipesFolder` at `:86`) and
registers the set. `WorldBootstrap.SpawnWorld` calls it before materialising the zone, so a shipped
recipe reaches `All` — and therefore both the offer payload and `PlayerCrafting.TryCraft` — without
any per-recipe wiring.

### Shipped recipes

`Assets/Resources/Recipes/` holds the recipe assets the game ships. The first is the tutorials'
refining step, `Iron_Ingot.asset`: two `Assets/Resources/Items/Iron_Ore.asset` refine into one
`Assets/Resources/Items/Iron_Ingot.asset` (a 1×1 good) at a `Furnace`, for 5 crafting XP. It is what
the tutorial furnace marker stands next to, so a brand-new character can gather ore and refine it
without leaving the starting zone. `TutorialForgeContentTests` asserts each of those facts against
the real assets.

## 2. Crafting stations

`CraftingStationType` (`Assets/Scripts/Crafting/CraftingStationType.cs:5`) is an enum:

| Value | Meaning | Line |
|---|---|---|
| `None` (0) | Not craftable anywhere. | `:7` |
| `Furnace` (1) | Refining: ore → bar. | `:8` |
| `Anvil` (2) | Crafting: bar → gear. | `:9` |
| `Alchemy` (3) | Brewing: reagents → potions. | `:10` |

`CraftingStation` (`Assets/Scripts/Crafting/CraftingStation.cs:7`) is a `NetworkBehaviour` with a
replicated station type. The server sets it with `Configure(CraftingStationType)` (`:32`). Clicking
the station calls `RequestOpenFromLocalPlayer` (`:36`), which finds the local `PlayerCrafting` and
invokes `RequestOpenStation` — that now starts a **server round-trip**: the client asks, the server
decides what the station offers and sends it back (section 5). The client never reads recipes from
its own registry to render a panel.

The model is data-driven too: the prefab carries a `Furnace` and an `Anvil` child under `Visuals/`,
and `ApplyVisuals` shows the one matching the replicated type (with the furnace as the fallback for
`None`/`Alchemy`). The anvil is procedural; the furnace hosts the imported forge model. New station
types add a model child and a case, not a separate prefab.

### Clicking a station: in reach, or a word of advice

A click on a station is a request to open it, not a request to walk anywhere. The server refuses an
open request from outside the crafting reach, so `PlayerWorldPointer` checks the same reach on the
client and, when the player is too far away, writes "Out of range." to the chat log — then sends the
request anyway. The check is advice, never a gate: a stale client position must not be able to block
an interaction the server would allow.

There is no auto-walk and no destination marker. Walking to a station is the player's job — W/A/S/D
— and a click made in reach opens the panel at once. `CraftingStation.StandOffPosition` (`:52`)
still exists for callers that need a sensible place to stand (the anvil uses 2 m, the forge 5 m,
both inside the 6 m craft reach), but the pointer does not use it.

A click on the crafting panel is swallowed by the UI: the pointer checks
`EventSystem.current.IsPointerOverGameObject()` before resolving a world click, so clicking a recipe
row never reaches a station. A click on the world while the panel is open closes it.


The panel itself is still opened by the **server's** payload — the client never decides what a
station offers, it only asks. The client-side reach check exists to avoid asking for a refusal,
not to bypass one.

A station is also **solid ground**: the prefab carries a `NavMeshObstacle` that carves a hole in
the zone NavMesh around the station's body, so a server-planned route bends around the workbench
instead of running through it (see
[14](14-server-authority-hardening.md#destination-intent--the-server-owns-the-route-not-just-the-direction)).
The obstacle's size is set in the station's **local** space (`CraftingStationPrefabFactory.BodyLocalSize`),
because `NavMeshObstacle.size` is scaled by the transform. The station's root stays scaled to
`DefaultScale` for placement, but the body and its carve match the visible model: the forge is
8 x 6.4 x 8 m in world space, the anvil 1.6 x 1 x 1.2 m. `CraftingStation.ApplyVisuals` swaps those
sizes when the replicated type changes.

## 3. Crafting rules and offers

`CraftingValidator` (`Assets/Scripts/Crafting/CraftingValidator.cs:6`) is pure:

- `CanCraft(bag, recipe, stationType, craftingLevel)` (`:8`) checks, in order:
  1. the recipe and its output are non-null,
  2. the recipe's station type is not `None` and **matches** the station the player is using,
  3. the player's crafting level meets `RequiredCraftingLevel`,
  4. the bag has all ingredients (`HasIngredients`).
- `HasIngredients(bag, recipe)` (`:27`) — every ingredient is present in the required count.
- `Count(bag, itemName)` (`:40`) — how many non-empty instances match a name.

`CraftingOfferRules` (`Assets/Scripts/Crafting/CraftingOfferRules.cs:21`) is the read-only sibling
the server uses to build the UI payload. It never touches `TryCraft`:

- `Available(recipes, stationType, level, bag)` (`:27`) returns one
  `RecipeOffer` per recipe that **belongs to this station and is at or below the player's level**.
  A recipe whose ingredients are missing is still listed, with `IngredientsMet = false` — the panel
  shows affordances, and the craft path re-validates anyway. A `None` station, a null recipe list, an
  empty list or an empty registry all yield an empty payload; a null bag is treated as empty.
- `Available(stationType, level, bag)` (`:62`) is the convenience overload over
  `RecipeRegistry.All` used by `PlayerCrafting`.

`RecipeOffer` (`Assets/Scripts/Crafting/CraftingOffer.cs:26`) is the wire struct that crosses the
`[TargetRpc]`: public fields only, so FishNet can serialise it.


## 4. The crafting XP curve

`CraftingXpTable` (`Assets/Scripts/Crafting/CraftingXpTable.cs:5`) defines the curve:

- `XpForLevel(level)` (`:7`) = `100 * (level - 1)^2`. So level 1 needs 0 XP, level 2 needs 100,
  level 3 needs 400, level 4 needs 900, and so on.
- `LevelForXp(xp)` (`:13`) = `floor(sqrt(xp / 100)) + 1`.

(There is a duplicate, unused copy of these formulas in `PlayerCrafting.CraftingXpTableImpl` at
`PlayerCrafting.cs:229`. It is dead code; the live curve is `CraftingXpTable`.)

## 5. PlayerCrafting — the server craft path and the offer payload

`PlayerCrafting` (`Assets/Scripts/Crafting/PlayerCrafting.cs:28`) is the `NetworkBehaviour` holding a
player's crafting XP, running the authoritative craft, and implementing `ICraftingSource` so the
crafting panel can bind to it.

### Opening a station (server decides what is offered)

- `RequestOpenStation(station)` (`:87`) — owner only; sends `RpcRequestOpen(station.NetworkObject)`.
- `RpcRequestOpen` (`:100`) — `[ServerRpc]`; re-checks the sender owns the object, then calls
  `TrySendOffers`.
- `TrySendOffers(target, stationObject)` (`:115`, `[Server]`) — the server's gate: the station must
  exist and the player must be within `MaxCraftDistance` (`:30`). Only then does it build the payload
  and answer **that one client** with `RpcOpenStation`.
- `BuildOffers(station)` (`:140`, `[Server]`) — `CraftingOfferRules.Available(station.StationType,
  CraftingLevel, inventory.Items)` copied into a `RecipeOffer[]` for the wire.
- `RpcOpenStation` (`:125`, `[TargetRpc]`) — runs on the owning client: stores `OpenedStation` and
  `AvailableRecipes`, then raises `StationOpened(station, offers)`. The panel repaints from this.
- `AvailableRecipes` (`:57`) and `StationOpened` (`:63`) are the `ICraftingSource` surface;
  `RecipesChanged` (`:60`) is raised when crafting XP changes client-side, so a level-up can refresh
  the panel without a new payload.

Stepping too far from the station and clicking it produces **no payload**: the client cannot read a
station it could not use. A successful open also reports an **Interact** quest objective using
`CraftingStationType.ToString()` as the key.

### One reach rule for both ends

`PlayerCrafting.IsWithinReach(playerPosition, stationPosition)` (`:44`) is the single definition of
"close enough to use a station", and **both** ends use it: the server when it answers an open
request and when it accepts a craft, and the client when it decides whether a station needs a walk
first. If the two ends measured reach differently, the client would ask for a station the server
refuses — the walk would end and no panel would ever appear.

### Crafting

- `MaxCraftDistance = 6f` (`:30`) — how close the player must stand to the station.
- `RequestCraft(station, recipeName)` (`:152`) — owner only; sends `RpcRequestCraft`.
- `RpcRequestCraft` (`:167`) — `[ServerRpc]`; re-checks the sender owns the object, then `TryCraft`;
  on success it re-sends the offers (`TrySendOffers`) so the panel is handed the freshly-affordable
  list rather than a stale one.
- `TryCraft(NetworkObject stationObject, string recipeName)` (`:180`) — the server's full check:

```csharp
if (stationObject == null || !stationObject.TryGetComponent(out CraftingStation station))
    return false;
RecipeData recipe = RecipeRegistry.Resolve(recipeName);
if (recipe == null) return false;
if (!IsWithinReach(transform.position, station.transform.position))
    return false;
if (inventory == null ||
    !CraftingValidator.CanCraft(inventory.Items, recipe, station.StationType, CraftingLevel))
    return false;
if (!inventory.TryCraft(recipe)) return false;      // consume + produce (chapter 06)
AwardCraftingXp(recipe.CraftingXp);                 // :203
return true;
```

So a craft is: resolve the recipe → be close enough → pass `CraftingValidator` (station type, level,
ingredients) → `PlayerInventory.TryCraft` consumes the ingredients and adds the output → award XP.
The client only ever sends a **recipe name**; everything else is decided by the server. Because the
payload is only a display list, a forged or stale recipe name never authorises anything — `TryCraft`
re-runs the full validation.

### Timed craft orders

A recipe with `CraftDurationSeconds` above zero does not craft inside the click. `TryCraft` (`:223`)
resolves and validates exactly as above, then calls `TryEnqueue` (`:263`) instead of spending:

- the ingredients leave the bag through `PlayerInventory.TryTakeIngredients` (`PlayerInventory.cs:730`)
  and become the order's **escrow** — instance ids, stack counts and durability intact — which is
  what lets a cancel later return the exact same instances;
- the order carries an **absolute UTC deadline** (`CraftQueueRules.DueAtUtcTicks`), the recipe name
  and the escrow, and lives on the player's save record (`PlayerSaveRecord.CraftOrders`, next to
  `CraftingXp`), so a zone transfer, a disconnect and a restart all read the same order.

Five orders share the queue (`CraftQueueRules.MaxOrders`); every state counts against the cap until
the order is pruned, and a sixth request is refused with `CraftQueueRules.QueueFullMessage`.

| State | Meaning |
|---|---|
| `InProgress` | Escrow taken, waiting for the UTC deadline. |
| `Completed` | Due, but the output has not been granted yet (or did not fit the bag). |
| `Claimed` | Resolved and pruned; a reload that finds it does nothing. |
| `Cancelled` | Cancelled before its due tick; its escrow is claimable (see *Cancelling an order*). |

`PlayerCrafting.ProcessOrdersAsync` (`:393`) is the server tick. It leaves `InProgress` only through
`CraftQueueRules.TryComplete`, which refuses a second caller in the same tick, then **persists
Completed before granting**. `TryGrantCompleted` (`:436`) grants the output and awards the recipe's
crafting XP in the same step, marks the order `Claimed`, persists again, and finally removes it. A
reload that finds `Completed` finishes an interrupted grant; one that finds `Claimed` does nothing.
An order that came due while the player was offline completes on the first server tick after the
save is applied at join. A completion that does not fit leaves the order `Completed`, still holding
its slot; `RequestClaimOrder` (`:299`) retries it through `ClaimOrderAsync` (`:322`) once the bag has
room.

The panel never derives a deadline: it renders `CraftOrderView.RemainingSeconds` from the server,
which pushes a fresh order payload once a second (`PlayerCrafting.Update`, `:577`) while the player
has orders.

### Cancelling an order

`RequestCancelOrder` (`PlayerCrafting.cs:346`) asks the server to give up an order that has **not** reached its due tick.
`CraftQueueRules.TryCancel` is the only way into `Cancelled`, and it refuses an order that is not
`InProgress` or whose deadline has passed; once an order is due the only exit is a claim.

Cancel and completion share that "only `InProgress` can move" rule, and both transitions run before
any await on the single server thread, so a cancel landing in the same tick as a completion is
exactly one outcome: whichever ran first changed the state and the other refuses. There is no path
that both refunds escrow and grants output.

`CancelOrderAsync` (`:370`) persists `Cancelled` before returning the escrow, then refunds through
`PlayerInventory.TryGrantEscrow` — the same instance ids, stack counts and durability that were
taken. If the bag cannot take the whole refund the order stays `Cancelled` with its escrow
claimable and keeps its slot; `RequestClaimOrder` returns it once there is room, exactly once,
because `MarkClaimed`/`PruneClaimed` run after the refund and a reload that finds `Claimed` does
nothing.

### Surviving content drift

Recipes and ingredients are data assets, and a balance change can rename or delete one while an order
is in flight. `PlayerCrafting.RestoreFromSave` (`:485`) first prunes `Claimed` orders and then runs
`CraftQueueRules.CancelOnContentDrift`: any surviving order whose recipe, or any escrowed
ingredient, no longer resolves becomes `Cancelled`. Its escrow is untouched, so the normal claim path
returns the materials instead of the load dropping them.

The refund is rebuilt from the escrow fields (`CraftEscrowItem.ToInstance`), not from an
`ItemDefinition`, so even an ingredient whose asset is gone comes back with its name, instance id,
stack count and durability. It comes back exactly once: the claim marks the order `Claimed`, which is
persisted before the slot is pruned and ignored on a reload.

A refused enqueue speaks too. `TryEnqueue` sends `CraftQueueRules.QueueFullMessage` with the order
payload, and the panel renders it in its status line: a completed or cancelled order must be claimed
to free a slot. Every claimable row offers that claim.

### One ingredient-spend path

`PlayerInventory.TryTakeIngredients(recipe, taken)` (`Assets/Scripts/Inventory/PlayerInventory.cs:730`)
is the only place ingredients leave the bag. It plans the ingredient set on a copy first and removes
anything only when the bag covers every entry — a failure is a no-op, never a partial spend — and
hands the exact instances back to the caller. `TrySpendIngredients` (`:701`) is that take plus
releasing the ids, for the instant path; a queued craft keeps the instances as escrow instead.
`TryCraft` (`:674`) checks the output fits in the bag the ingredients would leave behind, spends,
then grants through `TryGrantCraftOutput` (`:757`). Both paths therefore remove the same set for the
same recipe, and both grant through the same helper.

A recipe's `CraftDurationSeconds` (`RecipeData.cs:39`) is what a queued craft will wait on, and it
defaults to `0`. **`0` means instant**, so every recipe authored before the queue existed still
crafts in the tick it was requested; `IsInstant` (`:42`) is the read the instant path and the tests
use. Populate it by hand for a recipe that should take time.


## 6. Gathering data: ResourceDefinition and ResourceNode

A resource is two pieces: the **data** (`ResourceDefinition`) and the **object** (`ResourceNode`).

### 6.1 ResourceDefinition — what a resource is

`ResourceDefinition` (`Assets/Scripts/Gameplay/ResourceDefinition.cs:26`) is a `ScriptableObject`
created with `Assets > Create > MMO > Resource Definition`. It is the gathering half of what
`RecipeData` is to crafting: a designer authors it, the node wears it, and the balance never lives
in code.

| Field | Property | Meaning |
|---|---|---|
| `_resourceType` | `Type` | `ResourceType` — `None`, `Ore`, `Lumber`, `Herb`, `Fish`. |
| `_drop` | `Drop` | The `ItemDefinition` a harvest awards. |
| `_dropAmount` | `DropAmount` | Units the node holds **in total** (floored at 1). |
| `_gatheringTier` | `GatheringTier` | Tool tier the gatherer must bring (0 = bare hands). |
| `_requiredTool` | `RequiredTool` | `ToolKind` the resource names (`None` = no tool named). |
| `_resetTime` | `ResetTime` | Seconds an emptied node stays empty. |
| `_gatherIntervalSeconds` | `GatherIntervalSeconds` | Seconds between two harvests. |
| `_interactRange` | `InteractRange` | Metres a gatherer may stand from the node. |

The shipped definition is `Assets/Resources/ResourceDefinitions/Iron_Ore.asset` — an ore vein that
drops the shipped `Iron Ore` item, five units, bare-handed (no tool, tier 0), a 30 second reset, a
1.2 second interval and a 3 metre reach. `ResourceDefinition.LoadDefault()`
(`Assets/Scripts/Gameplay/ResourceDefinition.cs:101`) finds it by name, which is how the prefab
factory hands a runtime-built node its configuration.

The large variant is `Assets/Resources/ResourceDefinitions/Iron_Ore_Large.asset`: the same ore,
reset, interval and reach, but **15 units** and the `ResourceNode_Large` prefab with its own full and
empty models. The Wilderness authors one small and one large vein; any `ResourceNodeMarker` can point
its `NodePrefab` at the large prefab to place another.

Copper and tin follow the same pattern. `Copper_Ore`, `Copper_Ore_Large`, `Tin_Ore` and
`Tin_Ore_Large` drop the `Copper Ore` / `Tin Ore` items and use the same 5/15 split. Cobalt and
veridium are small-only (`Cobalt_Ore`, `Veridium_Ore`) and each carries its own depleted model and
material. TestZone authors all eight node markers: iron, copper and tin small and large, plus cobalt
and veridium. Copper and tin share `StoneOreEmpty` when depleted; the large variants use
`StoneOreLargeEmpty`; cobalt and veridium use their own empty assets.

The definition carries **no networking**. Both ends read the same asset through the same prefab —
exactly like `WeaponData` on `PlayerCombat` — so there is nothing to replicate and nothing a client
can lie about.

### 6.2 ResourceNode — the object in the world

`ResourceNode` (`Assets/Scripts/Gameplay/ResourceNode.cs:50`) is a `NetworkBehaviour`. It is placed
in a scene as a `ResourceNodeMarker` (built as a prefab with `Tools > MMO > Create Resource Node
Prefab`); on server start the zone's `WorldBootstrap` spawns the networked node at each marker (see
[04](04-networking-and-the-join-flow.md) and [09](09-editor-tooling-and-scenes.md)).

The node's only serialized field is `_definition` (`:60`); every configuration question is answered
by reading it (`DropAmount`, `GatheringTier`, `RequiredTool`, `ResetTime`, `GatherIntervalSeconds`,
`InteractRange`). A node with no definition is **inert, not broken**: it answers with the documented
defaults and yields nothing.

Replicated state is just `Remaining` (`SyncVar<int>`) and `Depleted` (`SyncVar<bool>`). Depletion is
a **state change**, not a despawn: when depleted, `ApplyDepletedState` (`:275`) disables the
collider and renderer, and a server timer respawns it when `ResetTime` is up (`Respawn`, `:297`).

The prefab also carries a carving `NavMeshObstacle` sized to its body, exactly like the crafting
station, so a server-planned route bends *around* a vein instead of through it. A click outside the
node's `InteractRange` is refused by the server and earns the same "Out of range." advice as a
station: the intent is still sent, and the player walks closer and clicks again.

`TryGather(PlayerInventory inventory)` (`:190`) is the server's harvest: if the node is available,
`inventory.TryAddItem(definition)`, decrement `Remaining`, and deplete at zero.
**Award leads, durability follows** — if the bag is full, the node is not consumed.

## 7. Gathering rules

`GatheringRules` (`Assets/Scripts/Gameplay/GatheringRules.cs:28`) is pure:

| Method | Purpose | Line |
|---|---|---|
| `IsWithinReach(gatherer, node, reach)` | Distance check (strict `<`, positive reach). | `:36` |
| `IsTickDue(nextTickAt, now)` | `now >= nextTickAt`. | `:42` |
| `ComputeNextTick(now, interval)` | `now + max(0, interval)`. | `:52` |
| `ComputeRespawnAt(now, respawn)` | `now + max(0, respawn)`. | `:58` |
| `IsRespawnDue(respawnAt, now)` | `now >= respawnAt`. | `:64` |
| `ComputeRemaining(remaining)` | `max(0, remaining - 1)`. | `:73` |

### 7.1 The tool gate: ToolRules

The tool check is not part of `GatheringRules`: it is a question about the items a player holds, so
it lives with the item data in `ToolRules` (`Assets/Scripts/Inventory/ToolRules.cs:24`).

A tool's identity lives on its `ItemDefinition`: `ToolKind` (which family of tool it is) and
`ToolTier` (how good it is). `ToolKind` values are `None`, `Pickaxe`, `Axe`, `Shovel`, `Sickle`,
`Knife`.

| Method | Purpose | Line |
|---|---|---|
| `KindOf(itemName)` | The kind of tool an item is (`None` when it is not a tool). | `:36` |
| `TierOf(itemName)` | The tier an item reaches (0 when it is not a tool). | `:46` |
| `BestTier(bag, equipment, kind)` | The best tier of that kind, in bag **or** worn (`NoTool` = −1). | `:57` |
| `BestToolTier(bag, equipment)` | The best tier of any tool at all. | `:72` |
| `Satisfies(bag, equipment, requiredTool, requiredTier)` | The gate itself. | `:96` |

`Satisfies` has two conditions and both must hold:

- **Kind.** If the resource names a tool, the player must hold one of that kind (bag or worn).
- **Tier.** If the resource asks for a tier, the tool that counts must reach at least that tier —
  which is what refuses a T1 pickaxe on a T2 vein.

A resource that names no tool and asks for no tier is harvestable by hand. A resource that names no
tool but *does* ask for a tier still needs a tool (bare hands reach no tier), of any kind. There is
no way to write "T2 or above" and have bare hands slip through.

An item nobody can resolve is simply not a tool: it contributes no kind and no tier, and is never an
exception. A missing definition is data the server has to refuse, not a crash.

## 8. PlayerGathering — the harvest loop

`PlayerGathering` (`Assets/Scripts/Gameplay/PlayerGathering.cs:35`) is the player's gathering
pipeline. The tick timer lives on the player and is server-only.

- `RequestGather(NetworkObject target)` (`:60`) — owner only; sends `RpcRequestGather`.
- `RpcRequestGather` (`:73`) — `[ServerRpc]`; re-checks ownership, then `StartGathering`.
- `StartGathering(NetworkObject target)` (`:93`) — checks the target is a spawned, available
  `ResourceNode`, the player has an inventory, is within `InteractRange`, and satisfies the tool
  gate. Then it sets `_nextTickAt = now + GatherIntervalSeconds` — so the **first** harvest lands
  one interval later, not instantly.
- `StopGathering()` (`:124`) — clears the target; idempotent.
- `Update` (`:133`) — each server frame while gathering: verify the node is still there/available and
  the player is still in range with the tool; when the tick is due, call `node.TryGather`, stop if the
  node is now depleted, otherwise schedule the next tick.

The tool gate itself is `HasRequiredTool(node, inventory)` (`:188`), which asks `ToolRules` — the
same question is asked at the start of a session and again on every tick, because a player can drop
or swap a tool between two harvests.

Note the asymmetry with crafting: in `PlayerGathering`, `Update` re-checks reach/tool every tick, so
walking away or dropping the tool stops the harvest.

## 9. A gathering tick, end to end

```
CLIENT clicks a node with PlayerWorldPointer
  the raycast finds ResourceNode
  out of reach → chat advisory ("Out of range."), and the intent is still sent
  → ResourceNode.RequestGatherFromLocalPlayer
    → PlayerGathering.FindLocal → RequestGather ── ServerRpc ──►
SERVER
  PlayerGathering.RpcRequestGather → StartGathering
    checks: spawned node, available, in range, tool kind + tier satisfied
    schedule first tick
  ... every frame while gathering (Update) ...
    tick due → node.TryGather(inventory)
       inventory.TryAddItem → decrement Remaining → maybe Deplete
    stop if depleted or out of range / tool gone
    SyncVar Remaining/Depleted replicate to all clients
```

## Common tasks

- **Add a recipe.** Create the asset (`Assets > Create > MMO > Recipe Data`), set the ingredients,
  output, station type, required level and XP, and save it under `Resources`. It self-registers. Add
  EditMode tests for `CraftingValidator` behaviour.
- **Change which recipes a station shows.** Edit `CraftingOfferRules.Available`; it is the only thing
  that decides the panel's list, so the client cannot disagree with the server about it.
- **Add a new station type.** Add the value to `CraftingStationType` and handle it wherever the
  station is configured (the prefab factory/baker). Recipes reference the enum, so no other code
  changes.
- **Change the crafting XP curve.** Edit `CraftingXpTable.XpForLevel`/`LevelForXp`. Ignore the dead
  `CraftingXpTableImpl` copy.
- **Add a new kind of resource.** Create the asset (`Assets > Create > MMO > Resource Definition`),
  set the kind, drop item, amount, tool and tier, and save it under `Resources/ResourceDefinitions`.
  Point a node at it — no code changes.
- **Add a resource node to a scene.** Drop a `ResourceNodeMarker` where the node should appear (or
  bake the zone with `Tools > MMO > Build Zones`) and point it at the resource-node prefab. The
  prefab carries the definition; the marker only says where the node goes.
- **Make a resource need a tool.** Set the definition's `RequiredTool` (the kind) and
  `GatheringTier` (the floor). The tool item itself carries `ToolKind` and `ToolTier` on its
  `ItemDefinition` — a T1 pickaxe and a T2 pickaxe are two items of one kind at two tiers.
- **Change how long gathering takes per item.** Edit `GatherIntervalSeconds` on the definition.
- **Tune node respawn.** Edit `ResetTime` on the definition.
- **Make gathering stop when the player moves.** Already handled: `PlayerGathering.Update` re-checks
  `IsWithinReach` every tick.
