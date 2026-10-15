# 08 — UI: main menu and HUD

This chapter explains how the game's user interface is built, the login flow it drives, and the HUD
that renders replicated state.

## 1. The rule: the UI is built entirely in code

There are **no UI prefabs and no UI scene assets**. Every screen is constructed at runtime by a
static factory method using `new GameObject(...)`, `AddComponent<T>()`, and manual `RectTransform`
layout. This is a deliberate choice:

- A scene, a fresh process and a unit test must produce an **identical object graph**.
- The factory is the single source of truth; a scene only holds a small bootstrap component that
  calls the factory.
- Views never poll. They are "dumb": a factory (or a test) wires them with `Set...` methods, and they
  repaint only when an event fires. No view class defines `Update` or `LateUpdate`.

Two helpers support this:

- `ViewObject.Destroy(GameObject)` (`Assets/Scripts/UI/ViewObject.cs:8`) picks `DestroyImmediate` in
  edit mode and `Destroy` in play mode, so factories work in both.
- `UIFont.Default` (`Assets/Scripts/UI/UIFont.cs:16`) lazily resolves Unity's built-in
  `LegacyRuntime.ttf`, because no font asset is committed.

## 2. The Main Menu

### The factory

`MainMenuFactory` (`Assets/Scripts/UI/MainMenuFactory.cs:21`) builds the whole login screen: a
full-screen backdrop, a centered login panel with a title, username and password fields, a Sign In
button, and a status line. `Create(bool createEventSystem = true)` (`:45`) returns a `LoginView`
wired to those widgets. The palette (`BackgroundColor`, `PanelColor`, `FieldColor`, `AccentColor`,
...) and the 1920×1080 reference resolution are constants on the factory.

If `createEventSystem` is true it also ensures a UI `EventSystem` exists (via
`NetworkHudFactory.EnsureEventSystem`).

### The view

`LoginView` (`Assets/Scripts/UI/LoginView.cs:20`) is the component on the menu root. It exposes the
widgets as read-only properties and the raw `Username`/`Password` strings. `SetFields(...)` (`:46`)
wires the four widgets and is idempotent (it removes an old click listener before adding a new one).
`Submit()` (`:77`) raises the `LoginSubmitted(username, password)` event (`:32`). `SetStatus` (`:63`)
updates the status line; `SetBusy` (`:70`) toggles whether the button is interactable.

### The bootstrap

`MainMenuBootstrap` (`Assets/Scripts/Bootstrap/MainMenuBootstrap.cs:23`) is the only component the
scene actually needs. On `Start` (`:62`) it:

1. finds the scene `NetworkManager` if it was not set,
2. defaults the gateway to a real `NakamaApiGateway`,
3. calls `EnsureWired` (`:77`), which creates the view if missing (via `MainMenuFactory.Create`),
   constructs a `LoginConnector`, subscribes to `LoginSubmitted` and `StatusChanged`, and — crucially
   — registers the `JoinResultBroadcast` handler **before** the connection starts, so a rejection is
   never missed.

`HandleLoginAsync` (`:102`) sets the button busy, builds a fallback `LastZoneInfo` from the
configured default zone, awaits the connector, and always clears busy afterwards.

### The connector (the actual login logic)

`LoginConnector` (`Assets/Scripts/Bootstrap/LoginConnector.cs:26`) is a plain C# class (not a
MonoBehaviour) so it is testable. `LoginAndConnectAsync(username, password, fallbackZone)` (`:57`)
does the end-to-end flow:

1. Refuse if already busy; set `IsBusy`.
2. Validate the input with `AuthRules.Validate` — fail fast on a bad username/password.
3. `_gateway.LoginAsync(username, password)` — authenticate against the backend.
4. If the result has a valid last zone, use it; otherwise use the fallback. `ApplyZone` (`:140`)
   points the transport at the address/port.
