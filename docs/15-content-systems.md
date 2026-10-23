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
