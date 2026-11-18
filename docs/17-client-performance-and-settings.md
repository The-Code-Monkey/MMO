# 17 — Client performance, audio, input and settings

This chapter covers the four client-side facilities added for large fights: pooled combat
presentation, spatial audio with a voice cap, rebindable input, and the local settings that drive
them. All four follow the project rule from [12](12-conventions-and-checklist.md): the decision is a
pure, tested core; the Unity object is a thin shell.

## 1. Object pooling

Combat is resolved on the server as an instant effect (`PlayerAbilities.ApplyEffect`). There is no
networked projectile entity, so pooling covers **local presentation**, not authority.

| Type | File | Role |
|---|---|---|
| `PoolBudget` | `Assets/Scripts/Client/Effects/PoolBudget.cs` | Pure prewarm rule: expected concurrency × headroom, clamped to a maximum. |
| `VfxPool` | `Assets/Scripts/Client/Effects/VfxPool.cs` | Fixed pool built in `Configure`; `Rent` returns null when exhausted instead of allocating. |
| `PooledVfx` | `Assets/Scripts/Client/Effects/PooledVfx.cs` | Counts down and returns itself to the pool. |
| `VfxTemplateFactory` | `Assets/Scripts/Client/Effects/VfxTemplateFactory.cs` | Code-built placeholder burst; no prefab or scene asset. |
| `ClientEffects` | `Assets/Scripts/Client/Effects/ClientEffects.cs` | Owner-only endpoint: plays a burst and the matching impact sound. |

`PlayerAbilities` broadcasts an `[ObserversRpc]` after a cast resolves. It carries only the ability
name and effect position; the server has already applied damage. A client skips ally effects when
`SettingsModel.ShowAllySpellEffects` is false.

When the pool is empty, the effect is dropped. That is deliberate: presentation degrades under a
burst, but a fight never triggers an allocation storm.

## 2. Spatial audio

| Type | File | Role |
|---|---|---|
| `AudioVoiceBudget` | `Assets/Scripts/Client/Audio/AudioVoiceBudget.cs` | Pure selection: priority, then nearest, then loudest, capped at N voices. |
| `MixerVolume` | `Assets/Scripts/Client/Audio/MixerVolume.cs` | Linear 0–1 to decibels. |
| `PlaceholderAudioClips` | `Assets/Scripts/Client/Audio/PlaceholderAudioClips.cs` | Procedural impact/spell stand-ins until #77. |
| `AudioService` | `Assets/Scripts/Client/Audio/AudioService.cs` | Fixed 3D `AudioSource` pool, voice stealing, mixer bus volumes. |
| `MMOAudioMixer` | `Assets/Resources/Audio/MMOAudioMixer.mixer` | Master/Music/SFX/Voice groups. |

The mixer exposes `MasterVolume`, `MusicVolume`, `SfxVolume` and `VoiceVolume`. Settings apply the
first three; the voice bus is reserved for future dialogue/VO. `AudioService` loads the mixer from
`Resources/Audio/MMOAudioMixer`, so no scene reference is needed.

Every source has `spatialBlend = 1`; direction and distance come from the world position passed to
`PlayImpact`. Voices past the hard cap are stopped rather than stacked.

## 3. Input rebinding

The action map is built in code, not committed as an `.inputactions` asset, so a fresh process and a
test get identical defaults.

| Type | File | Role |
|---|---|---|
| `GameAction` / `GameActions` | `Assets/Scripts/Client/Input/GameAction.cs` | Rebindable actions and display names. |
| `GameInputActions` | `Assets/Scripts/Client/Input/GameInputActions.cs` | Builds the `Gameplay` map with default keys. |
| `InputBindingRules` | `Assets/Scripts/Client/Input/InputBindingRules.cs` | Pure conflict and reserved-key rules. |
| `InputBindingStore` | `Assets/Scripts/Client/Input/InputBindingStore.cs` | Saves Input System overrides as JSON. |
| `InputRebindService` | `Assets/Scripts/Client/Input/InputRebindService.cs` | Runs one interactive rebind, applies the plan, saves. |
| `ClientInput` | `Assets/Scripts/Client/Input/ClientInput.cs` | Owner-side façade: `WasPressed`, settings, rebind entry point. |

Default bindings: `W`/`S`/`Q`/`E` movement, `A`/`D` turning, mouse buttons for orbiting and
facing the camera, `Space` jump, `1`–`6` abilities, `G` mount, `E` interact, `T` trade, `B` auction,
`M` mailbox, `F10` settings — see [18](18-player-controls.md) for what each one does. Gameplay code
asks `ClientInput.Current.WasPressed(GameAction.X)` for an edge and
`ClientInput.Current.IsHeld(GameAction.X)` for a held key; the old direct key checks remain only as
a fallback when no `ClientInput` exists (tests and tools).

One control belongs to one action. Rebinding a key that another action owns erases the other
action's binding. `Escape` and `F1` are reserved. While a rebind is running, or the settings panel
is open, gameplay actions are blocked; only the settings key still reads through so the panel can
be closed.

Overrides live at `Application.persistentDataPath/input-bindings.json`. A missing or corrupt file
keeps the code defaults and never stops the client. The Input System matches an override by binding
id, so `GameInputActions` derives each id from the map, action and default path: a generated id
would differ on every launch and silently drop the rebinds a player saved.

## 4. Settings

| Type | File | Role |
|---|---|---|
| `SettingsModel` | `Assets/Scripts/Client/Settings/SettingsModel.cs` | Quality, volumes, ally-effect toggle, interpolation delay. |
| `SettingsRules` | `Assets/Scripts/Client/Settings/SettingsRules.cs` | Pure clamps and millisecond-to-tick conversion. |
| `SettingsStore` | `Assets/Scripts/Client/Settings/SettingsStore.cs` | JSON at `Application.persistentDataPath/settings.json`; failures fall back to defaults. |
| `SettingsApplier` | `Assets/Scripts/Client/Settings/SettingsApplier.cs` | Applies quality and `NetworkTransform.SetInterpolation`. |
| `SettingsPanelView` / `SettingsPanelFactory` | `Assets/Scripts/UI/SettingsPanelView.cs`, `SettingsPanelFactory.cs` | The code-built settings window. |

Every change saves and applies immediately. Lowering quality or hiding ally effects mid-fight does
not need an Apply button. Interpolation is clamped to 50–250 ms and converted at the network tick
rate; a client cannot push an unsafe value. Audio volumes are applied through `AudioService` when
`ClientInput` saves.

## Common tasks

- **Tune the VFX pool:** change `ClientEffects.DefaultExpectedConcurrent`; `PoolBudget` adds 50%
  headroom and caps at 256.
- **Add a real effect:** replace `VfxTemplateFactory.Create` (or make it choose by ability name).
  The pool and RPC do not change.
- **Add a sound:** add a cached clip in `PlaceholderAudioClips` (or a real bank later) and call
  `AudioService.Play` from `ClientEffects.Play`.
- **Add a rebindable action:** add a `GameAction`, add it in `GameInputActions.Create`, and read it
  through `ClientInput.WasPressed`. The settings panel builds its row from the enum automatically.
- **Change a default key:** edit the path in `GameInputActions.Create`. Existing players keep their
  saved override until they rebind or delete the JSON file.
- **Change a settings default:** edit `SettingsModel`; `SettingsRules.Sanitize` protects old files.

## Limits

- The VFX and audio are placeholders until the art pass (#77).
- Pooling and audio are presentation only; server authority is unchanged.
- The repository currently has no PlayMode suite, so RPC, audio-device and rebind flows are
  compiled but not executed by automated tests.
