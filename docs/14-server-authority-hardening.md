# 14 — Server authority hardening

This chapter documents the anti-cheat hardening added under issue #63. The rest of the docs say
"the client asks, the server decides"; this chapter covers the three places where the server used to
take a client at its word and what now stops it:

1. **Movement** — a client used to be the authority on its own position.
2. **Line of sight** — an attack or ability could land through a wall.
3. **Dupes** — the same physical item could be moved twice under concurrency.

Movement and line of sight are enforced by the same **pure-rules + networked-shell** pattern every
other gameplay system uses (see [01](01-architecture-overview.md)), so their decisions are pinned by
EditMode tests with no scene. Dupe protection is a **single-writer ownership registry** in the
process, backed by the backend's version-guarded writes across processes.

## 1. Movement is server-authoritative

A client never sends a position it believes it stands at. It sends an **input direction**, and the
server integrates that direction at its own speed. There is no field in the input path for a lie to
live in.

### MovementRules — the pure arithmetic

`MovementRules` (`Assets/Scripts/Gameplay/MovementRules.cs:20`) is plain maths with no clock and no
scene:

| Member | Meaning | Line |
|---|---|---|
| `SnapTolerance` | `0.01f` of slack so float noise and a jittery frame never read as a teleport. | `:26` |
| `DefaultGrace` | `1.25f` — extra distance tolerated on top of the nominal speed limit. | `:29` |
| `MaxDistance(maxSpeed, deltaSeconds, grace)` | The furthest a legitimate mover could travel in that time; negative inputs read as zero. | `:36` |
| `IsPlausible(last, reported, maxSpeed, deltaSeconds, grace)` | True when `reported` is within that budget of the last server position. | `:46` |
| `SnapBack(lastServerPosition)` | Where an offender is placed: the last legal position, never a partial correction. | `:61` |

The **grace multiplier** is what keeps honest high-latency movement from being snapped: a message
that arrives a few frames late may cover a little more ground than the strict limit, while a
teleport of any meaningful size stays far outside the budget.

### PlayerMover — the networked shell

`PlayerMover` (`Assets/Scripts/Gameplay/PlayerMover.cs:29`) is the **sole writer** of a player's
position. It is a `NetworkBehaviour` added to the player prefab **last** (`PlayerPrefabFactory.cs:132`)
so every behaviour before it keeps the NetworkBehaviour and RPC index it was assigned.

| Member | Role | Line |
|---|---|---|
| `MaxSpeed` | The server integration speed (`_maxSpeed`, default `6` m/s). | `:40` |
| `RequestMove(Vector2)` | Owner-side intent: sends the input direction. A non-owner gets no RPC. | `:60` |
| `RpcRequestMove(Vector2, sender)` | The `[ServerRpc]`; re-checks the sender owns the object. | `:74` |
| `ApplyInput(input, deltaSeconds)` | `[Server]`: normalises a too-long direction, then moves `MaxSpeed * dt` in it. | `:89` |
| `ApplyReportedPosition(position, deltaSeconds)` | `[Server]`: judges a position-bearing message with `MovementRules.IsPlausible`; a teleport snaps back. | `:106` |
| `Teleport(position)` | `[Server]`: a privileged reposition (ability, respawn) that *moves* the authoritative baseline. | `:125` |
| `LastAuthoritativePosition` | The last position the server itself observed (live transform before the first tick). | `:46` |

The component works with or without a `CharacterController`: with one the move is collision-aware,
without one the transform is moved directly, so the mover can be dropped on a bare capsule and
tested. A direct `Transform` write by anything else would be a bug — the mover is the one door.

```
client                                   server (PlayerMover)
  move stick ─► RequestMove(input) ─────► RpcRequestMove   (owner check)
                                             │
                                             ▼
                                          ApplyInput(input, dt)   ← server integrates; client never sent a position
                                             │
                                             ▼
                                          MoveBy(dir * MaxSpeed * dt) → CaptureAuthoritativePosition()
```

`ApplyReportedPosition` exists as the **belt** to `ApplyInput`'s braces: any future message that
carries a position (a correction, a buggy RPC) is still judged by `MovementRules`, so a rogue RPC
cannot smuggle movement past the budget.

## 2. Line of sight

An attack or ability must not reach what a wall is hiding. The decision is pure arithmetic; only the
raycast itself is engine code, and it sits behind a seam.

### SightRules — the pure verdict

`SightRules` (`Assets/Scripts/Gameplay/SightRules.cs:14`):

