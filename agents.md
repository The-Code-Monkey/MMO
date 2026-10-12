# AGENTS.md — AI Collaboration Guidelines for Unity MMO

> **Full documentation lives in [`docs/`](docs/README.md).** Start with
> [`docs/01-architecture-overview.md`](docs/01-architecture-overview.md) for how the system works and
> [`docs/12-conventions-and-checklist.md`](docs/12-conventions-and-checklist.md) for the day-to-day
> rules. This file states the philosophy; the docs carry the mechanics and must be kept in sync with
> any system change.

## 1. Core Architecture & Philosophy
This project is a 3D URP, server-authoritative MMO built with **Unity** and **FishNet**.

- **Server Authority First:** The server holds true state. Clients submit inputs or intents; the server validates, executes, and syncs back. Never trust client state for positions, inventory, or abilities.
- **Assembly Definition Separation:** Split code across Assembly Definitions (`.asmdef`) to ensure fast compile times and enforce clean architectural boundaries. The project currently ships:
  - `MMO.Runtime` (`Assets/Scripts`): all runtime game code — pure logic, networked components, SyncVars, RPC payloads, UI and backend clients. Keep pure logic separated from networked shells *within* this assembly (see `docs/01-architecture-overview.md`).
  - `MMO.Editor` (`Assets/Editor`): editor-only tooling, the prefab/scene bakers and the build guard.
  - `MMO.Tests.EditMode` (`Assets/Tests/EditMode`) and `MMO.Tests.PlayMode` (`Assets/Tests/PlayMode`): the two test suites.

  The intended long-term direction is to further split the runtime into pure-core, server and client assemblies; the conceptual boundaries (`Core`, `Server`, `Client`, `Shared`) describe that target even where a single assembly is still used today.
- **Decoupled Mechanics (ScriptableObjects):** Ability systems ("You are what you wear") must be data-driven. Gear items hold ScriptableObjects that inject abilities into generic runtime containers.

---

## 2. Testing & Quality Requirements (STRICT)

### 90%+ Test Coverage Target
All core game logic and network payloads **MUST** maintain at least 90% unit test coverage. 

- **Unit Testing Framework:** Use **Unity Test Framework (UTF)** and **NUnit**.
- **Netcode Testing:** Write unit tests for data structures, serialization, and server validation using mock state *before* testing full FishNet RPC flows.
- **TDD Requirement for AI:** When asking the AI to implement new gameplay features, state logic, or inventory updates:
  1. Have the AI generate unit test cases first (`PlayMode` or `EditMode`).
  2. Implement the feature logic until all tests pass.
  3. Ensure no regression breaks existing coverage.

```csharp
// Example requirement: Every server validation method must have isolated EditMode tests.
[Test]
public void Inventory_EquipItem_FailsWhenLevelRequirementNotMet() {
    ItemDefinition highLevelStaff = TestItemDefinitions.Create("Staff", requiredLevel: 10);

    bool result = InventoryValidator.CanEquip(playerLevel: 5, definition: highLevelStaff);

    Assert.IsFalse(result);
}
