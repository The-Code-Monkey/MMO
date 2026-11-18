# 12 — Conventions and checklist

This chapter collects the rules the project expects every change to follow. It is the practical
companion to `agents.md`: that file states the philosophy, this one states the mechanics.

## 1. Server authority first

The server holds the truth. Clients submit **intents**; the server validates and executes. Apply this
without exception:

- Never trust client state for position, health, mana, inventory, abilities, XP or social
  membership.
- Every networked mutator is `[Server]`.
- Every client request is a `[ServerRpc]` that takes a `NetworkConnection sender` and re-checks
  ownership:

```csharp
[ServerRpc]
private void RpcDoThing(SomeArg arg, NetworkConnection sender = null)
{
    if (sender == null || !OwnerMatches(sender))
        return;
    // ... now do the thing
}
```

This double check is deliberate even though FishNet also enforces ownership — see
`PlayerStats.RpcRequestDamage` (`Assets/Scripts/Gameplay/PlayerStats.cs:230`) and
`PlayerAbilities.RpcRequestCast` (`Assets/Scripts/Gameplay/PlayerAbilities.cs:166`) for the pattern.

Three anti-cheat rules fall out of this; they are documented in full in
[14](14-server-authority-hardening.md):

- **Movement takes input, never a position.** A client's intent is a direction, and the server
  integrates it at its own speed (`PlayerMover`). Any position-bearing message is judged by
  `MovementRules` and snapped back if it is implausible.
- **Line of sight is a server raycast.** An attack or ability lands only on a target the server can
  actually reach (`SightRules` + `IServerSight`), so nothing hits through a wall.
- **Item identity is server-minted.** Every item carries an `InstanceId` the server assigns, and
  `ItemOwnershipRegistry` is the single writer for who holds each one.

## 2. The pure-core / networked-shell split

- Put decision logic in pure classes/static methods that take every input (including `now`) as an
  argument, read no clock, and touch no Unity objects. Examples: `AbilityCastRules`,
  `InventoryGrid`, `InventoryValidator`, `CraftingValidator`, `GatheringRules`, `JoinPolicy`,
  `PartyRules`, `GuildRules`, `MovementRules`, `SightRules`, `ToolRules`.
- Put the clock, world and network in a `NetworkBehaviour` shell that calls the pure core.

This is what keeps the test suite fast and offline. If you find yourself writing `Time.time` inside a
rules method, stop and pass `now` in instead.

## 3. FishNet specifics

- **SyncVars use the object form**, not the attribute:
  `private readonly SyncVar<int> _health = new();`. The `[SyncVar]` attribute is obsolete in FishNet
  4.x and replicates nothing.
- **SyncLists** are used for ordered collections (`SyncList<ItemInstance>` for the bag/equipment).
- **Wire payload types use public fields** (`ItemInstance`, `PlayerSaveRecord`, the broadcast
  structs), because FishNet's serialiser works from public fields.
- **Data assets use private `[SerializeField]` fields** with read-only properties
  (`AbilityData`, `ItemDefinition`, `RecipeData`, `WeaponData`, `ArmorData`) so the Inspector is the
  only writer.
- **Replication drives the UI through events**, never polling. Views subscribe to `VitalsChanged`,
  `SlotsChanged`, `ContainersChanged`, `StateChanged`, etc. No view defines `Update`.
- **Gameplay keys are read through `ClientInput`**, not checked directly, so a rebind reaches every
  consumer — `WasPressed` for edges, `IsHeld` for continuous input. Local preference files (settings
  and bindings) fall back to defaults on a missing, corrupt or unwritable file and never stop the
  client. The whole scheme, including the camera and pointer, is in
  [18](18-player-controls.md).

## 4. Data is data

New abilities, items, recipes, weapons and armor are **assets**, not code. Prefer:

- `Assets > Create > MMO > Ability Data`
- `Assets > Create > MMO > Item Definition`
- `Assets > Create > MMO > Recipe Data`
- `Assets > Create > MMO > Resource Definition`
- `Assets > Create > MMO > Weapon Data` / `Armor Data`
- `Assets > Create > MMO > Quest Data`
- `Assets > Create > MMO > Dialogue Node`
- `Assets > Create > MMO > Weighted Loot Table`
- `Assets > Create > MMO > Aura Data`

