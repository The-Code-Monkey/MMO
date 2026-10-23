# 11 — Extending the game

This chapter is a set of copy-paste-friendly walkthroughs. Each one names the files you touch and the
tests you add. Read [12-conventions-and-checklist](12-conventions-and-checklist.md) first if you have
not — the rules there apply to everything below.

## 1. Add a new ability (data only)

A plain damage or heal ability needs **no new code**.

1. **Create the asset.** `Assets > Create > MMO > Ability Data`. Give it a name, mana cost, cooldown,
   cast time, cast range, effect radius, logic (`TargetedDamage` / `AreaDamage` / `SelfHeal`) and
   effect power. Save it under `Assets/Data/Abilities` (for example `Fireball.asset` already exists).
2. **Expose it.** Put the ability in a `WeaponData` or `ArmorData` asset's ability list
   (`Assets > Create > MMO > Weapon Data` / `Armor Data`). On the server,
   `PlayerAbilities.EquipGear(weapon, armor)` loads the list into the player's action bar.
3. **Know what the server checks.** Every cast runs through `AbilityCastRules.Validate`
   (`Assets/Scripts/Gameplay/AbilityCastRules.cs:27`), in this order: slot valid → slot not empty →
   not already casting → global cooldown ready → slot cooldown ready → enough mana → in range (unless
   self-heal). See [05](05-gameplay-stats-combat-abilities.md) for the full explanation.
4. **Write the test first.** A fresh `AbilityCooldownTracker` starts with every slot ready, and all
   time is passed in, so there is nothing to wait for:

```csharp
[Test]
public void Validate_RejectsACastTheCasterCannotPayFor()
{
    AbilityData fireball = NewAbility("Fireball", manaCost: 20);
    AbilityCooldownTracker tracker = new();

    Assert.AreEqual(
        AbilityCastResult.NotEnoughMana,
        AbilityCastRules.Validate(0, Slots(fireball), tracker,
            currentMana: 5, distanceToTarget: 10f, now: 0d));
}
```

## 2. Add a new ability mechanic (code)

If the effect is genuinely new (not just new numbers):

1. Add a case to `AbilityLogic` (`Assets/Scripts/Gameplay/AbilityLogic.cs`).
2. Handle it in `AbilityEffects.ResolveOrigin` (`Assets/Scripts/Gameplay/AbilityEffects.cs:23`) if it
   needs a different origin, and in `PlayerAbilities.ApplyEffect`
   (`Assets/Scripts/Gameplay/PlayerAbilities.cs:198`) for the actual effect.
3. Add an EditMode test in `AbilityEffectsTests` and, if it changes network behaviour, a PlayMode
   test.

## 3. Add a new item

```
1. Assets > Create > MMO > Item Definition   → e.g. "Iron Helm"
   - GridWidth / GridHeight   → footprint in the bag grid
   - EquipSlot                → Head / Chest / MainHand / ...
   - RequiredLevel            → enforced by InventoryValidator.CanEquip
   - MaxHealthBonus / MaxManaBonus → summed over equipped items and applied through
     PlayerStats.ApplyEquipmentBonuses whenever equipment changes
2. Save it under Assets/Resources/Items/. The asset self-registers in OnEnable, and
   ItemDefinitionRegistry.Resolve falls back to Resources.Load, so no wiring is needed.
3. Test the rules, not the asset:

   [Test]
   public void CanEquip_False_WhenPlayerBelowRequiredLevel()
   {
       ItemDefinition helm = TestItemDefinitions.Create("Iron Helm", requiredLevel: 10);
       Assert.IsFalse(InventoryValidator.CanEquip(playerLevel: 5, definition: helm));
   }

4. To make it obtainable: grant it on the server through PlayerInventory.TryAddItem
   (a gather, a craft output, or a drop). Never grant an item from a client request without
   a server check.
```

## 4. Add a new recipe

```csharp
// 1. Assets > Create > MMO > Recipe Data
//    RecipeName "Iron Sword", Ingredients [ Iron Ingot x5 ], Output Broadsword x1,
//    StationType Anvil, RequiredCraftingLevel 3, CraftingXp 25
//
// 2. Save the asset under Assets/Resources/Recipes so the server finds it: WorldBootstrap
//    calls RecipeRegistry.LoadAllFromResources() at start-up, and each recipe also self-registers
//    in OnEnable (RecipeData.OnEnable → RecipeRegistry.Register). The server-side check before
//    anything is granted is exactly what PlayerCrafting.TryCraft runs
//    (Assets/Scripts/Crafting/PlayerCrafting.cs:160):

RecipeData recipe = RecipeRegistry.Resolve(recipeName);
bool ok = recipe != null
    && Vector3.Distance(transform.position, station.transform.position) <= MaxCraftDistance
    && CraftingValidator.CanCraft(inventory.Items, recipe, station.StationType, CraftingLevel);

// 3. If ok, inventory.TryCraft(recipe) consumes the ingredients and adds the output,
//    then AwardCraftingXp(recipe.CraftingXp) runs. The client only sends a recipe name.
```

Add EditMode tests in `CraftingValidatorTests` for any new rule, and a PlayMode test if you change the
station interaction.

## 5. Add a resource node to a scene

1. Run `Tools > MMO > Create Resource Node Prefab` (if the prefab does not exist).
2. Drag it into the zone scene.
3. Create the resource's data (`Assets > Create > MMO > Resource Definition`): pick its kind, the
   item it drops, how many units a node holds, the tool kind and tier it demands, and its timings.
   Save it under `Assets/Resources/ResourceDefinitions/`.
