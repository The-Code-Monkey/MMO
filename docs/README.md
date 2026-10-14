# Unity MMO — Developer Documentation

This folder is the full, plain-English guide to how the game works and how to keep building it.
It assumes you can read basic C# but know nothing about this project. Every term is explained the
first time it appears, and every system chapter ends with a **Common tasks** section showing the
small, safe changes you are most likely to make.

If you only have ten minutes, read [01-architecture-overview](01-architecture-overview.md) and
[02-getting-started](02-getting-started.md). Those two get you oriented and running.

## Reading order

| # | Chapter | What it answers |
|---|---|---|
| 01 | [Architecture overview](01-architecture-overview.md) | Who is in charge (the server), how code is laid out, how one player input travels through the system. |
| 02 | [Getting started](02-getting-started.md) | Which Unity version, how to boot the backend, how to open the project and press Play. |
| 03 | [Backend and persistence](03-backend-and-persistence.md) | Nakama + PostgreSQL, the save record, `IPlayerRepository`, the save cadence, "fail open". |
| 04 | [Networking and the join flow](04-networking-and-the-join-flow.md) | FishNet basics, the connection gate, the deferred verdict, token auth, zone transfers. |
| 05 | [Gameplay: stats, combat, abilities](05-gameplay-stats-combat-abilities.md) | Health/mana, damage maths, the ability pipeline, data-driven ScriptableObject skills. |
| 06 | [Inventory and equipment](06-inventory-and-equipment.md) | The grid bag, equipment slots, `SyncList<ItemInstance>`, gear stat bonuses, crafting glue. |
| 07 | [Crafting and gathering](07-crafting-and-gathering.md) | Recipes, stations, the crafting XP curve, resource nodes and gathering ticks. |
| 08 | [UI: main menu and HUD](08-ui-main-menu-and-hud.md) | The code-built UI pattern, the login screen, the HUD, why there are no UI prefabs. |
| 09 | [Editor tooling and scenes](09-editor-tooling-and-scenes.md) | Every `Tools > MMO > ...` baker, what it writes, and the build guard. |
| 10 | [Testing](10-testing.md) | EditMode vs PlayMode, the fakes, how to run the suite, and the coverage rules. |
| 11 | [Extending the game](11-extending-the-game.md) | Task-style how-tos: add an ability, an item, a recipe, a zone, a UI screen. |
| 12 | [Conventions and checklist](12-conventions-and-checklist.md) | Server-authority rules, TDD flow, and the pull-request checklist. |
| 13 | [Social: party and guild](13-social-party-and-guild.md) | Cross-zone parties and guilds, the CAS retry loop, presence heartbeats. |
| 14 | [Server authority hardening](14-server-authority-hardening.md) | Anti-cheat: server-authoritative movement, line-of-sight raycasts, and the item-dupe guard. |

## The one idea to remember

This is a **server-authoritative** game. The server holds the truth for position, health, mana,
inventory, abilities and social state. A client can only *ask* to do something; the server decides
whether that is allowed and then copies the result back. Nothing in the docs below should be read as
"the client does X" — the client *requests* X and the server decides.

## Conventions used in this documentation

- Source references are written as `Assets/Scripts/.../File.cs:123`. The number is the line on which
  the described code starts, so you can jump straight to it.
- Text diagrams are used instead of images so they live in the repository, render on GitHub, and
  show up clearly in a diff.
- "SyncVar" means a variable that FishNet (the networking library) copies from the server to every
  client automatically. The server is always the only writer.
- Where the code and an older comment disagree, the code wins; the docs describe what actually runs.

## Keeping these docs honest

The documentation and the `agents.md` rules are meant to stay in step. If you change a system,
change its chapter in the same pull request. If you find a `File.cs:line` reference that no longer
points at the right thing, fix it — broken anchors are treated as bugs. See
[12-conventions-and-checklist](12-conventions-and-checklist.md) for the full list of habits this
project expects.