Items and recipes self-register via `OnEnable`, so dropping a named asset in the right `Resources`
folder is enough to make it resolvable. Quests and loot tables are loaded from
`Resources/Content/Quests` and `Resources/Content/LootTables` by their registries.

Content conventions:

- Loot entries are **independent chances** (`0..1`), not normalized weights; 0.5 means 50%.
- Quest objective keys are stable strings: `NPCStats.TargetName` for kills,
  `ItemDefinition.ItemName` for collects, and the node/resource/station/chest key for interacts.
- Auras are session-only; they are not saved.
- Enemy camps are authored on `EnemyMarker` and run through `SpawnController`; a production manager
  does not carry a fixed target-dummy spawner.

## 5. Testing rules (TDD)

From `agents.md` and the way the suite is built:

1. Write the test **first**, before the implementation. Match the test mode to the code: EditMode for
   pure logic and payloads, PlayMode for anything over a socket.
2. Keep 90%+ coverage of core game logic and network payloads.
3. Test data structures, serialisation and server validation with mock state **before** testing full
   FishNet RPC flows.
4. Run the whole suite before you consider the change done.

See [10](10-testing.md) for the fakes (`FakeApiGateway`, `InMemoryPlayerRepository`,
`TestItemDefinitions`, `TestResourceDefinitions`) and the `*TestAsset` cleanup pattern for
assets/prefabs.

## 6. Persistence and backend rules

- Gameplay code never talks to Nakama directly. Use `IPlayerRepository` (server) or `IApiGateway`
  (client), with an implementation plus a fake in tests.
- Keep the fail-open behaviour: a dead backend must not stop a join or crash the server. The one
  exception is token validation, which fails **closed**.
- Writes that can race carry the `StorageVersion` and retry on a version conflict. Do not add a
  blind overwrite.

## 7. Editor tooling rules

- Bakers must be **idempotent**: re-running one reuses or overwrites, never duplicates.
- Build scene objects through the runtime factories so the scene matches tests exactly.
- Clean up scene copies in a `finally` after `SaveAsPrefabAsset`.

## 8. Code style

- Match the surrounding style. The codebase is generously commented at the class/why level; add a
  comment when *why* is not obvious, not to restate *what* the next line does.
- Prefer small pure helpers with descriptive names over long methods.
- Keep validation order stable and documented — several tests depend on which rejection is reported.
- New public types get a short XML summary like their neighbours.

## 9. Pull-request checklist

Before opening a PR:

- [ ] The change preserves server authority (no client writes authoritative state).
- [ ] Movement, line of sight and item identity are still server-owned (see
      [14](14-server-authority-hardening.md)).
- [ ] State a client could exploit is guarded structurally, not merely validated once (e.g. shared
      state goes through a single-writer; a second item grant is refused).
- [ ] Tests were written first and pass; core logic is well covered.
- [ ] New pure logic lives in a pure class taking `now`/inputs as arguments.
- [ ] New data uses ScriptableObject assets where possible; content keys and loot chances follow
      the conventions above.
- [ ] New wire types use public fields; new SyncVars use the object form.
- [ ] The backend is reached only through `IPlayerRepository` / `IApiGateway`.
- [ ] Any changed system's doc chapter in `docs/` is updated in the same PR.
- [ ] Every `File.cs:line` reference added or changed still resolves.
- [ ] No secrets, keys or tokens are committed.
- [ ] The full test suite passes locally.

## 10. Documentation rules

- Keep the docs and `agents.md` aligned. If you change a system, change its chapter.
- Prefer plain English and explain a term the first time it appears.
- Use text diagrams, not images, so they diff and render on GitHub.
- Every system chapter ends with a **Common tasks** section.
- Broken `File.cs:line` anchors are treated as bugs.

## Common tasks

- **Reviewing a change.** Walk the checklist above; the first three items catch most problems.
- **Unsure whether a change is allowed.** If it lets a client set authoritative state instead of
  asking the server, it is not.
- **Updating `agents.md`.** Only update it when the philosophy changes; day-to-day mechanics belong
  in these docs. Keep the two cross-linked.
