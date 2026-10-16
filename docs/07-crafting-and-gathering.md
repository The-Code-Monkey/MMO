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

`RecipeIngredient` (`RecipeData.cs:8`) is a small serialisable struct: `Item` (an `ItemDefinition`)
and `Count`.

### Self-registration

Like items, recipes self-register. `RecipeData.OnEnable` (`:34`) registers in `RecipeRegistry` when
named, and `OnDisable` (`:40`) unregisters. `RecipeRegistry`
(`Assets/Scripts/Crafting/RecipeRegistry.cs:6`) mirrors `ItemDefinitionRegistry`: `Register` (`:12`),
`Unregister` (`:31`), `Resolve` (`:40`, with a `Resources.Load<RecipeData>` fallback) and `Clear`
(`:55`). `All` (`:63`) returns a snapshot `IReadOnlyList<RecipeData>` of every registered recipe
(nulls skipped) — the server uses it to build the crafting payload below without ever handing assets
to a client.

## 2. Crafting stations

`CraftingStationType` (`Assets/Scripts/Crafting/CraftingStationType.cs:5`) is an enum:

| Value | Meaning | Line |
|---|---|---|
| `None` (0) | Not craftable anywhere. | `:7` |
| `Furnace` (1) | Refining: ore → bar. | `:8` |
| `Anvil` (2) | Crafting: bar → gear. | `:9` |
| `Alchemy` (3) | Brewing: reagents → potions. | `:10` |

`CraftingStation` (`Assets/Scripts/Crafting/CraftingStation.cs:7`) is a `NetworkBehaviour` with a
replicated station type. The server sets it with `Configure(CraftingStationType)` (`:14`). Clicking
the station calls `RequestOpenFromLocalPlayer` (`:18`), which finds the local `PlayerCrafting` and
invokes `RequestOpenStation` — that now starts a **server round-trip**: the client asks, the server
decides what the station offers and sends it back (section 5). The client never reads recipes from
its own registry to render a panel.

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
`PlayerCrafting.cs:209`. It is dead code; the live curve is `CraftingXpTable`.)

## 5. PlayerCrafting — the server craft path and the offer payload

`PlayerCrafting` (`Assets/Scripts/Crafting/PlayerCrafting.cs:27`) is the `NetworkBehaviour` holding a
player's crafting XP, running the authoritative craft, and implementing `ICraftingSource` so the
crafting panel can bind to it.

### Opening a station (server decides what is offered)

- `RequestOpenStation(station)` (`:68`) — owner only; sends `RpcRequestOpen(station.NetworkObject)`.
- `RpcRequestOpen` (`:81`) — `[ServerRpc]`; re-checks the sender owns the object, then calls
  `TrySendOffers`.
- `TrySendOffers(target, stationObject)` (`:96`, `[Server]`) — the server's gate: the station must
  exist and the player must be within `MaxCraftDistance` (`:29`). Only then does it build the payload
  and answer **that one client** with `RpcOpenStation`.
- `BuildOffers(station)` (`:126`, `[Server]`) — `CraftingOfferRules.Available(station.StationType,
  CraftingLevel, inventory.Items)` copied into a `RecipeOffer[]` for the wire.
- `RpcOpenStation` (`:111`, `[TargetRpc]`) — runs on the owning client: stores `OpenedStation` and
  `AvailableRecipes`, then raises `StationOpened(station, offers)`. The panel repaints from this.
- `AvailableRecipes` (`:43`) and `StationOpened` (`:49`) are the `ICraftingSource` surface;
  `RecipesChanged` (`:46`) is raised when crafting XP changes client-side, so a level-up can refresh
  the panel without a new payload.

Stepping too far from the station and clicking it produces **no payload**: the client cannot read a
station it could not use.

### Crafting

- `MaxCraftDistance = 3f` (`:29`) — how close the player must stand to the station.
- `RequestCraft(station, recipeName)` (`:138`) — owner only; sends `RpcRequestCraft`.
- `RpcRequestCraft` (`:147`) — `[ServerRpc]`; re-checks the sender owns the object, then `TryCraft`;
  on success it re-sends the offers (`TrySendOffers`) so the panel is handed the freshly-affordable
  list rather than a stale one.
- `TryCraft(NetworkObject stationObject, string recipeName)` (`:160`) — the server's full check:

```csharp
if (stationObject == null || !stationObject.TryGetComponent(out CraftingStation station))
    return false;
RecipeData recipe = RecipeRegistry.Resolve(recipeName);
if (recipe == null) return false;
if (Vector3.Distance(transform.position, station.transform.position) > MaxCraftDistance)
    return false;
if (inventory == null ||
    !CraftingValidator.CanCraft(inventory.Items, recipe, station.StationType, CraftingLevel))
    return false;
if (!inventory.TryCraft(recipe)) return false;      // consume + produce (chapter 06)
AwardCraftingXp(recipe.CraftingXp);                 // :189
return true;
```

