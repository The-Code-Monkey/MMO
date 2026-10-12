# 05 — Gameplay: stats, combat and abilities

This chapter covers health and mana, basic weapon attacks, and the data-driven ability system. It
follows one cast from button press to damage. Gather/craft and inventory live in their own chapters
([06](06-inventory-and-equipment.md), [07](07-crafting-and-gathering.md)).

## 1. PlayerStats — health, mana and level

`PlayerStats` (`Assets/Scripts/Gameplay/PlayerStats.cs:34`) is the component that holds a player's
vitals and level. It is server-authoritative: only `[Server]` methods change the values, and FishNet
copies them to clients.

The constants:

| Constant | Value | Line |
|---|---|---|
| `MaxHealth` | 100 | `PlayerStats.cs:36` |
| `MaxMana` | 50 | `PlayerStats.cs:37` |
| `DefaultLevel` | 1 | `PlayerStats.cs:40` |

The replicated values are SyncVars (`PlayerStats.cs:43-47`): health, mana, level, a max-health bonus
and a max-mana bonus. The bonuses come from equipment, so the *effective* caps are:

```csharp
public int EffectiveMaxHealth => MaxHealth + _maxHealthBonus.Value;   // :60
public int EffectiveMaxMana   => MaxMana   + _maxManaBonus.Value;     // :63
```

The public read-only properties are `Health`, `Mana`, `Level`, `MaxHealthBonus`, `MaxManaBonus`,
`EffectiveMaxHealth`, `EffectiveMaxMana`. The component also raises two events:

- `VitalsChanged` (`:72`) — health or mana moved (raised on server and clients). The HUD listens.
- `LevelChanged` (`:78`) — the level changed (server only). Persistence listens so a level-up saves
  immediately.

### Server mutators

Every mutation is a `[Server]` method:

- `TakeDamage(int)` (`:116`) and `Heal(int)` (`:122`),
- `SpendMana(int)` (`:128`) and `RestoreMana(int)` (`:134`),
- `SetLevel(int)` (`:145`),
- `ApplySave(int health, int mana, int level)` (`:156`) — used when a save record is loaded. It
  clamps health and mana to the effective caps.
- `ApplyEquipmentBonuses(int maxHealthBonus, int maxManaBonus)` (`:173`) — used when gear changes. If
  a bonus shrinks, current values are pulled down with it (`:179-180`).

The arithmetic itself lives in pure static helpers so it can be tested directly:
`ComputeNewHealth` (`:210`), `ComputeHealedHealth` (`:215`), `ComputeNewMana` (`:220`) and
`ComputeRestoredMana` (`:225`). They all clamp the result, so a negative damage amount cannot heal
and a negative heal cannot damage.

### The client request path

A client may ask to damage someone through `RequestDamage(int)` (`:187`), which returns unless the
caller owns the player, then sends `RpcRequestDamage` (`:195`). The server handler re-checks that the
sender is the owner before calling `TakeDamage`. That double check (FishNet also enforces ownership)
is the house style: never trust the client.

## 2. Basic weapon attacks

`PlayerCombat` (`Assets/Scripts/Gameplay/PlayerCombat.cs:19`) handles the "auto attack" a weapon
provides. The weapon is a `WeaponData` assigned only by the server.

- `RequestAttack(NetworkObject target)` (`:50`) — owner only; sends `RpcRequestAttack`.
- `RpcRequestAttack` (`:62`) — re-checks ownership, then calls `Attack`.
- `Attack(NetworkObject target)` (`:80`) — the server's checks, in order:
  1. a weapon must be equipped,
  2. the target must be spawned,
  3. the target must have a `TargetDummy` component (only dummies are attackable today),
  4. the target must be within `weapon.AttackRange`,
  5. then `dummy.TakeDamage(weapon.AutoAttackDamage)`.

`IsWithinAttackRange` (`:103`) is a pure helper using strict `<` — a target exactly on the boundary
is a miss. `FindLocal` (`:118`) finds the local player's `PlayerCombat` component on either the host
or client side, used by the mouse-click path.