| Member | Meaning | Line |
|---|---|---|
| `CanSee(withinRange, withinCone, raycastClear)` | The one verdict: all three must hold. A single blocked ray refuses, exactly like being out of range. | `:24` |
| `WithinRange(eye, target, range)` | Strictly closer than `range`, matching the combat/gathering/looting boundary. | `:34` |
| `WithinCone(eye, forward, target, halfAngleDegrees)` | Inside the facing cone; a target at the eye counts, a zero `forward` sees nothing beyond the eye. | `:47` |

### IServerSight and PhysicsSight — the raycast seam

- `IServerSight` (`Assets/Scripts/Gameplay/IServerSight.cs:16`) has one method,
  `HasLineOfSight(eyePosition, targetPosition, target)` (`:24`): true when nothing solid stands
  between the two. Putting it behind an interface lets a test force a clear or blocked line without
  spawning a wall (see `FakeSight` in the tests).
- `PhysicsSight` (`Assets/Scripts/Gameplay/PhysicsSight.cs:17`) is the live implementation: one
  `Physics.Raycast` from the eye toward the target, triggers ignored. A hit on the **target itself**
  is a clear line, not cover — the ray ends on what it aimed at. `DefaultEyeHeight = 1.5f` (`:24`)
  is where the eye sits above the two-metre capsule's pivot.

### Where the decision is enforced

- **Basic attacks.** `PlayerCombat.Attack` (`Assets/Scripts/Gameplay/PlayerCombat.cs:103`) now needs
  the weapon's range, the facing cone **and** a clear ray. The whole verdict is one pure static,
  `PlayerCombat.IsVisibleToAttack(...)` (`:132`), so the geometry is pinned in EditMode. The eye
  height and cone are serialized tunables: `_eyeHeight` (default `PhysicsSight.DefaultEyeHeight`)
  and `_attackHalfAngleDegrees` (default `60`) at `:32-33`. The raycast is reached through the
  injectable `Sight` property (`:42`), which defaults to a real `PhysicsSight`.
- **Abilities.** `AbilityEffects.SelectVisibleVictims(eyePosition, victims, sight)`
  (`Assets/Scripts/Gameplay/AbilityEffects.cs:90`) filters an effect's victims down to those the
  caster can see, so a fireball centred just past a wall damages only what the wall did not hide.
  `PlayerAbilities.ApplyEffect` (`Assets/Scripts/Gameplay/PlayerAbilities.cs:212`) applies it to
  every non-self victim, using an eye at `PhysicsSight.DefaultEyeHeight` and the injectable
  `PlayerAbilities.Sight` property (`:65`). A null `Sight` is treated as a clear line, so a caller
  with nothing wired keeps the old behaviour rather than silently hitting nobody.

## 3. Dupe protection

Two players must never hold the exact same physical item. Two pieces make that structural instead of
merely unlikely.

### Server-minted item identity

Every item carries a unique id the server mints; a client can never supply one.

- `ItemInstance.InstanceId` (`Assets/Scripts/Inventory/ItemInstance.cs:39`) is the identity, added to
  the wire struct. `ItemInstance.NewInstanceId()` (`:119`) mints one (`Guid.NewGuid().ToString("N")`),
  `Create(...)` (`:74`) assigns it to every freshly created item, and
  `ItemInstance.WithIdentity()` (`:129`) mints one for a legacy instance that has none.
- `ItemSaveData.InstanceId` (`Assets/Scripts/Persistence/PlayerSaveRecord.cs:24`) round-trips the id.
  `PlayerSaveMapper` reads it back on load and **mints a fresh id when an older save carries an empty
  one** (`PlayerSaveMapper.cs:117`), and writes it out on save (`:145`). So a save written before
  instance ids existed still gets a trackable item the moment it is loaded.

### ItemOwnershipRegistry — the single writer

`ItemOwnershipRegistry` (`Assets/Scripts/Inventory/ItemOwnershipRegistry.cs:43`) is the in-process,
single-writer source of truth for **who holds which item instance**. Every inventory mutation that
creates, moves or consumes an item routes through it.

`OwnershipMove` (`:11`) is the outcome of one operation; `Ok` is the only success and every other
value is a refusal the caller must honour:

| Operation | Meaning | Line |
|---|---|---|
| `Grant(instanceId, holder)` | Record that `holder` now holds the id. First grant wins; re-granting to the same holder is `Ok`; a second holder is `DuplicateDetected`. | `:64` |
| `Transfer(instanceId, from, to)` | Atomically move an id. `NotHeld` when the source does not hold it; `DuplicateDetected` when the destination already does. | `:87` |
| `Release(instanceId, holder)` | Forget an item (consumed, destroyed, dropped). Only the current holder may; anyone else gets `NotHeld`. | `:113` |
| `TryGetOwner` / `IsHeldBy` / `Count` / `Clear` | Read helpers. | `:132`, `:143`, `:49`, `:154` |

