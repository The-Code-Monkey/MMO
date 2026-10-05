# AGENTS.md — AI Collaboration Guidelines for Unity MMO

## 1. Core Architecture & Philosophy
This project is a 3D URP, server-authoritative MMO built with **Unity** and **FishNet**.

- **Server Authority First:** The server holds true state. Clients submit inputs or intents; the server validates, executes, and syncs back. Never trust client state for positions, inventory, or abilities.
- **Assembly Definition Separation:** Split code across Assembly Definitions (`.asmdef`) to ensure fast compile times and enforce clean architectural boundaries:
  - `Project.Core`: Pure C# data models, math, interfaces, and database DTOs.
  - `Project.Server`: Server-only authoritative logic, validation, database integration.
  - `Project.Client`: Client-side prediction, UI, visual effects, local input mapping.
  - `Project.Shared`: Networked components, FishNet SyncVars, RPC payloads.
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
    var player = MockPlayer(level: 5);
    var highLevelStaff = MockWeapon(requiredLevel: 10);
    
    bool result = InventoryValidator.CanEquip(player, highLevelStaff);
    
    Assert.IsFalse(result);
}