`TargetDummy` (`Assets/Scripts/Gameplay/TargetDummy.cs:18`) is the punching bag. It has its own
`MaxHealth = 100` and a `[Server] TakeDamage`. Clicking it calls
`RequestAttackFromLocalPlayer`, which routes through the local `PlayerCombat`.

`NPCStats` (`Assets/Scripts/Gameplay/NPCStats.cs:28`) is the general NPC vitals component
(`MaxHealth = 50`) implementing the `ITargetSource` interface so a target frame can display it.

## 3. The ability system

Abilities are **data**, not code. A skill is an asset you edit in the Inspector; the same asset is
read by both the server and the client. Adding a plain damage or heal ability requires no new C#.

### AbilityData

`AbilityData` (`Assets/Scripts/Gameplay/AbilityData.cs:19`) is a `ScriptableObject` created with
`Assets > Create > MMO > Ability Data`. Its fields:

| Field | Meaning | Line |
|---|---|---|
| `AbilityName` | Display name; also what is replicated for the action bar. | `:30` |
| `ManaCost` | Mana the server spends when the cast is accepted. | `:33` |
| `Cooldown` | Seconds this slot stays locked after resolution. | `:36` |
| `CastTime` | Seconds from cast start to the effect landing. | `:39` |
| `CastRange` | Maximum distance the target may stand from the caster. | `:42` |
| `EffectRadius` | Radius around the effect origin that counts as a hit (0 = exact point). | `:48` |
| `Logic` | Which mechanic the server runs (see below). | `:51` |
| `EffectPower` | Raw damage/heal before scaling. | `:54` |

`AbilityData` has **no** `OnEnable` registration — it is pure data read from a reference, unlike
items and recipes which self-register (see [06](06-inventory-and-equipment.md) and
[07](07-crafting-and-gathering.md)).

### AbilityLogic — the three mechanics

`AbilityLogic` (`Assets/Scripts/Gameplay/AbilityLogic.cs`) is an enum, not a class hierarchy, so a
skill stays serialisable and testable:

| Value | Meaning | Line |
|---|---|---|
| `TargetedDamage` (0) | Damage every valid target within `EffectRadius` of the aimed point. | `:18` |
| `AreaDamage` (1) | Damage every valid target within `EffectRadius` of the **caster** (a melee cleave that ignores mouse aim). | `:24` |
| `SelfHeal` (2) | Heal the caster. Skips the range check. | `:29` |

The existing assets are `Assets/Data/Abilities/Fireball.asset` (`TargetedDamage`),
`Cleave.asset` (`AreaDamage`) and `Second Wind.asset` (`SelfHeal`).

### AbilityCastResult — why a cast was refused

`AbilityCastResult` (`Assets/Scripts/Gameplay/AbilityCastResult.cs:12`) is the enum of outcomes:
`Success`, `InvalidSlot`, `EmptySlot`, `AlreadyCasting`, `GlobalCooldownActive`, `SlotOnCooldown`,
`NotEnoughMana`, `OutOfRange`.

### AbilityCastRules — the server's bouncer

`AbilityCastRules.Validate` (`Assets/Scripts/Gameplay/AbilityCastRules.cs:27`) is a pure function
that takes the slot, the loadout, the cooldown tracker, the current mana, the distance to the target
and `now`, and returns one `AbilityCastResult`. It changes nothing and reads no clock, which is why
the order is pinned by tests. The order is **outside-in**, so a request that breaks several rules
always reports the earliest:

```csharp
if (slot < 0 || slot >= slots.Count)              return AbilityCastResult.InvalidSlot;
if (ability == null)                              return AbilityCastResult.EmptySlot;
if (cooldowns.IsCasting(now))                     return AbilityCastResult.AlreadyCasting;
if (!cooldowns.IsGlobalCooldownReady(now))        return AbilityCastResult.GlobalCooldownActive;
if (!cooldowns.IsReady(slot, now))                return AbilityCastResult.SlotOnCooldown;
if (currentMana < ability.ManaCost)               return AbilityCastResult.NotEnoughMana;
if (ability.Logic != AbilityLogic.SelfHeal &&
    distanceToTarget > ability.CastRange)         return AbilityCastResult.OutOfRange;
                                                  return AbilityCastResult.Success;
```

