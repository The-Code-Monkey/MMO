# 18 — Player controls

This chapter is the control scheme end to end: the keys, what the client sends, what the server
decides, and the camera and pointer that wrap around it. The one rule behind all of it is the
project's oldest: **the client expresses intent; the server decides.** Nothing here lets a client
say where it is, or how fast it got there.

## 1. The keys at a glance

| Input | What it does | Where the rule lives |
|---|---|---|
| `W` / `S` | Walk forward / back along the heading. Backward keeps 0.55× the speed. | `MoveIntentMath` |
| `Q` / `E` | Strafe left / right at 0.8× speed. | `MoveIntentMath` |
| `A` / `D` | Turn the character at 120°/s — or strafe, while RMB is held. | `HeadingMath` |
| Hold `RMB` | Pin the heading to the camera and steer the camera by dragging. | `ThirdPersonCameraRig`, `PlayerMover` |
| Hold `LMB` | Orbit the camera without touching the heading. | `ThirdPersonCameraRig` |
| `LMB`+`RMB` | Run forward, the classic shortcut. | `PlayerMoveInput` |
| `Space` | Jump, if grounded. | `JumpMath`, `PlayerMover` |
| Wheel | Move the orbit arm between 0.3 m and 15 m. | `CameraOrbitMath` |
| Click | Activate the station, teller, node, NPC, chest or dummy under the pointer. | `PlayerWorldPointer` |
| `M` `E` `G` `T` `B` `1`–`6` | Mailbox, interact, mount, trade, auction, abilities. | `GameInputActions` |