5. If a `JoinRequestClient` exists, hand it the session token and player name.
6. `ClientManager.StartConnection()`.
7. `WaitForAdmissionAsync` (`:224`) — wait until FishNet reports the client **authenticated**.
   Important detail: FishNet's "Started" state fires before the join gate admits you, so admission is
   signalled by `OnAuthenticated`, not by "started".
8. If that attempt failed *and* the zone tried was a stored one that differs from the menu default,
   retry once against the default (`ShouldRetryFallbackZone`, `:178`). A stored zone can outlive
   the server it names — orchestrated raids are ephemeral containers — so the stale record heals
   itself instead of blocking the login.
9. `RecordZoneAsync` (`:185`) — write the `last_zone` record now that the connection is admitted;
   whichever zone actually admitted (stored or fallback) is what gets recorded.
10. Return success.

The admission timeout is `AdmissionTimeoutMilliseconds = 10000` (`:36`) per attempt.

### Connection buttons

`ConnectionButtons` (`Assets/Scripts/Bootstrap/ConnectionButtons.cs:8`) is a small development helper
with Host / Client / Stop buttons. `OnStopClicked` flushes saves (`PlayerPersistenceService.FlushAsync`)
**before** stopping the server, so a manual stop never loses progress.

## 3. The HUD

The HUD is a set of independent views under a coordinating component, all created by a factory.

### NetworkHud (the coordinator)

`NetworkHud` (`Assets/Scripts/UI/NetworkHud.cs:19`) holds references to the player HUD, target frame,
inventory grid and action bar. `SetViews(...)` (`:41`) wires them. The useful methods are:

- `BindPlayer(vitals, inventory = null, abilities = null)` (`:58`) — binds the player-facing views to
  the live sources. Any argument may be null; that screen just stays quiet.
- `BindTarget(target)` (`:74`) — binds the target frame.
- `Unbind()` (`:81`) — disconnects everything.

### The views

| View | File | Binds to | Renders |
|---|---|---|---|
| `PlayerHudView` | `Assets/Scripts/UI/PlayerHudView.cs:17` | `IVitalsSource` | Health and mana bars. |
| `VitalBarView` | `Assets/Scripts/UI/VitalBarView.cs:27` | `IVitalsSource` | One bar (health or mana). `VitalKind` (`VitalKind.cs:7`) selects which. |
| `TargetFrameView` | `Assets/Scripts/UI/TargetFrameView.cs:25` | `ITargetSource` | The target's name and health. |
| `InventoryGridView` | `Assets/Scripts/UI/InventoryGridView.cs:28` | `IInventorySource` | The 8×6 bag grid. |
| `ActionBarView` | `Assets/Scripts/UI/ActionBarView.cs:31` | `IAbilityBarSource` | Up to 6 ability slots. |
| `CraftingPanelView` | `Assets/Scripts/UI/CraftingPanelView.cs:7` | `ICraftingSource` | **Stub** — see below. |

How the views work:

- `VitalBarView.Repaint` (`VitalBarView.cs:96`) reads the current/max pair from the source and sets
  `fill.fillAmount = ComputeFillAmount(current, max)` (`:117`) and a `"current/max"` label.
- `ActionBarView` builds one `ActionBarSlotView` per slot on `Bind` (`:97`). A left-click calls
  `HandleSlotClicked` (`:145`), which guards the source and slot, then calls
  `_source.RequestCast(slot, castTarget())`. The cast target is supplied by `SetCastTarget` (`:90`)
  (the game wires it to the mouse position), defaulting to a point in front. Cooldowns are not shown
  because the interface deliberately does not expose them (see [05](05-gameplay-stats-combat-abilities.md)).
- `InventoryGridView` builds one cell per grid position on `Bind` (`:96`) using
  `source.GridWidth`/`GridHeight`. `Repaint` (`:126`) tints every cell an item covers and labels only
  the item's origin cell, so a 2×1 item reads as one item spanning two cells.
- `TargetFrameView.Repaint` (`:85`) shows or hides the whole frame depending on whether there is a
  target.

