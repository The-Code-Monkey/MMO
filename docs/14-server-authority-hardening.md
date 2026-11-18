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

`PlayerMover` (`Assets/Scripts/Gameplay/PlayerMover.cs:31`) is the **sole writer** of a player's
position. It is a `NetworkBehaviour` on the player prefab (`PlayerPrefabFactory.cs:164`); behaviours
added after it — the walking driver among them — are appended so every earlier behaviour keeps the
NetworkBehaviour and RPC index it was assigned.

| Member | Role | Line |
|---|---|---|
| `MaxSpeed` | The server integration speed (`_maxSpeed`, default `6` m/s). | `:65` |
| `RequestMoveTo(Vector3)` | Owner-side intent: sends a destination; the server plans the route and walks it. | `:89` |
| `RequestMoveIntent(input, turn, faceCamera, cameraYaw, jump)` | Owner-side intent: the raw keys, re-stated while held. A non-owner gets no RPC. | `:103` |
| `RpcRequestMoveTo(Vector3, sender)` | The `[ServerRpc]` for a destination: re-checks the sender, samples the destination onto the NavMesh, then plans the route. | `:265` |
| `RpcRequestMoveIntent(input, turn, faceCamera, cameraYaw, jump, tick, sender)` | The `[ServerRpc]` for the raw keys: re-checks the sender, clamps every axis, cancels any destination in flight, and latches a jump. | `:121` |
| `StepIntent(deltaSeconds)` | Server: turns the intent into a heading and a world direction — heading-relative normally, camera-relative while facing the camera. | `:240` |
| `TryJump()` | Server: leaves the ground if grounded, keeping the horizontal velocity at takeoff. | `:160` |
| `StepVertical(deltaSeconds)` | Server: gravity, the vertical move and the ground probe. | `:176` |
| `ApplyInput(input, deltaSeconds)` | `[Server]`: normalises a too-long direction, then moves `MaxSpeed * dt` in it. | `:403` |
| `ApplyReportedPosition(position, deltaSeconds)` | `[Server]`: judges a position-bearing message with `MovementRules.IsPlausible`; a teleport snaps back. | `:456` |
| `Teleport(position)` | `[Server]`: a privileged reposition (ability, respawn) that *moves* the authoritative baseline. | `:475` |
| `LastAuthoritativePosition` | The last position the server itself observed (live transform before the first tick). | `:74` |

Two intents reach the mover: a destination and the raw keys. The keys are sent as local axes plus a
turn axis and a face-camera flag, because only the server knows the heading — it alone decides
whether "forward" means along the heading or along the camera. While the player faces the camera the
heading is pinned to it and A/D strafe; otherwise A/D turn the heading at 120°/s and the keys are
relative to it. The server scales backward and strafe, normalises, and integrates at its own speed;
receiving any intent cancels a destination in flight, so the keyboard and a nav walk cannot fight
over the next step. The heading is written only here and replicated by the `NetworkTransform`, which
is what keeps the server-side attack cone and the player's view agreeing.

Jumping is the one motion the client cannot describe: it is a request, and the server decides. A
jump is only honoured while the body stands on something — `isGrounded` backed by a short downward
spherecast, because the flag false-negatives on slopes and stair lips. The horizontal velocity at
takeoff is captured and re-applied unchanged until landing, so air input cannot steer; gravity is
`-20` m/s² and the impulse is derived from the declared apex, not typed in twice. A flying mount
keeps its gravity-free path and cannot jump.

The player object ships a `CharacterController`, so a move is collision-aware: the body is swept
and a wall stops it. The mover still works without one — the transform is moved directly — which is
what lets it be dropped on a bare capsule in a test. A direct `Transform` write by anything else
would be a bug: the mover is the one door.

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

### Click-to-move — the server owns the route, not just the direction

A click-to-move request carries a **destination**, not a direction, so the server has to decide
*how* the player gets there. `RpcRequestMoveTo` samples the destination onto the zone NavMesh
(a destination off the mesh is refused) and then plans a route over that mesh with
`NavMesh.CalculatePath`. The route's corners are walked one at a time by the same
`ApplyInput`/`MoveBy` pair, so the movement rules above still apply to every step.

Planning the route is what makes solid objects solid: a crafting station carries a
`NavMeshObstacle` that carves a hole in the NavMesh, and a route planned over a holed mesh
**bends around the hole** instead of running through the workbench. The client never gets to
choose that line — it only says where the player wants to stand, and the server walks them
around whatever is in the way. If no complete route exists (no mesh, unreachable target), the
mover falls back to walking straight at the destination, exactly as it did before.

The route bookkeeping itself is pure arithmetic over positions and lives in `WalkPath`
(`Assets/Scripts/Gameplay/WalkPath.cs`), so it is unit-testable without an engine, a clock or a
network.

### Walking to a station before it can be used

A click on a crafting station is a **destination request**, not a "open this now" request: the server
refuses a station outside the crafting reach, so a far-away click has to become a walk.
`PlayerClickToMove.BeginStationApproach` asks the server to walk to the station's stand-off point
(two metres in front of it), remembers the station as pending, and opens it only once the walk is
over — the click-to-move marker clearing is the "the walk ended" signal.

The client checks the reach on its own side too (`PlayerCrafting.IsWithinReach`, the same rule the
server applies), but only to avoid asking for a request the server would refuse. The panel still
opens from the server's payload: the client cannot open a station it may not use, and the server
re-checks the distance when it answers.

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
| Two processes (two zones, one item) | The backend's compare-and-swap / single transaction (#59). Auction state is written with a `StorageVersion` and a losing race throws `Persistence.StorageVersionConflictException`, which the caller retries. The gold/item transfer for a purchase is the Nakama Lua RPC `auction_buy` (module `nakama/modules/auction_buy.lua`): it claims the listing with a CAS write first so exactly one buyer wins, then moves gold as the commit point. The transaction record and index writes that follow are best-effort audit metadata — a failure there is logged, not surfaced as a `CONFLICT` (which would strand the already-moved gold). |

The registry makes the in-process race impossible; the backend CAS makes the cross-process race
settle in exactly one winner.

## 3a. Player-facing moderation notices

Detection and enforcement are separate from communication. `MMO.Moderation` gives the server one
service and the player one shell:

- `ModerationRules` (pure) defines the stable `ACTION_CATEGORY` message code, the plain-language
  explanation for it, and the report throttle (`MaxReportsPerHour`, rolling window, same-target
  dedupe). A notice carries a code and an expiry, never detector evidence — telling a cheater what
  tripped the detector teaches the cheat.
- `ModerationService` files throttled reports into `moderation_reports` and stores notices in
  `moderation_notices`, both through `IModerationRepository` with the same optimistic version check
  as every other record. `NakamaModerationRepository` is the production implementation.
- `PlayerModeration` is the intent shell on the player prefab: `[ServerRpc]` report intents re-check
  ownership and resolve the reporter from the join cache, and notices return over
  `[TargetRpc]` JSON. `NotifyAsync` is what a kick/warn calls before disconnecting; `RequestNotices`
  replays anything still in force on reconnect.
- `ModerationNoticeOverlay` on the owning client prints `ModerationRules.Describe(notice)`. There is
  deliberately no unban or appeal path in this surface.

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
