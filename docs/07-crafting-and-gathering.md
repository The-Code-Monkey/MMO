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
(`:55`).

## 2. Crafting stations

`CraftingStationType` (`Assets/Scripts/Crafting/CraftingStationType.cs:5`) is an enum:

| Value | Meaning | Line |
|---|---|---|
| `None` (0) | Not craftable anywhere. | `:7` |
| `Furnace` (1) | Refining: ore → bar. | `:8` |
| `Anvil` (2) | Crafting: bar → gear. | `:9` |

`CraftingStation` (`Assets/Scripts/Crafting/CraftingStation.cs:7`) is a `NetworkBehaviour` with a
replicated station type. The server sets it with `Configure(CraftingStationType)` (`:14`). Clicking
the station calls `RequestOpenFromLocalPlayer` (`:18`), which finds the local `PlayerCrafting` and
invokes `RequestOpenStation` — a purely local UI event, no server round-trip.

## 3. Crafting rules

`CraftingValidator` (`Assets/Scripts/Crafting/CraftingValidator.cs:6`) is pure:

- `CanCraft(bag, recipe, stationType, craftingLevel)` (`:8`) checks, in order:
  1. the recipe and its output are non-null,
  2. the recipe's station type is not `None` and **matches** the station the player is using,
  3. the player's crafting level meets `RequiredCraftingLevel`,
  4. the bag has all ingredients (`HasIngredients`).
- `HasIngredients(bag, recipe)` (`:27`) — every ingredient is present in the required count.
- `Count(bag, itemName)` (`:40`) — how many non-empty instances match a name.

## 4. The crafting XP curve

`CraftingXpTable` (`Assets/Scripts/Crafting/CraftingXpTable.cs:5`) defines the curve:

- `XpForLevel(level)` (`:7`) = `100 * (level - 1)^2`. So level 1 needs 0 XP, level 2 needs 100,
  level 3 needs 400, level 4 needs 900, and so on.
- `LevelForXp(xp)` (`:13`) = `floor(sqrt(xp / 100)) + 1`.

(There is a duplicate, unused copy of these formulas in `PlayerCrafting.CraftingXpTableImpl` at
`PlayerCrafting.cs:105`. It is dead code; the live curve is `CraftingXpTable`.)

## 5. PlayerCrafting — the server craft path

`PlayerCrafting` (`Assets/Scripts/Crafting/PlayerCrafting.cs:11`) is the `NetworkBehaviour` holding a
player's crafting XP and running the authoritative craft.

- `MaxCraftDistance = 3f` (`:13`) — how close the player must stand to the station.
- `CraftingXp` (`:19`) is replicated; `CraftingLevel` (`:20`) is derived from it via the XP table.
- `StationOpened` (`:22`) is a local event for the UI.
- `RequestCraft(station, recipeName)` (`:45`) — owner only; sends `RpcRequestCraft`.
- `RpcRequestCraft` (`:54`) — `[ServerRpc]`; re-checks the sender owns the object, then `TryCraft`.
- `TryCraft(NetworkObject stationObject, string recipeName)` (`:64`) — the server's full check:

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
AwardCraftingXp(recipe.CraftingXp);                 // :93
return true;
```

So a craft is: resolve the recipe → be close enough → pass `CraftingValidator` (station type, level,
ingredients) → `PlayerInventory.TryCraft` consumes the ingredients and adds the output → award XP.
The client only ever sends a **recipe name**; everything else is decided by the server.

## 6. Gathering data: ResourceNode

`ResourceNode` (`Assets/Scripts/Gameplay/ResourceNode.cs:41`) is a `NetworkBehaviour` placed in a
scene (built as a prefab with `Tools > MMO > Create Resource Node Prefab`). Its defaults:

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
- **Add a new station type.** Add the value to `CraftingStationType` and handle it wherever the
  station is configured (the prefab factory/baker). Recipes reference the enum, so no other code
  changes.
- **Change the crafting XP curve.** Edit `CraftingXpTable.XpForLevel`/`LevelForXp`. Ignore the dead
  `CraftingXpTableImpl` copy.
- **Add a resource node to a scene.** Bake the prefab
  (`Tools > MMO > Create Resource Node Prefab`), place it in the scene, and set its item id, tool
  requirement, yield, respawn and interval in the Inspector.
- **Change how long gathering takes per item.** Edit `GatherIntervalSeconds` on the node.
- **Tune node respawn.** Edit `RespawnSeconds` on the node.
- **Make gathering stop when the player moves.** Already handled: `PlayerGathering.Update` re-checks
  `IsWithinReach` every tick.
