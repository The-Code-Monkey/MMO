# 06 — Inventory and equipment

This chapter explains the grid bag, the equipment slots, how items are defined as data, and how the
server keeps the whole thing honest. Crafting is the subject of [07](07-crafting-and-gathering.md);
this chapter covers the inventory half of crafting (`PlayerInventory.TryCraft`).

## 1. The two containers

A player has two inventory containers, both replicated with FishNet `SyncList`s
(`Assets/Scripts/Inventory/PlayerInventory.cs`):

- a **bag** — `SyncList<ItemInstance> _items` (`:58`), an 8×6 grid (`GridWidth = 8` at `:44`,
  `GridHeight = 6` at `:47`),
- **equipment** — `SyncList<ItemInstance> _equipment` (`:59`), one entry per equipment slot.

Both are exposed read-only as `Items` (`:73`) and `Equipment` (`:76`), and a single
`ContainersChanged` event (`:83`) fires whenever either changes, so the UI can repaint.

Only the server mutates these lists. A client asks through `RequestEquip` / `RequestUnequip`.

## 2. ItemInstance — what travels over the wire

`ItemInstance` (`Assets/Scripts/Inventory/ItemInstance.cs:27`) is a `struct` with **public fields**:

| Field | Meaning | Line |
|---|---|---|
| `ItemName` | The registry key. The definition itself never travels. | `:30` |
| `InstanceId` | The server-minted unique id of this physical item; the ownership registry tracks it. Empty means an empty cell, or a legacy save entry not yet assigned an id. | `:39` |
| `GridX`, `GridY` | Position in the bag grid. | `:42`, `:45` |
| `Width`, `Height` | Footprint size. | `:48`, `:51` |
| `Durability` | Remaining wear, `0..MaxDurability`. `0` on an indestructible item. | `:61` |

Why public fields? FishNet auto-generates serialisers for network payloads from **public** fields and
ignores private `[SerializeField]` fields, so a wire type must expose them. This is the opposite of
the data assets, which keep their fields private.