The registry is guarded by a lock so the guarantee holds even for a caller that drives it off the
main server tick, and it is deliberately cheap when everything runs single-threaded. The structural
half of dupe protection is this: it does not make duplication *unlikely*, it makes the same physical
item *impossible to hold twice*.

### Structural (in-process) versus transactional (cross-process)

| Scope | What settles it |
|---|---|
| One process (a zone) | `ItemOwnershipRegistry` — the single writer; a duplicate grant is refused. |
| Two processes (two zones, one item) | The backend's compare-and-swap / single transaction (#59). Auction state is written with a `StorageVersion` and a losing race throws `Persistence.StorageVersionConflictException`, which the caller retries. The gold/item transfer for a purchase is the atomic Nakama Lua RPC `auction_buy` (module `nakama/modules/auction_buy.lua`). |

The registry makes the in-process race impossible; the backend CAS makes the cross-process race
settle in exactly one winner.

## 4. Testing these guarantees

The hard decisions are pure, so they are pinned in EditMode; the networked shells get PlayMode
coverage where a wall or a socket is actually needed.

| Guarantee | Tests |
|---|---|
| Movement budget (a 50-unit, 0.1 s hop is refused; a normal run passes; snap-back) | `Assets/Tests/EditMode/MovementRulesTests.cs` |
| Sight verdict (range, cone, blocked ray) | `Assets/Tests/EditMode/SightRulesTests.cs` |
| Raycast seam (target is not cover, triggers ignored, zero-length line) | `Assets/Tests/EditMode/PhysicsSightTests.cs` + `FakeSight` |
| Duplicate refusal, transfer/release semantics, save round-trip | `Assets/Tests/EditMode/ItemOwnershipRegistryTests.cs` |
| No instance id under two holders after a concurrency storm | `Assets/Tests/EditMode/ItemOwnershipRegistryStressTests.cs` |
| The mover integrates input and snaps an impossible delta | `Assets/Tests/EditMode/PlayerMoverTests.cs`, `Assets/Tests/PlayMode/PlayerMoverPlayModeTests.cs` |
| A click behind cover lands nothing; the same click lands once the line clears | `Assets/Tests/PlayMode/PlayerCombatPlayModeTests.cs` |

The stress suite (`ItemOwnershipRegistryStressTests`) starts its contenders on a `Barrier` and fires
many simultaneous pickups, trades and loots at the same instance id. Because the registry is
lock-guarded, "exactly one winner per id" holds under **every** scheduling order, so the assertions
are deterministic rather than flaky. The cross-process race needs a live Nakama profile and is
covered by the integration suite, not EditMode.

## Common tasks

- **Change how fast players may move.** Edit `_maxSpeed` on the `PlayerMover` component (default
  `6` m/s). The budget math stays the same; `MovementRules` only reads the speed you pass it.
- **Loosen or tighten lag tolerance.** Edit `MovementRules.DefaultGrace` (`:29`) or
  `SnapTolerance` (`:26`). There are tests pinning both boundaries.
- **Change how far a player can see/attack.** Edit `_attackHalfAngleDegrees` on `PlayerCombat`
  (default `60`) or the weapon's `AttackRange`; both feed `PlayerCombat.IsVisibleToAttack`.
- **Change the eye height.** Edit `PhysicsSight.DefaultEyeHeight` (`:24`); `PlayerCombat` and
  `PlayerAbilities` both seed their eye from it.
- **Turn off line of sight for a test or a build.** Replace the `Sight` property on `PlayerCombat`
  / `PlayerAbilities` with a stub whose `HasLineOfSight` always returns true (this is what
  `FakeSight` does).
- **Block a new system behind line of sight.** Route the victim list through
  `AbilityEffects.SelectVisibleVictims(eye, victims, sight)` before applying it — the filtering is a
  pure list operation, so it needs no scene.
- **Add a new holder of items.** No code: every holder is just a string (an account id, a guild id,
  a "bank" id). Route its inventory mutations through `Grant` / `Transfer` / `Release`.
- **Track which item is which.** Use `ItemInstance.NewInstanceId()` at creation and carry
  `ItemInstance.InstanceId`; the save layer round-trips it for you.
- **Change the attack range.** Edit `WeaponData.AttackRange` (see
  [05](05-gameplay-stats-combat-abilities.md#2-basic-weapon-attacks)); range is checked by
  `PlayerCombat.IsWithinAttackRange`.
