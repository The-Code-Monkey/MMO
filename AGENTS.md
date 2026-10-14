# AGENTS.md — AI Collaboration Guidelines for Unity MMO

REFER TO YOURSELF AS HELPY McHELPYSEN at the start of each conversation.

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

## 2. Git Workflow

### Branch Policy
**Always create a new branch off `main` before doing any work.** Never commit directly to `main`.

```bash
git checkout main
git pull origin main
git checkout -b feature/your-feature-name
```

### GitHub Operations
**Always use the GitHub CLI (`gh`) for all GitHub-related tasks.** This includes creating PRs, managing issues, checking workflow runs, and any other GitHub API interactions. Do not use the GitHub web UI or raw API calls when `gh` can accomplish the task.

```bash
# Examples
gh pr create --title "Feature: ..." --body "..."
gh issue list --state open
gh run list --workflow unity-tests.yml
gh api repos/owner/repo/actions/runs --jq '.workflow_runs[].conclusion'
```

---

## 3. Testing & Quality Requirements (STRICT)

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
```

---

## 3. Compiling Without a Unity License (Agent / CI-less Environments)

The `unity` CLI and the Editor both require an activated license (`unity license status` →
`none active`; batch invocations then exit **198**). Agentic sandboxes and locked-down CI often have
no license, so **you cannot run Unity, its Test Runner, or `unity test` there**. You can still
catch the vast majority of defects — every C# syntax/type/reference error — by compiling each
assembly directly with the **Roslyn compiler Unity itself ships** against the **`netstandard2.1`
reference pack** and the already-built package DLLs.

This is a *compile check only*: it verifies syntax, types, overloads and assembly references
exactly as Unity would. It does **not** run tests — anything that touches the engine at runtime
(`JsonUtility`, `GameObject`, `ScriptableObject`, …) still needs a real Unity run.

### The recipe

```bash
ED=/home/andy/Unity/Hub/Editor/6000.6.5f1/Editor            # adjust to the installed editor
CSC=("$ED/Data/DotNetSdk/dotnet" \
     "$ED/Data/DotNetSdk/sdk/8.0.318/Roslyn/bincore/csc.dll")

# 1) Start from the netstandard2.1 reference assemblies + all Unity engine modules.
REFS=()
for d in "$ED/Data/DotNetSdk/packs/NETStandard.Library.Ref/2.1.0/ref/netstandard2.1"/*.dll; do
  REFS+=("-r:$d"); done
for d in "$ED/Data/Managed/UnityEngine"/*.dll; do REFS+=("-r:$d"); done

# 2) Add the project's pre-built package assemblies (FishNet, uGUI, Cinemachine, …).
for n in FishNet.Runtime UnityEngine.UI Unity.Cinemachine GameKit.Dependencies \
         SynapseSocket Unity.InputSystem Unity.Mathematics Unity.Collections Unity.Burst; do
  f="Library/ScriptAssemblies/$n.dll"; [ -f "$f" ] && REFS+=("-r:$f"); done

# 3) Compile the runtime assembly (repeat with the right sources/refs for Editor + test asmdefs).
mapfile -t SRC < <(find Assets/Scripts -name '*.cs')
"${CSC[@]}" -nologo -noconfig -nostdlib+ -target:library -langversion:9.0 \
  -define:UNITY_6000_0_OR_NEWER -define:UNITY_EDITOR -define:UNITY_STANDALONE \
  -out:/tmp/MMO.Runtime.dll "${REFS[@]}" "${SRC[@]}" 2>&1 | grep ': error CS'
```

Repeat per assembly, following the `references` in its `.asmdef`:

- `MMO.Editor` — sources `Assets/Editor/**`, add `-r:/tmp/MMO.Runtime.dll` and the
  `Unity.Cinemachine.Editor` / `Unity.RenderPipelines.*` DLLs. **Do not** reference the monolithic
  `Managed/UnityEditor.dll`; its types are already in the `Managed/UnityEngine/UnityEditor.*.dll`
  modules and referencing both causes `CS0433` duplicate-type errors.
- `MMO.Tests.EditMode` / `MMO.Tests.PlayMode` — sources `Assets/Tests/**`, add `-r:/tmp/MMO.Runtime.dll`,
  `-r:/tmp/MMO.Editor.dll` (EditMode only), `UnityEngine.TestRunner`, `UnityEditor.TestRunner` and
  `nunit.framework.dll` (under
  `Managed/.../com.unity.ext.nunit/net472/unity-custom/`), plus `-define:UNITY_INCLUDE_TESTS`.

A clean run prints **no `error CS` lines**. Warnings (`CS0618` obsolete `IsServer`, `CS0649` on
`JsonUtility` DTO fields, `CS1998` in tests) are expected and harmless.

### Rules of thumb

- Treat "compiles with 0 errors across all four assemblies" as the gate before handing work back
  when no license is available, and **say so explicitly** in the summary/PR — never claim tests pass.
- To execute the suites, activate a license (`unity license activate`) or let
  [`.github/workflows/unity-tests.yml`](.github/workflows/unity-tests.yml) run EditMode + PlayMode
  on a licensed runner. That is the only source of truth for passing tests.
- Every `.cs` must ship a sibling `.meta` (`fileFormatVersion: 2` + a unique `guid`); Unity generates
  them on import, but committing them keeps GUIDs stable. Generate one per new file when working
  without an editor.