Helpers: `IsEmpty` (`:64`) is true when `ItemName` is blank; `Empty` (`:67`) is the default value;
`Create(ItemDefinition definition, int gridX, int gridY)` (`:74`) builds an instance from a
definition, copying its footprint, seeding `Durability` from the definition's `MaxDurability`, and
minting a fresh `InstanceId`. `NewInstanceId()` (`:119`) mints a new id
(`Guid.NewGuid().ToString("N")`); `WithIdentity()` (`:129`) returns the instance with a guaranteed id,
assigning one only to a legacy instance that has none (see
[14](14-server-authority-hardening.md#3-dupe-protection)).

## 3. ItemDefinition — what an item is

`ItemDefinition` (`Assets/Scripts/Inventory/ItemDefinition.cs:24`) is a `ScriptableObject` created
with `Assets > Create > MMO > Item Definition`. Fields:

| Field | Meaning | Line |
|---|---|---|
| `ItemName` | Registry key and display name. | `:26` |
| `EquipSlot` | Which equipment slot it goes in. | `:27` |
| `RequiredLevel` | Minimum player level to equip. | `:28` |
| `GridWidth`, `GridHeight` | Footprint in the bag grid (default 1×1). | `:29`, `:30` |
| `MaxHealthBonus` | Health cap bonus when equipped. | `:31` |
| `MaxManaBonus` | Mana cap bonus when equipped. | `:32` |
| `MaxDurability` | Wear ceiling; `0` (the default) means indestructible. | `:33` |

The public `MaxDurability` accessor (`:73`) is a read-through (`Mathf.Max(0, _maxDurability)`) rather
than the raw field, so a negative authoring value cannot leak out. It is what `ItemInstance.Create`
seeds a fresh item with and what `PlayerSaveMapper` clamps a loaded durability against.

Existing items live in `Assets/Resources/Items/` (e.g. `Iron_Ore.asset`, `Iron_Helm.asset`,
`Broadsword.asset`).

### Self-registration

`ItemDefinition.OnEnable` (`ItemDefinition.cs:85`) registers the definition in
`ItemDefinitionRegistry` if it has a name, and `OnDisable` (`:91`) unregisters it. Assets loaded
from disk register on load; assets created in code register once named. This means you normally do
not have to wire anything up — drop a named asset in `Resources/Items` and it is findable.

`ItemDefinitionRegistry` (`Assets/Scripts/Inventory/ItemDefinitionRegistry.cs:23`) is a static
name → definition map:

- `Register` (`:37`) refuses null and empty-name definitions, and warns if a *different* instance
  takes an existing name.
- `Resolve` (`:81`) looks in the dictionary and, on a miss, falls back to
  `Resources.Load<ItemDefinition>(itemName)` and caches the result.
- `Clear` (`:102`) empties the map (tests use this).

## 4. The grid geometry

`InventoryGrid` (`Assets/Scripts/Inventory/InventoryGrid.cs:18`) is a pure class that owns the grid
*geometry* but not the items. Every method takes the current item list as an argument, so the grid
can be tested without a live server.

| Method | Purpose | Line |
|---|---|---|
| `Contains(x, y, w, h)` | Does the rectangle fit inside the grid at all? | `:51` |
| `CanPlace(items, x, y, w, h)` | Does it fit **and** not overlap an existing item? | `:65` |
| `TryFindFreeSpace(items, w, h, out x, out y)` | First free spot, scanning left-to-right, top-to-bottom. | `:101` |
| `FindAt(items, x, y)` | The index of the item covering a cell, or -1. | `:132` |

`TryFindFreeSpace` deliberately scans in a fixed order, so packing is deterministic — the same items
always land in the same places.

There is **no stacking**. Two "Iron Ore" items are two separate `ItemInstance` entries, each taking
its own footprint. There is no stack-count field anywhere.

## 5. Validating an equip

`InventoryValidator` (`Assets/Scripts/Inventory/InventoryValidator.cs:13`) has exactly one method,
`CanEquip` (`:21`) — the project's canonical example of a pure rule:

```csharp
public static bool CanEquip(int playerLevel, ItemDefinition definition)
{
    if (definition == null)
        return false;                    // an item the server cannot resolve must be refused
    return playerLevel >= definition.RequiredLevel;
}
```

There is no `CanAdd` here; "does this item fit" is answered by `InventoryGrid`, and "can it be
equipped" by `CanEquip`. The three server gates for an equip are: resolve the definition, run
`CanEquip`, and check the displaced item fits back in the bag.

## 6. Equipment slots

`EquipmentSlot` (`Assets/Scripts/Inventory/EquipmentSlot.cs:9`) is an enum whose numeric values are a
**wire contract** because `PlayerInventory` indexes the equipment list by `(int)slot`:

| Value | Slot | Line |
|---|---|---|
| 0 | `MainHand` | `:11` |
| 1 | `OffHand` | `:12` |
| 2 | `Head` | `:13` |
| 3 | `Chest` | `:14` |
| 4 | `Feet` | `:15` |

`EquipmentSlots` (`Assets/Scripts/Inventory/EquipmentSlots.cs:14`) provides `Count = 5` (`:17`),
`All` (`:32`) and `IsValid(slot)` (`:38`), which guards against casting garbage like
`(EquipmentSlot)99`.

`PlayerInventory.EnsureEquipmentSlots` (`PlayerInventory.cs:634`) pads the equipment list with empty
entries until it is `EquipmentSlots.Count` long.

## 7. PlayerInventory — the server's rules

`PlayerInventory` (`Assets/Scripts/Inventory/PlayerInventory.cs:41`) is the `NetworkBehaviour` that
owns both containers. The important methods:

### Ask to equip / unequip

- `RequestEquip(int gridX, int gridY)` (`:136`) — owner only; sends `RpcRequestEquip`.
- `RpcRequestEquip` (`:145`) — `[ServerRpc]`; re-checks the sender owns the object, then calls
  `TryEquip`.
- `RequestUnequip(EquipmentSlot slot)` (`:156`) and `RpcRequestUnequip` (`:165`) — the same shape for
  unequipping.

Note the view sends **coordinates, not an item** (see `InventorySlotView`), so a stale click is
re-resolved server-side rather than acting on a copied item.

### TryEquip — the core

`TryEquip(int gridX, int gridY)` (`:227`) runs all the gates before changing anything:

1. Find the item index at the cell (`InventoryGrid.FindAt`); refuse if there is none.
2. Resolve the definition via `ItemDefinitionRegistry.Resolve`; refuse if unknown.
3. `InventoryValidator.CanEquip(playerLevel, definition)`; refuse if the level is too low.
4. `definition.EquipSlot` must be a valid slot, and the item type must not be a bag-only item.
5. Build a scratch copy of the bag **without** the moving item, then check the currently worn item in
   that slot fits back into the scratch bag. A full bag refuses the swap.
6. Only now mutate: remove from the bag, set the equipment slot, place any displaced item back in the
   bag.
7. `RecalculateEquipmentStats()`.

### TryUnequip

`TryUnequip(EquipmentSlot slot)` (`:297`) validates the slot, requires something worn there, finds
free bag space, then clears the slot and adds the item to the bag. A full bag refuses entirely.

### TryAddItem

`TryAddItem(ItemDefinition definition)` (`:207`) is how the server grants an item (a gather, a craft
output, a drop). It finds free space and adds the instance. Acquisition deliberately does **not**
check level — level is checked at equip time.

### Stat bonuses

`RecalculateEquipmentStats` (`:473`) rebuilds the list of equipped definitions and calls
`stats.ApplyEquipmentBonuses(...)` (`:494`). It **recalculates from scratch** rather than
incrementing, so an item removed correctly subtracts its bonus. The sums come from two pure helpers:
`ComputeMaxHealthBonus` (`:504`) and `ComputeMaxManaBonus` (`:520`).

### Restore (from a save)

`Restore(IReadOnlyList<ItemInstance> bag, IReadOnlyList<ItemInstance> equipment)` (`:542`) rebuilds
the containers from a loaded save. The bag is copied as-is; the equipment list is padded to the
right length and extra entries are ignored. It then recalculates stats. This is called by
`PlayerSaveTrigger` during the join (see [03](03-backend-and-persistence.md)).

### CountItem

`CountItem(string itemName)` (`:629`) counts how many of an item are in the bag, delegating to
`CraftingValidator.Count` (`Assets/Scripts/Crafting/CraftingValidator.cs:40`).

## 8. The clickable views

The two small components that make the grid clickable:

- `InventorySlotView` (`Assets/Scripts/Inventory/InventorySlotView.cs:27`) holds a grid coordinate.
  Right-click calls `_inventory.RequestEquip(_gridX, _gridY)` (`:65`).
- `EquipmentSlotView` (`Assets/Scripts/Inventory/EquipmentSlotView.cs:16`) holds a slot. Right-click
  calls `_inventory.RequestUnequip(_slot)` (`:42`).

Both ignore every mouse button except the right one, so left-click stays free for the action bar.

`IInventorySource` (`Assets/Scripts/Inventory/IInventorySource.cs:17`) is the read/request interface
the UI binds to: `GridWidth`, `GridHeight`, `GetItemAt`, `RequestEquip`, and the
`ContainersChanged` event.

## 9. The inventory half of crafting

`PlayerInventory.TryCraft(RecipeData recipe)` (`:569`) is the container-level craft. It:

1. Works on a scratch copy of the bag,
2. verifies every ingredient is present (consuming copies from the scratch copy),
3. verifies the output fits,
4. then removes the real ingredients and adds the outputs.

This is separate from `PlayerCrafting.TryCraft` (the station/proximity/XP half), which calls this one
once all its own checks pass. See [07](07-crafting-and-gathering.md). Note the output placement is
currently simple: multiple outputs are added at the same coordinates (`:613`).

## 10. Death, loot and durability

Item durability is the anti-inflation half of death, and it is persisted. `ItemSaveData.Durability`
(`PlayerSaveRecord.cs:32`) round-trips the count; `PlayerSaveMapper.Apply` restores it clamped to the
definition's current `MaxDurability` (`PlayerSaveMapper.cs:112`) and treats a zero from a
pre-durability save as "fully repaired" rather than broken. A balance rollback that lowers a cap can
therefore never leave a saved item above its own ceiling.

`PlayerInventory` grows a small server-only surface for the death resolver:

| Method | What it does | Line |
|---|---|---|
| `ApplyDurabilityDamage(int amount)` | Wears every **equipped** destructible item, returning how many changed. | `:384` |
| `LootableItems()` | The non-empty bag **and** worn items as one flat list — the drop snapshot. | `:414` |
| `ClearAll()` | Empties both containers and re-derives the stat caps. | `:426` |
| `CollectLootable(bag, equipment)` | The pure half of `LootableItems`, testable without a server. | `:439` |
| `TryAddExistingItem(ItemInstance item)` | Adds an existing instance (not a freshly minted one) into free space, **preserving its durability**. | `:363` |
| `RequestLoot(LootBag bag)` / `RpcRequestLoot` | Owner intent → `[ServerRpc]` → owner + range check → `bag.Loot(this)`. | `:178`, `:187` |

`LootBag` (`Assets/Scripts/Gameplay/LootBag.cs:22`) is the pile of dropped items. It is a
**server-write** `SyncList<ItemInstance>` (`Fill` is `[Server]`) and is deliberately *unowned*:
FishNet only lets an owner send a `[ServerRpc]` to an object, so a looter routes the intent through
their own `PlayerInventory` instead, which re-checks range before calling `Loot`. A bag that empties
despawns itself.

The full-loot flow, owned by `PlayerDeath` (`Assets/Scripts/Gameplay/PlayerDeath.cs:32`):

1. On `PlayerStats.Died`, read the tier under the corpse (`ZoneRiskConfig.ResolveTierAt`).
2. For black (`DropInventory`), spawn the `LootBag` and **fill it first**, *then* `ClearAll()` and
   push the wipe to the backend immediately. A crash between the two leaves the items in the bag
   (recoverable) rather than destroyed.
3. Wear the gear and charge the repair bill for safe/yellow.
4. Always `Respawn()`.

The prefab's drop target is resolved from the `NetworkManager`'s spawnable prefabs (the same
registration that gives the loot bag its id), so no per-prefab reference needs baking. See
[05, section 1a](05-gameplay-stats-combat-abilities.md#1a-death-and-the-risk-tiers) for the tier
table.

## 11. Single-writer ownership (dupe protection)

Two copies of one item is the oldest MMO exploit. `ItemInstance.InstanceId` (`:39`) gives every item
the server mints a unique identity, and `ItemOwnershipRegistry`
(`Assets/Scripts/Inventory/ItemOwnershipRegistry.cs:43`) is the single writer that maps each id to
exactly one holder, so the same physical item can never end up in two bags.

`OwnershipMove` (`:11`) is the outcome of one operation — `Ok` is the only success:

| Operation | What it does | Line |
|---|---|---|
| `Grant(instanceId, holder)` | First grant wins; re-granting to the same holder is `Ok`, a second holder is `DuplicateDetected`. | `:64` |
| `Transfer(instanceId, from, to)` | Atomically moves an id. `NotHeld` when `from` does not hold it; `DuplicateDetected` when `to` already does. | `:87` |
| `Release(instanceId, holder)` | Forgets a consumed/destroyed/dropped item. Only the current holder may; anyone else gets `NotHeld`. | `:113` |
| `TryGetOwner` / `IsHeldBy` / `Count` / `Clear` | Read helpers (owner lookup, ownership test, tracked count, wipe for tests). | `:132`, `:143`, `:49`, `:154` |

The registry is lock-guarded, so "exactly one holder per id" holds even when calls arrive from
different threads. That makes the in-process race impossible rather than merely unlikely; the
**cross-process** race (two zones, one item) is settled on the backend by the version-guarded writes
described in [14](14-server-authority-hardening.md#3-dupe-protection).

Identity is **server-minted**: `ItemInstance.NewInstanceId()` (`:119`) is called only by
`ItemInstance.Create` and the load path, so a client can never inject an id. For persistence,
`ItemSaveData.InstanceId` (`PlayerSaveRecord.cs:24`) round-trips the id; `PlayerSaveMapper.Apply`
mints a fresh one when an older save carries an empty id (`PlayerSaveMapper.cs:117`) and
`ToSaveData` writes it back (`PlayerSaveMapper.cs:150`).

## Common tasks

- **Add an item.** Create the asset (`Assets > Create > MMO > Item Definition`), name it, set the
  footprint, slot, level requirement and bonuses, and save it under `Assets/Resources/Items/`.
  It self-registers, so no wiring is needed. Then add EditMode tests for the rules.
- **Add a new equipment slot.** Add the value to `EquipmentSlot`, add it to `EquipmentSlots.AllSlots`
  and bump `Count`. Because the numeric values are a wire contract, add new slots at the **end**.
- **Give an item a new stat bonus.** Add a field to `ItemDefinition`, sum it in
  `ComputeMaxHealthBonus`/`ComputeMaxManaBonus` (or a new helper), and apply it in
  `RecalculateEquipmentStats`.
- **Make an item destructible.** Set `MaxDurability` to a positive value on the definition (0, the
  default, means indestructible). Fresh instances then start at that ceiling, a safe-zone death wears
  them, and the count round-trips through `ItemSaveData.Durability`.
- **Make an item stack.** This does not exist yet. You would add a count field to `ItemInstance`,
  teach `TryAddItem` to merge, and teach `PlayerSaveMapper` to round-trip the count.
- **Test an equip rule.** Use `InventoryValidator.CanEquip` directly, or `TestItemDefinitions.Create`
  to build an item in code and drive `TryEquip` on a fresh `PlayerInventory` (see
  [10-testing](10-testing.md)).
- **Protect an item from duplication.** Route the mutation through `ItemOwnershipRegistry`
  (`Grant` / `Transfer` / `Release`); a second holder for one id is refused. See
  [14](14-server-authority-hardening.md#3-dupe-protection).