`CraftingPanelView` (`CraftingPanelView.cs:7`) is **incomplete scaffolding**: it has a `Bind`/`Unbind`
and a `StationOpened` handler, but `Repaint` (`:41`) has an empty body and it has no
`AddComponentMenu`. Treat it as a placeholder to be finished.

### The HUD factory

`NetworkHudFactory` (`Assets/Scripts/UI/NetworkHudFactory.cs:30`) builds the whole HUD in one call:
`Create(vitals, inventory, abilities, target, createEventSystem)` (`:63`). It creates the player HUD
(with health and mana bars), the target frame, the inventory grid (sized from
`PlayerInventory.GridWidth`/`GridHeight`) and the action bar (sized from `AbilityLoadout.MaxSlots`),
then adds a `NetworkHud` and binds them.

`EnsureEventSystem` (`:103`) returns the existing `EventSystem` or creates one. It prefers the new
Input System's UI module, discovering it by reflection (`FindInputSystemModule`, `:296`) and falling
back to the old `StandaloneInputModule`.

## 4. Why interfaces matter here

The HUD never references `PlayerStats`, `PlayerInventory` or `PlayerAbilities` directly. It binds to
the small interfaces:

| Interface | File |
|---|---|
| `IVitalsSource` | `Assets/Scripts/Gameplay/IVitalsSource.cs:17` |
| `ITargetSource` | `Assets/Scripts/Gameplay/ITargetSource.cs:16` |
| `IInventorySource` | `Assets/Scripts/Inventory/IInventorySource.cs:17` |
| `IAbilityBarSource` | `Assets/Scripts/Gameplay/IAbilityBarSource.cs:19` |
| `ICraftingSource` | `Assets/Scripts/UI/ICraftingSource.cs:6` |

This is what makes the UI testable without a network. A test can write a tiny in-memory
`IVitalsSource`, bind a `VitalBarView`, raise `VitalsChanged`, and assert the fill amount changed.
The `SetVisuals` / `SetBars` / `SetContainer` methods on the views exist specifically so a test (or
the factory) can wire them.

## 5. The Main Menu end to end

```
Play MainMenu scene
  MainMenuBootstrap.Start
    → MainMenuFactory.Create() builds LoginView
    → new LoginConnector(networkManager, NakamaApiGateway)
    → registers JoinResultBroadcast handler
  player types credentials, presses Sign In
  LoginView.Submit → LoginSubmitted → MainMenuBootstrap.HandleLoginAsync
    → LoginConnector.LoginAndConnectAsync
         AuthRules.Validate
         gateway.LoginAsync            (Nakama email auth, token, last zone)
         ApplyZone                     (point transport at the zone)
         JoinRequestClient.SetAuthToken
         ClientManager.StartConnection
         wait for OnAuthenticated      (the gate admitted us)
         ↳ stored zone failed? retry the menu default once
         gateway.SaveLastZoneAsync     (record last_zone)
    → status line updates; on success the player spawns in the zone
```

## Common tasks

- **Add a field to the login screen.** Add the widget in `MainMenuFactory`, expose it on `LoginView`,
  and read it in `LoginConnector`.
- **Build a new HUD element.** Create a view component with a `Bind(source)` method and a `Repaint`
  that reads from an interface, add it to `NetworkHud` (`SetViews`, `BindPlayer`), and construct it
  in `NetworkHudFactory.Create`.
- **Show a new piece of replicated state.** Add the property/event to the relevant source interface
  (e.g. `IVitalsSource`), implement it on the NetworkBehaviour, and read it in the view's `Repaint`.
- **Finish the crafting panel.** Implement `CraftingPanelView.Repaint` and wire it in
  `NetworkHudFactory`. It should read from `ICraftingSource` and list `RecipeRegistry` entries.
- **Change the UI colours or scale.** Edit the `Color`/reference-resolution constants at the top of
  `MainMenuFactory` or `NetworkHudFactory`.
