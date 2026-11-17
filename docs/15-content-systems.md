# 15 — Content systems: quests and dialogue

This chapter covers the content layer that reads authored lore: quests, NPC dialogue, and the
player-side shells that track and render them. Loot tables and auras have their own mechanics
documented in [06](06-inventory-and-equipment.md) and
[05](05-gameplay-stats-combat-abilities.md); weighted tables and aura data both live under
`Assets/Scripts/Content`.

## 1. Quests

`QuestData` (`Assets/Scripts/Content/QuestData.cs`) is the authored asset:

| Field | Meaning |
|---|---|
| `QuestId` | Stable save/registry key. |
| `Title` | Display name. |
| `Objectives` | `Kill`, `Collect` or `Interact`, each with a target name and required count. |
| `PrerequisiteQuestIds` | Quests that must be completed before this one is offered. |
| `RewardXp` | Experience granted on turn-in/completion. |
| `RewardTable` | Optional `WeightedLootTable` awarded on completion. |

`QuestRules` (`Assets/Scripts/Content/QuestRules.cs`) is pure: `IsAvailable`, `Advance`,
`IsObjectiveComplete` and `IsComplete`. It never reads the clock or a Unity object.

### Objective keys

The target name is the contract between a quest and the world:

| Kind | Key |
|---|---|
| `Kill` | `NPCStats.TargetName` — the enemy's GameObject name. |
| `Collect` | `ItemDefinition.ItemName`. The count is the **current bag count**, not a historical total. |
| `Interact` | `DialogueNode.NodeId`, `ResourceDefinition.name`, `CraftingStationType.ToString()` or a `LootChest`'s GameObject name. |

There is deliberately no `IQuestTarget` abstraction yet. Content authors use the same stable keys
the rest of the game already uses.

### PlayerQuestLog

`PlayerQuestLog` (`Assets/Scripts/Content/PlayerQuestLog.cs:16`) is the server-owned shell on the
player prefab:

- `TryStartQuest(QuestData)` (`:91`) checks prerequisites and adds one replicated
  `QuestProgressEntry` per objective;
- `NotifyKill` (`:160`) and `NotifyInteract` (`:168`) advance matching objectives;
- `RefreshCollectObjectives` (`:181`) reads the live bag through `PlayerInventory.CountItem` and is
  hooked to `ContainersChanged`;
- `TryComplete` (`:126`) marks the quest completed first, then grants XP and the reward table. The
  completed set makes a repeated kill/turn-in idempotent;
- `Restore` (`:231`) replaces the live state from a save.

Progress and completion are `SyncList`s, so a client can render them. A reward loot table uses the
same `LootAwarder` path as mob loot, including the overflow bag.

`NpcLoot` credits kills through `IQuestKillSink`, which `PlayerQuestLog` implements. That is why a
damage-over-time poison kill still credits the player who applied the aura.

`PlayerStats.Experience` (`Assets/Scripts/Gameplay/PlayerStats.cs:55`) stores quest XP.
`AddExperience` (`:185`) is server-only. There is no player level curve in this system yet.

## 2. Dialogue

`DialogueNode` (`Assets/Scripts/Content/DialogueNode.cs`) is a node asset: node id, speaker, text
and choices. A choice has text, an optional `Next` node, and an optional `OfferQuest`/`TurnInQuest`
action with a `QuestData` reference.

`DialogueRules` (`Assets/Scripts/Content/DialogueRules.cs`) is pure: `ChoiceCount`, `CanChoose`,
`TryChoose` and `Next`. The server validates the client's index through these.

### The session

`PlayerDialogue` (`Assets/Scripts/Content/PlayerDialogue.cs:16`) is the per-player shell:

1. `RequestTalk` (`:30`) sends the NPC `NetworkObject` to the server.
2. The server checks ownership, the NPC's `InteractRange` and that the NPC has a root node.
3. `RpcShowNode` (`:144`) sends a `DialoguePayload` containing only speaker, text and choice labels.
4. `RequestChoice` (`:54`) sends an index. The server looks it up on its own `_node`, applies the
   quest action, and either advances or closes.

The client never sends a node id, so it cannot jump into a conversation branch it was not offered.
`DialoguePayload` has public fields because FishNet serialises public fields.

### The NPC

`DialogueNpc` (`Assets/Scripts/Content/DialogueNpc.cs:12`) is a `NetworkBehaviour` with a serialized
root `DialogueNode` and `InteractRange`. It needs a collider for Unity's `OnMouseDown`; a click asks
the local `PlayerDialogue` to talk and decides nothing itself.

### The reader

`DialogueView` (`Assets/Scripts/UI/DialogueView.cs:14`) is the minimal reader: speaker, text and one
button per server-sent choice label. `NetworkHudFactory.Create` builds it concealed, and
`LocalPlayerHud` binds it to that player's `PlayerDialogue`. It has no node ids, no quest buttons
and no `Update`; it repaints only from `NodeShown`/`Closed`.

There is intentionally no quest-log UI in this issue.

## 2a. Achievements

Achievement definitions are assets (`Create > MMO > Achievement Data`) resolved by
`AchievementRegistry` from `Resources/Content/Achievements`, the same self-registering pattern as
quests. Each declares an `AchievementTrigger` (level, gather, craft, raid clear, PvP kill, quest), a
threshold, and a small one-shot reward: gold and/or one item.