So a craft is: resolve the recipe → be close enough → pass `CraftingValidator` (station type, level,
ingredients) → `PlayerInventory.TryCraft` consumes the ingredients and adds the output → award XP.
The client only ever sends a **recipe name**; everything else is decided by the server. Because the
payload is only a display list, a forged or stale recipe name never authorises anything — `TryCraft`
re-runs the full validation.


## 6. Gathering data: ResourceNode

`ResourceNode` (`Assets/Scripts/Gameplay/ResourceNode.cs:41`) is a `NetworkBehaviour`. It is placed
in a scene as a `ResourceNodeMarker` (built as a prefab with `Tools > MMO > Create Resource Node
Prefab`); on server start the zone's `WorldBootstrap` spawns the networked node at each marker (see
[04](04-networking-and-the-join-flow.md) and [09](09-editor-tooling-and-scenes.md)). Its defaults:

| Constant | Value | Line |
|---|---|---|
| `DefaultItemId` | `"Iron Ore"` | `:44` |
| `DefaultMaxYield` | 5 | `:47` |
| `DefaultRespawnSeconds` | 30 | `:50` |
| `DefaultGatherIntervalSeconds` | 1.2 | `:53` |
| `DefaultInteractRange` | 3 | `:56` |

Configurable fields: item id, required tool, max yield, respawn seconds, gather interval and
interact range.

Replicated state is just `Remaining` (`SyncVar<int>`) and `Depleted` (`SyncVar<bool>`). Depletion is
a **state change**, not a despawn: when depleted, `ApplyDepletedState` (`:235`) disables the
collider and renderer, and a server timer respawns it 30 seconds later (`Respawn`, `:257`).

`TryGather(PlayerInventory inventory)` (`:166`) is the server's harvest: if the node is available,
resolve the item, `inventory.TryAddItem(definition)`, decrement `Remaining`, and deplete at zero.
**Award leads, durability follows** — if the bag is full, the node is not consumed.

## 7. Gathering rules

`GatheringRules` (`Assets/Scripts/Gameplay/GatheringRules.cs:24`) is pure:

| Method | Purpose | Line |
|---|---|---|
| `IsWithinReach(gatherer, node, reach)` | Distance check (strict `<`, positive reach). | `:32` |
| `HasRequiredTool(bag, equipment, requiredTool)` | Does the player carry the tool (in bag **or** worn)? Blank tool = always true. | `:49` |
| `IsTickDue(nextTickAt, now)` | `now >= nextTickAt`. | `:62` |
| `ComputeNextTick(now, interval)` | `now + max(0, interval)`. | `:72` |
| `ComputeRespawnAt(now, respawn)` | `now + max(0, respawn)`. | `:78` |
| `IsRespawnDue(respawnAt, now)` | `now >= respawnAt`. | `:84` |
| `ComputeRemaining(remaining)` | `max(0, remaining - 1)`. | `:93` |

Tool checks look only at `ItemInstance.ItemName`, so no asset has to load.

## 8. PlayerGathering — the harvest loop

`PlayerGathering` (`Assets/Scripts/Gameplay/PlayerGathering.cs:35`) is the player's gathering
pipeline. The tick timer lives on the player and is server-only.

- `RequestGather(NetworkObject target)` (`:60`) — owner only; sends `RpcRequestGather`.
- `RpcRequestGather` (`:72`) — `[ServerRpc]`; re-checks ownership, then `StartGathering`.
- `StartGathering(NetworkObject target)` (`:91`) — checks the target is a spawned, available
  `ResourceNode`, the player has an inventory, is within `InteractRange`, and has the required tool.
  Then it sets `_nextTickAt = now + GatherIntervalSeconds` — so the **first** harvest lands one
  interval later, not instantly.
- `StopGathering()` (`:122`) — clears the target; idempotent.
- `Update` (`:132`) — each server frame while gathering: verify the node is still there/available and
  the player is still in range with the tool; when the tick is due, call `node.TryGather`, stop if the
  node is now depleted, otherwise schedule the next tick.

Note the asymmetry with crafting: in `PlayerGathering`, `Update` re-checks reach/tool every tick, so
walking away or dropping the tool stops the harvest.

## 9. A gathering tick, end to end

```
CLIENT clicks a node
  ResourceNode.OnMouseDown → RequestGatherFromLocalPlayer
    → PlayerGathering.FindLocal → RequestGather ── ServerRpc ──►
SERVER
  PlayerGathering.RpcRequestGather → StartGathering
    checks: spawned node, available, in range, has tool
    schedule first tick
  ... every frame while gathering (Update) ...
    tick due → node.TryGather(inventory)
       resolve item → inventory.TryAddItem → decrement Remaining → maybe Deplete
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
- **Add a resource node to a scene.** Drop a `ResourceNodeMarker` where the node should appear (or
  bake the zone with `Tools > MMO > Build Zones`), point it at the resource-node prefab and set its
  item id, tool requirement, yield, respawn and interval.
- **Change how long gathering takes per item.** Edit `GatherIntervalSeconds` on the node.
- **Tune node respawn.** Edit `RespawnSeconds` on the node.
- **Make gathering stop when the player moves.** Already handled: `PlayerGathering.Update` re-checks
  `IsWithinReach` every tick.
