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
| `GridX`, `GridY` | Position in the bag grid. | `:33`, `:36` |
| `Width`, `Height` | Footprint size. | `:39`, `:42` |

Why public fields? FishNet auto-generates serialisers for network payloads from **public** fields and
ignores private `[SerializeField]` fields, so a wire type must expose them. This is the opposite of
the data assets, which keep their fields private.

Helpers: `IsEmpty` (`:45`) is true when `ItemName` is blank; `Empty` (`:48`) is the default value;
`Create(ItemDefinition definition, int gridX, int gridY)` (`:55`) builds an instance from a
definition, copying its footprint.

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

Existing items live in `Assets/Resources/Items/` (e.g. `Iron Ore.asset`, `Iron Helm.asset`,
`Broadsword.asset`).

### Self-registration

`ItemDefinition.OnEnable` (`ItemDefinition.cs:67`) registers the definition in
`ItemDefinitionRegistry` if it has a name, and `OnDisable` (`:73`) unregisters it. Assets loaded
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

`PlayerInventory.EnsureEquipmentSlots` (`PlayerInventory.cs:460`) pads the equipment list with empty
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

`TryEquip(int gridX, int gridY)` (`:199`) runs all the gates before changing anything:

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

`TryUnequip(EquipmentSlot slot)` (`:269`) validates the slot, requires something worn there, finds
free bag space, then clears the slot and adds the item to the bag. A full bag refuses entirely.

### TryAddItem

`TryAddItem(ItemDefinition definition)` (`:179`) is how the server grants an item (a gather, a craft
output, a drop). It finds free space and adds the instance. Acquisition deliberately does **not**
check level — level is checked at equip time.

### Stat bonuses

`RecalculateEquipmentStats` (`:299`) rebuilds the list of equipped definitions and calls
`stats.ApplyEquipmentBonuses(...)` (`:320`). It **recalculates from scratch** rather than
incrementing, so an item removed correctly subtracts its bonus. The sums come from two pure helpers:
`ComputeMaxHealthBonus` (`:330`) and `ComputeMaxManaBonus` (`:346`).

### Restore (from a save)

`Restore(IReadOnlyList<ItemInstance> bag, IReadOnlyList<ItemInstance> equipment)` (`:368`) rebuilds
the containers from a loaded save. The bag is copied as-is; the equipment list is padded to the
right length and extra entries are ignored. It then recalculates stats. This is called by
`PlayerSaveTrigger` during the join (see [03](03-backend-and-persistence.md)).

### CountItem

`CountItem(string itemName)` (`:455`) counts how many of an item are in the bag, delegating to
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

`PlayerInventory.TryCraft(RecipeData recipe)` (`:395`) is the container-level craft. It:

1. Works on a scratch copy of the bag,
2. verifies every ingredient is present (consuming copies from the scratch copy),
3. verifies the output fits,
4. then removes the real ingredients and adds the outputs.

This is separate from `PlayerCrafting.TryCraft` (the station/proximity/XP half), which calls this one
once all its own checks pass. See [07](07-crafting-and-gathering.md). Note the output placement is
currently simple: multiple outputs are added at the same coordinates (`:435`).

## Common tasks

- **Add an item.** Create the asset (`Assets > Create > MMO > Item Definition`), name it, set the
  footprint, slot, level requirement and bonuses, and save it under `Assets/Resources/Items/`.
  It self-registers, so no wiring is needed. Then add EditMode tests for the rules.
- **Add a new equipment slot.** Add the value to `EquipmentSlot`, add it to `EquipmentSlots.AllSlots`
  and bump `Count`. Because the numeric values are a wire contract, add new slots at the **end**.
- **Give an item a new stat bonus.** Add a field to `ItemDefinition`, sum it in
  `ComputeMaxHealthBonus`/`ComputeMaxManaBonus` (or a new helper), and apply it in
  `RecalculateEquipmentStats`.
- **Make an item stack.** This does not exist yet. You would add a count field to `ItemInstance`,
  teach `TryAddItem` to merge, and teach `PlayerSaveMapper` to round-trip the count.
- **Test an equip rule.** Use `InventoryValidator.CanEquip` directly, or `TestItemDefinitions.Create`
  to build an item in code and drive `TryEquip` on a fresh `PlayerInventory` (see
  [10-testing](10-testing.md)).