`AchievementRules` is the pure core: `Progress` adds a non-negative increment, clamps to
`0..threshold` and reports completion; `ShouldGrant` is true only on the transition, so a duplicate
or out-of-order event cannot double-pay. `AchievementTracker` applies events to an
`AchievementRecord` (counters, completed ids, granted ids) and lists completed-but-unpaid rewards
for retry — a full bag leaves the completion standing and the grant pending rather than deleting it.

The server-only `PlayerAchievements` component subscribes to `PlayerStats.LevelChanged` and
`PlayerQuestLog.QuestCompleted`, and `PlayerGathering`/`PlayerCrafting` call `Record` at their
success points. Raid clears can call it when the encounter system lands. There is no client RPC that
creates an event, so the counter can never be client-supplied. Progress is captured into
`PlayerSaveRecord.Achievements` by `PlayerSnapshotFactory` and restored by `PlayerSaveApplier`, so it
survives a zone handoff with the rest of the save. A completion is pushed to the owner over a
`[TargetRpc]`; removing a definition from content leaves old record entries inert.

## 3. Master spawn controllers

`EnemyMarker` is now a population point, not a one-shot spawner. It carries `MinAlive`, `MaxAlive`,
`PerPlayer`, `RespawnSeconds`, `WanderRadius` and `ActivationRadius`. On server start,
`WorldBootstrap` resolves the marker's enemy prefab and attaches a `SpawnController`
(`Assets/Scripts/Content/SpawnController.cs`) to the marker.

Every tick the controller:

1. counts live `PlayerStats` inside `ActivationRadius`;
2. asks `SpawnRules.TargetAlive` for the target population (zero with no players, otherwise
   `players * PerPlayer` clamped to the authored range);
3. despawns extras first, including corpses, when the target drops;
4. waits out each defeated enemy's `RespawnSeconds`, then despawns the corpse and spawns a fresh
   enemy at the marker (random offset within `WanderRadius`, sampled onto the NavMesh).

Spawning is server-only: the controller checks `NetworkManager.IsServerStarted`, and a client
scene contains no controller at all. There is no fixed `TargetDummySpawner` in a production
`NetworkManager`; the class remains for tests and manual scene wiring.

## 4. Loot and auras

`WeightedLootTable` (`Assets/Scripts/Content/WeightedLootTable.cs`) holds `LootEntry` rows with an
item, a **drop chance** in `0..1`, and an inclusive min/max count. Each entry rolls independently, so
50% leather and 1% epic mount are exactly those probabilities; the table is not a normalized weight
set. `LootRules.Roll` uses an injected `System.Random`, which keeps the roll deterministic in tests.

`NpcLoot` rolls the table on `NPCStats.Died` and awards the killer through `LootAwarder`. A full bag
falls back to a `LootBag`; `LootChest` uses the same award path and opens only once. Details of the
bag and ownership path are in [06](06-inventory-and-equipment.md).

`AuraData`/`AuraRules`/`AuraController` live alongside them. Auras are server-applied, tick-based and
session-only: DoT/HoT, stun/root, temporary health/mana/speed changes, and a replicated snapshot list
for a future buff bar. The gameplay side is in
[05](05-gameplay-stats-combat-abilities.md#5a-auras--buffs-debuffs-and-crowd-control).

## Deferred

- No quest-log UI, no buff-bar UI and no aura persistence.
- No player XP curve or level-up from XP; XP is stored and granted only.
- No generic `IStat` system; auras affect health, mana and movement speed only.
- No chest or dialogue NPC prefab/scene placement; both are reusable components for #75.
- No normalized weighted loot or "roll N from a table"; entries are independent chances.
- `EnemyBrain` still resolves target names by `name`/object id; quest and loot credit use direct
  `PlayerInventory` references, so they do not depend on that lookup.
- No boss loot integration; #83 can consume `WeightedLootTable`.

## Common tasks

- **Add a quest.** Create `Assets > Create > MMO > Quest Data`, give it a unique `QuestId`, add
  objectives using the keys above, and save it under `Assets/Resources/Content/Quests` if it should
  be resolvable by id after a save.
- **Add a conversation.** Create `Assets > Create > MMO > Dialogue Node`, set the speaker/text and
  wire each choice to its next node. Point a `DialogueNpc` prefab at the root.
- **Offer or turn in a quest.** On a choice, set `Action` to `OfferQuest` or `TurnInQuest` and assign
  the `QuestData`.
- **Test a quest rule.** Use `QuestRules` directly; `QuestRulesTests` is the template.
- **Test the shell.** Use `QuestDialoguePlayModeTests` and a code-built `QuestData`/`DialogueNode`;
  the server methods are `[Server]` and run on a host.
- **Tune an enemy camp.** Select its `EnemyMarker` and set the population range, per-player density,
  respawn timer, wander radius and activation radius. The server applies them through
  `SpawnController`; no prefab change is needed.
- **Test a spawn budget.** Use `SpawnRules.TargetAlive` directly; `SpawnControllerPlayModeTests`
  covers the live density, cap and respawn behaviour.
- **Add a loot table.** Create `Assets > Create > MMO > Weighted Loot Table`; remember entries are
  independent chances, not a normalized total. Attach it to `NpcLoot` or `LootChest`.
- **Add an aura.** Create `Assets > Create > MMO > Aura Data`; apply it through `AuraController` or an
  `ApplyAura` ability. Auras are not persisted.