4. Point the node prefab at the definition — that is the node's whole configuration.
5. Add EditMode tests in `ToolRulesTests`, `ResourceDefinitionTests` or `ResourceNodeTests` if you
   change the rules.

To add a **tool** item: create the item (`Assets > Create > MMO > Item Definition`), set its
`ToolKind` and `ToolTier`, and save it under `Assets/Resources/Items/`. The tier lives on the item,
so "Bronze Pickaxe" and "Iron Pickaxe" are two items of one kind at two tiers — and a T1 pickaxe
will not open a T2 vein.

## 6. Add a new zone server

```sh
# 1. Duplicate an existing zone scene, or build a fresh one with the bakers:
#      Tools > MMO > Create NetworkManager In Scene
#      Tools > MMO > Create Player Prefab          (once, shared)
#      Tools > MMO > Create Main Menu Scene        (once, shared by all zones)
# 2. On the baked MMOConnectionGate, set MMOConnectionSettings → Use Token Auth
#    (TokenAuthBootstrap re-wires the gate's validator from that flag at runtime).
# 3. Give the scene a distinct port and add it to Build Settings.
# 4. Boot the backend first so the gate can vouch for session tokens:
docker compose up -d
# 5. Press Play — with token auth on, joins without a valid session token are refused.
```

All servers must share `BackendSettings.ServerDeviceId` and the zone-transfer signing secret. See
[04](04-networking-and-the-join-flow.md).

## 7. Add a HUD element or a new UI screen

1. Write a view component with a `Bind(ISomeSource source)` method and a `Repaint` that reads from
   the interface. Do **not** add an `Update` — react to a source event.
2. Add the source interface (or extend an existing one like `IVitalsSource`), and implement it on the
   relevant `NetworkBehaviour`.
3. Construct the view in `NetworkHudFactory.Create` (`Assets/Scripts/UI/NetworkHudFactory.cs:63`) and
   add it to `NetworkHud.SetViews` / `BindPlayer`.
4. Add an EditMode test in the matching `*ViewTests` file using a tiny in-memory source.

See [08](08-ui-main-menu-and-hud.md) for the full UI pattern.

## 8. Add a brand-new server-authoritative feature

Follow the pure-core / networked-shell split ([01](01-architecture-overview.md#4-the-pure-core--networked-shell-pattern)):

1. **Pure core first.** Write a static class or plain class that takes all inputs (including `now`)
   and decides. Write EditMode tests for it.
2. **Networked shell.** Add a `NetworkBehaviour` (or extend an existing one) with:
   - `[ServerRpc]` request handlers from clients that re-check `sender` owns the object,
   - `[Server]` methods that run the pure logic and mutate SyncVars/SyncLists,
   - an event the UI can subscribe to.
3. **Wire it.** If it needs a component on the player prefab, add it in
   `PlayerPrefabFactory.Create` (`Assets/Scripts/Net/PlayerPrefabFactory.cs:72`) and re-run
   `Tools > MMO > Create Player Prefab`.
4. **Test.** EditMode for the core, PlayMode for the networked path.

Example shell shape:

```csharp
public sealed class PlayerCooking : NetworkBehaviour
{
    public void RequestCook(string recipeName)      // client intent
    {
        if (!IsOwner) return;
        RpcRequestCook(recipeName);
    }

    [ServerRpc]
    private void RpcRequestCook(string recipeName, NetworkConnection sender = null)
    {
        if (sender == null || !OwnerMatches(sender)) return;   // never trust the client
        TryCook(recipeName);
    }

    [Server]
    public bool TryCook(string recipeName)
    {
        // 1. validate with pure rules, 2. mutate replicated state, 3. raise an event
        return false;
    }
}
```

## 9. Add a field to the save

1. Add the field to `PlayerSaveRecord` (`Assets/Scripts/Persistence/PlayerSaveRecord.cs`).
2. Capture it in `PlayerSaveMapper.Capture` (`PlayerSaveMapper.cs:21`).
3. Apply it in `PlayerSaveMapper.Apply` (`PlayerSaveMapper.cs:69`) and, if it belongs to a live
   component, in `PlayerSaveTrigger.OnStartNetwork` (`PlayerSaveTrigger.cs:51`).
4. Bump `PlayerSaveRecord.CurrentSchemaVersion` if old saves need migrating.
5. Add an EditMode round-trip test.

## 10. Add a new social intent

Party and guild actions are pure rules (`PartyRules`, `GuildRules`) plus a networked shell
(`PlayerParty`, `PlayerGuild`) against the `SocialHubService`. To add one:

1. Add the rule/result to the relevant rules class with EditMode tests.
2. Add the intent method + `[ServerRpc]` on `PlayerParty`/`PlayerGuild` following the existing ones.
3. Add the matching operation to `SocialHubService` (read-modify-write with the CAS retry loop) and
   `ISocialRepository` if it needs storage.
4. See [13](13-social-party-and-guild.md) for the whole picture.

## Common tasks

- **"Where do I put my new file?"** Match the feature to the folder/namespace table in
  [01](01-architecture-overview.md#3-how-the-code-is-split-into-assemblies).
- **"Do I need to touch the scene?"** Usually no. Baked scenes read their wiring from components;
  adding a component in `PlayerPrefabFactory` plus re-running the player prefab baker is the usual
  step.
- **"Do I need a test?"** Yes. Pure logic → EditMode; anything over the wire → PlayMode. See
  [10](10-testing.md).
- **"Can the client just set this value?"** No. Client sends an intent; server validates and writes.