(`AbilityCastRules.cs:40-63`. A null `slots` or `cooldowns` throws — that is a server bug, not a
cast.)

### AbilityCooldownTracker — the server's stopwatch

`AbilityCooldownTracker` (`Assets/Scripts/Gameplay/AbilityCooldownTracker.cs:18`) tracks, per player:
when each slot unlocks, when the global cooldown unlocks, and when the current cast-time ability
finishes. It never reads a clock — every method takes `now`.

- A fresh tracker has every slot ready and is not casting (the arrays default to 0).
- `GlobalCooldown = 1.5f` (`:24`) is a rule of the combat system, not a per-asset field.
- `StartCooldown` (`:90`) only ever extends a cooldown, never shortens it.

### AbilityLoadout — the action bar

`AbilityLoadout` (`Assets/Scripts/Gameplay/AbilityLoadout.cs:21`) is a fixed array of up to
`MaxSlots = 6` abilities (`:24`). Empty slots are `null`.

`RebuildFromGear(WeaponData weapon, ArmorData armor)` (`:48`) fills slots from the weapon first
(slot 0 upward), then the armor, skipping nulls and **duplicates**. So a sword and a plate that both
grant Cleave cost one slot, not two.

### AbilityCaster — the pure brain

`AbilityCaster` (`Assets/Scripts/Gameplay/AbilityCaster.cs:21`) is plain C# (no FishNet), so the
whole pipeline is unit-testable. It owns a loadout, a cooldown tracker and a queue of pending casts.

`TryCast` (`:57`) does the full flow:

```csharp
manaCost = 0;
float distance = Vector3.Distance(casterPosition, targetPosition);
AbilityCastResult result = AbilityCastRules.Validate(slot, _loadout.Slots, _cooldowns,
    currentMana, distance, now);
if (result != AbilityCastResult.Success) return result;

AbilityData ability = _loadout.GetSlot(slot);
manaCost = ability.ManaCost;

double resolveAt = now + Math.Max(0d, ability.CastTime);
_cooldowns.BeginCastUntil(resolveAt);
_cooldowns.StartGlobalCooldown(now);
_cooldowns.StartCooldown(slot, ability.Cooldown, resolveAt);   // note: from resolveAt
_pending.Add(new PendingCast(slot, ability, targetPosition, resolveAt));
return AbilityCastResult.Success;
```

Two subtleties worth knowing:

- The slot cooldown starts at `resolveAt`, not `now`, so a long cast time does not eat recovery.
- The aim point is captured at request time, so a target that moves during the cast time is still
  resolved against where it *was*.

`TakeDueEffects(now)` (`:95`) returns (oldest first) every pending cast whose resolution time has
passed. `EquipGear` (`:41`) rebuilds the loadout and drops in-flight casts but keeps cooldowns.

### AbilityEffects — the geometry and arithmetic

`AbilityEffects` (`Assets/Scripts/Gameplay/AbilityEffects.cs:16`) is a pure static class:

- `ResolveOrigin(logic, casterPosition, targetPosition)` (`:23`) — `AreaDamage` and `SelfHeal` use
  the caster's position; everything else uses the target position.
- `Hits(origin, position, radius)` (`:42`) — true when the distance is within (clamped) radius.
- `ComputePower(ability)` (`:48`) — the clamped `EffectPower` (0 for a null ability).
- `SelectVictims(origin, radius, self, candidates)` (`:59`) — every non-null candidate inside the
  radius **except the caster**, so `AreaDamage` cannot hit its own caster.

### PlayerAbilities — the networked shell

`PlayerAbilities` (`Assets/Scripts/Gameplay/PlayerAbilities.cs:40`) is the `NetworkBehaviour`
implementing `IAbilityBarSource`. It replicates six slot-name strings (one SyncVar per slot) so the
action bar knows what to draw, and it owns the cast request flow.

- `EquipGear(WeaponData, ArmorData)` (`:126`) — `[Server]`. Rebuilds the caster's loadout, then
  publishes the slot names.
- `RequestCast(int slot, Vector3 targetPosition)` (`:138`) — owner only; sends `RpcRequestCast`.
- `RpcRequestCast` (`:151`) — `[ServerRpc]`. Re-checks the sender owns the object, reads live mana
  from `PlayerStats`, calls `_caster.TryCast(...)`, and on success spends the mana and applies any
  due casts immediately.