Every one of these is a `GameAction` and therefore rebindable — including the movement keys and the
mouse buttons. See [17](17-client-performance-and-settings.md#3-input-rebinding).

## 2. What a key press becomes

The client reads devices; it does not move anything. `PlayerMoveInput` packages the keys as **local
axes plus context** and re-states them about twenty times a second while they are held:

```
keyboard / mouse
  → PlayerMoveInput        (local strafe, forward, turn, face-camera, camera yaw, jump)
      ─ ServerRpc ─►
  → PlayerMover.StepIntent (server: resolve heading, scale, rotate into world X/Z)
  → ApplyInput             (server: move MaxSpeed * dt)
  → CharacterController.Move
  → NetworkTransform       (replicates position and heading to everyone)
```

The payload is deliberately *not* a world direction. Only the server knows the heading, so only the
server can decide whether "forward" means along the heading or along the camera. Sending the raw
keys also means a doctored client cannot turn faster than one axis allows or walk wider than one
unit: `RpcRequestMoveIntent` clamps every axis before it is used.

A release sends a zero immediately rather than waiting for the next tick, so the character stops
when the key stops. A movement intent also **cancels any destination in flight**, which is why the
keyboard and the server's walk-to-a-point path cannot fight over the next step.

## 3. Heading and the camera frame

**No RMB held.** The keys are relative to the character. A and D turn the heading at 120°/s; W and
S walk along it. A character can therefore walk forward while facing a different way only by
strafing, which is the point of the 0.8× penalty.

**RMB held.** The heading is pinned to the camera's horizontal forward, so the character faces where
the player looks, and A/D become strafing rather than turning. This is what makes a moving camera
orbit feel like steering the character rather than the view.

The heading is written on the server (`PlayerMover.StepIntent`) and replicated by the
`NetworkTransform`. That matters outside the camera: combat builds its 60° attack cone from
`transform.forward` **on the server**, so the cone the server uses and the direction the player sees
must be one value. The cosmetic model follows the same heading — it rotates toward the root, not
toward the direction of travel, so a strafing body keeps facing forward instead of leading with a
shoulder.

## 4. The orbit camera

`ThirdPersonCameraRig` is the one camera a client looks through. It is a perspective eye on an arm
that pivots at the character's **neck** — 1.6 m above the feet, not above the root, because the root
is the middle of a 2 m capsule (`CameraOrbitMath.Pivot`).

- **Orbit**: dragging either button changes yaw and pitch. Pitch is clamped to −35°…+70°; it never
  turns the character.
- **Zoom**: the wheel moves the arm between 0.3 m and 15 m, damped over 0.12 s rather than snapped.
- **Clip**: a sphere (radius 0.25 m) swept back from the pivot shortens the arm when scenery is in
  the way, so the camera never ends up on the far side of a wall. The local avatar is skipped by
  that probe and hidden when the eye is inside it (below 1.2 m). The near clip is 0.05 so the
  closest arm still renders.
- **Binding**: the rig follows the player this client **owns**, registered by
  `LocalPlayerCameraTarget`. Every client holds every avatar, so a tag search is only the fallback
  for a scene with no local player — a tag search can find a stranger and does not get to decide
  who the camera follows.

The rig lives in each zone scene, not on the player prefab, and survives zone loads as a
`DontDestroyOnLoad` singleton. `LocalCameraBaker` authors it; see
[09](09-editor-tooling-and-scenes.md#localcamerabaker).

## 5. Pointing at the world

`PlayerWorldPointer` resolves a press and release into a **world click** only when the pointer
travelled no more than 6 px (`WorldClickRules`). That single rule is what lets the same button both
orbit the camera and open an anvil: a drag is a drag, and a click is a click.

A world click is routed to whatever the raycast hits, through the intent entry point that object
already exposes — `CraftingStation`, `BankTeller`, `ResourceNode`, `DialogueNpc`, `LootChest`,
`TargetDummy`. A click on a panel is the panel's, not the world's. A click on the world itself
closes an open crafting or bank window.

**Out of range is advice, not a refusal.** The pointer checks the same reach rule the server applies
and writes `Out of range.` to the chat log, then sends the request anyway. The client's position is
an interpolation; a stale one must never block an interaction the server would allow, and the
server re-checks the distance when it answers.

The pointer also supplies the aim point ground-targeted abilities use (`AimPoint`), so the action bar
casts where the player is looking.

## 6. Cursor and focus

The pointer is captured — hidden and locked — only while the player is steering the camera: a press
over the world, not over UI, starts it; a release, `Esc`, or a modal that takes the keyboard
(`ClientInput.GameplayBlocked`) ends it. There is no game state that pauses the world, and the
cursor is never captured by a click that belongs to a panel.

## 7. Tests

The decisions in this chapter are pure and covered in EditMode:

| Maths | Test |
|---|---|
| Walking intent, backward/strafe multipliers, camera rotation | `MoveIntentMathTests` |
| Turning rate, clamping, wrapping | `HeadingMathTests` |
| Jump impulse from the declared apex | `JumpMathTests` |
| Pivot, pitch clamp, zoom clamp/damping, clip, avatar threshold | `CameraOrbitMathTests` |
| Click-versus-drag threshold | `WorldClickRulesTests` |

## Common tasks

- **Rebind a key.** Nothing to change: every control is a `GameAction` and appears in the settings
  panel. To add a new one, add the enum value, bind it in `GameInputActions.Create`, and read it
  through `ClientInput.WasPressed` (edges) or `IsHeld` (movement).
- **Change how moving feels.** The backward and strafe penalties are constants on
  `MoveIntentMath`; the turn rate is `HeadingMath.DegreesPerSecond`. Both are server-side, so a
  client cannot disagree.
- **Change the jump.** `JumpMath.Gravity` and `JumpMath.JumpHeight` are the only numbers; the
  impulse is derived, so the apex stays what the constant says.
- **Change the camera.** Pivot height, pitch clamp, zoom range, damping, clip radius and the
  avatar-hide threshold are all in `CameraOrbitMath`, with the rig's fields mirroring them so a
  scene can tune them without a code change.
- **Make something clickable.** Give it a non-trigger collider and a public
  `RequestXFromLocalPlayer()` intent, then add a branch to `PlayerWorldPointer.HandleWorldClick`
  with the reach rule and the advisory. Do not add `OnMouseDown`: it fires on press, so the first
  pixel of an orbit drag would activate the object.
- **The camera is following someone else.** Confirm the player registered through
  `LocalPlayerCameraTarget`. Unbinding is owner-scoped, so only the copy that bound the rig may clear
  it; a remote avatar's teardown is a no-op for the camera.
- **Run the tests.** `Assets/Tests/EditMode` holds the table above; CI runs them for every pull
  request into any branch (see [10](10-testing.md#in-ci)).