- `Update` (`:177`) — the server also calls `ApplyDueCasts` every frame while casts are pending, so
  a cast-time ability resolves when its timer elapses.
- `ApplyEffect` (`:198`) — the dispatch per `AbilityLogic`:
  - `SelfHeal` → `caster.Heal(ComputePower)`,
  - otherwise → `ResolveOrigin`, `SelectVictims`, then `victims[i].TakeDamage(power)`.

`GetSlotName` (`:78`) is what the bar reads; the cooldown tracker is deliberately **not** exposed on
the interface, because cooldowns are server-only state and a client must not be able to display
invented readiness.

### Interfaces the UI binds to

| Interface | File | Implemented by |
|---|---|---|
| `IVitalsSource` | `Assets/Scripts/Gameplay/IVitalsSource.cs:17` | `PlayerStats` |
| `ITargetSource` | `Assets/Scripts/Gameplay/ITargetSource.cs:16` | `NPCStats` |
| `IAbilityBarSource` | `Assets/Scripts/Gameplay/IAbilityBarSource.cs:19` | `PlayerAbilities` |

Each is small and event-driven (`VitalsChanged`, `Changed`, `SlotsChanged`), which is what lets the
UI be tested with a plain fake instead of a live NetworkBehaviour. See
[08](08-ui-main-menu-and-hud.md).

## 4. Gear as ability sources

The design slogan is "you are what you wear": gear injects abilities.

- `WeaponData` (`Assets/Scripts/Gameplay/WeaponData.cs:23`) — `WeaponName`, `AutoAttackDamage`,
  `AttackRange`, and a list of `Abilities`.
- `ArmorData` (`Assets/Scripts/Gameplay/ArmorData.cs:18`) — `ArmorName`, `ArmorValue` (mitigation is
  not yet wired into the damage pipeline), and a list of `Abilities`.

`PlayerPrefabBaker` equips the player with `Assets/Data/Weapons/Broadsword.asset` by default.

## 5. One cast, end to end

```
CLIENT
  ActionBar button → ActionBarView.HandleSlotClicked → IAbilityBarSource.RequestCast
    → PlayerAbilities.RequestCast  (owner guard)
    → RpcRequestCast  ──────────────── ServerRpc ─────────────►
SERVER
  PlayerAbilities.RpcRequestCast
    → sender owns object? yes
    → read PlayerStats.Mana
    → AbilityCaster.TryCast → AbilityCastRules.Validate
        success
    → PlayerStats.SpendMana(manaCost)
    → schedule: BeginCastUntil / StartGlobalCooldown / StartCooldown
    → (later, Update) ApplyDueCasts → ApplyEffect
         TargetedDamage/AreaDamage: ResolveOrigin → SelectVictims → TakeDamage
         SelfHeal: Heal
    → SyncVar writes replicate to all clients
CLIENT
  PlayerStats.VitalsChanged fires → VitalBarView.Repaint
```

## Common tasks

- **Add a plain damage or heal ability.** Create the asset
  (`Assets > Create > MMO > Ability Data`), set its fields, and add it to a weapon or armor asset's
  ability list. No code needed. Full walkthrough in [11](11-extending-the-game.md).
- **Add a brand-new ability mechanic.** Add a case to `AbilityLogic`, implement it in
  `AbilityEffects` / `PlayerAbilities.ApplyEffect`, and cover it with an EditMode test.
- **Change the global cooldown.** Edit `AbilityCooldownTracker.GlobalCooldown` (`:24`). There is a
  test pinning it.
- **Make something attackable.** Give it the component `PlayerCombat.Attack` understands, or extend
  `Attack` with another `TryGetComponent` branch.
- **Change base health/mana.** Edit `PlayerStats.MaxHealth` / `MaxMana`. Note these are also the
  defaults in `PlayerSaveRecord`, so update both if you change them.
- **Adjust the default weapon.** Change `PlayerPrefabBaker.DefaultWeaponAssetPath` and re-run
  `Tools > MMO > Create Player Prefab`.
