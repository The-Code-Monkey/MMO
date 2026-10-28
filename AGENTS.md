AGENTS.md — AI Collaboration Guidelines for Unity MMO

First thing you should do at the start of a new session is run both the /caveman skill and the /ponytail skills. THIS IS IMPORTANT EVERY NEW SESSION THIS NEEDS TO BE RUN

> Full documentation lives in docs/. Start with
> docs/01-architecture-overview.md for how the system works and
> docs/12-conventions-and-checklist.md for the day-to-day
> rules. This file states the philosophy; the docs carry the mechanics and must be kept in sync with
> any system change.
> 
1. Core Architecture & Philosophy
This project is a 3D URP, server-authoritative MMO built with Unity and FishNet.
 * Server Authority First: The server holds true state. Clients submit inputs or intents; the server validates, executes, and syncs back. Never trust client state for positions, inventory, or abilities.
 * Assembly Definition Separation: Split code across Assembly Definitions (.asmdef) to ensure fast compile times and enforce clean architectural boundaries. The project currently ships:
   * MMO.Runtime (Assets/Scripts): all runtime game code — pure logic, networked components, SyncVars, RPC payloads, UI and backend clients. Keep pure logic separated from networked shells within this assembly (see docs/01-architecture-overview.md).
   * MMO.Editor (Assets/Editor): editor-only tooling, the prefab/scene bakers and the build guard.
   * MMO.Tests.EditMode (Assets/Tests/EditMode) and MMO.Tests.PlayMode (Assets/Tests/PlayMode): the two test suites.
   The intended long-term direction is to further split the runtime into pure-core, server and client assemblies; the conceptual boundaries (Core, Server, Client, Shared) describe that target even where a single assembly is still used today.
 * Decoupled Mechanics (ScriptableObjects): Ability systems ("You are what you wear") must be data-driven. Gear items hold ScriptableObjects that inject abilities into generic runtime containers.
2. Git Workflow
Branch Policy
Always create a new branch off main before doing any work. Never commit directly to main.
git checkout main
git pull origin main
git checkout -b feature/your-feature-name

GitHub Operations
Always use the GitHub CLI (gh) for all GitHub-related tasks. This includes creating PRs, managing issues, checking workflow runs, and any other GitHub API interactions. Do not use the GitHub web UI or raw API calls when gh can accomplish the task.
# Examples
gh pr create --title "Feature: ..." --body "..."
gh issue list --state open
gh run list --workflow unity-tests.yml
gh api repos/owner/repo/actions/runs --jq '.workflow_runs[].conclusion'

Committing Unity Scene Files (.unity)
Rebaking a scene via Tools > MMO > Build Zones (or any editor operation that re-serializes a
scene) rewrites the whole .unity file. Unity reorders YAML documents, so a one-line logical
change — e.g. flipping _reuseAddress on the Tugboat transport — routinely shows up as a
15k–20k line diff (thousands of -/+ lines that only reorder target:/guid: pairs).
This is expected and correct. Commit the scene files as-is; do not try to minimize, squash, or
hand-prune the diff, and do not assume the file was corrupted or duplicated. Review scene diffs by
spot-checking the specific fields you intended to change, not by reading the whole patch.
3. Testing & Quality Requirements (STRICT)
90%+ Test Coverage Target
All core game logic and network payloads MUST maintain at least 90% unit test coverage.
 * Unit Testing Framework: Use Unity Test Framework (UTF) and NUnit.
 * Netcode Testing: Write unit tests for data structures, serialization, and server validation using mock state before testing full FishNet RPC flows.
 * TDD Requirement for AI: When asking the AI to implement new gameplay features, state logic, or inventory updates:
   * Have the AI generate unit test cases first (PlayMode or EditMode).
   * Implement the feature logic and verify compilation.
   * **NEVER run the tests** (do not invoke `tests-run` via Unity MCP or run Unity test suites via CLI). Write the tests and rely on compilation checks only.
// Example requirement: Every server validation method must have isolated EditMode tests.
[Test]
public void Inventory_EquipItem_FailsWhenLevelRequirementNotMet() {
    ItemDefinition highLevelStaff = TestItemDefinitions.Create("Staff", requiredLevel: 10);

    bool result = InventoryValidator.CanEquip(playerLevel: 5, definition: highLevelStaff);

    Assert.IsFalse(result);
}

4. Compiling Without a Unity License (Agent / CI-less Environments)
The unity CLI and the Editor both require an activated license (unity license status →
none active; batch invocations then exit 198). Agentic sandboxes and locked-down CI often have
no license, so you cannot run Unity, its Test Runner, or unity test there. You can still
catch the vast majority of defects — every C# syntax/type/reference error — by compiling each
assembly directly with the Roslyn compiler Unity itself ships against the netstandard2.1
reference pack and the already-built package DLLs.
This is a compile check only: it verifies syntax, types, overloads and assembly references
exactly as Unity would. It does not run tests — anything that touches the engine at runtime
(JsonUtility, GameObject, ScriptableObject, …) still needs a real Unity run.
The recipe
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

Repeat per assembly, following the references in its .asmdef:
 * MMO.Editor — sources Assets/Editor/**, add -r:/tmp/MMO.Runtime.dll and the
   Unity.Cinemachine.Editor / Unity.RenderPipelines.* DLLs. Do not reference the monolithic
   Managed/UnityEditor.dll; its types are already in the Managed/UnityEngine/UnityEditor.*.dll
   modules and referencing both causes CS0433 duplicate-type errors.
 * MMO.Tests.EditMode / MMO.Tests.PlayMode — sources Assets/Tests/**, add -r:/tmp/MMO.Runtime.dll,
   -r:/tmp/MMO.Editor.dll (EditMode only), UnityEngine.TestRunner, UnityEditor.TestRunner and
   nunit.framework.dll (under
   Managed/.../com.unity.ext.nunit/net472/unity-custom/), plus -define:UNITY_INCLUDE_TESTS.
A clean run prints no error CS lines. Warnings (CS0618 obsolete IsServer, CS0649 on
JsonUtility DTO fields, CS1998 in tests) are expected and harmless.
Rules of thumb
 * Treat "compiles with 0 errors across all four assemblies" as the gate before handing work back
   when no license is available, and say so explicitly in the summary/PR — never claim tests pass.
 * To execute the suites, activate a license (unity license activate) or let
   .github/workflows/unity-tests.yml run EditMode + PlayMode
   on a licensed runner. That is the only source of truth for passing tests.
 * Do not commit .meta files. Assets/**/*.meta is gitignored; let Unity generate them on
   import and leave them untracked.
5. Autonomous Agent Batch Processing Workflow
When operating as an autonomous agent processing tasks via MCP (Model Context Protocol), strictly adhere to the following master execution loop and constraints to prevent context-switching, scope creep, and environment corruption.
Master Execution Prompt
You are an autonomous Unity & Infrastructure Engineer. Your objective is to process a batch of GitHub issues, resolve them in the local Unity environment using the Unity MCP, and submit individual Pull Requests using the Git/GitHub MCP. 

Process the following issues one at a time: [INSERT ISSUE NUMBERS HERE]

For EVERY issue in the list, execute this exact sequence before moving to the next:

1. TRIAGE & SCOPE:
- Query the GitHub MCP to retrieve the full issue description and comments.
- Identify the exact Unity components, C# scripts, or prefabs affected.

2. LOCATE & PLAN:
- Use the Unity MCP to search the project directory for the target files.
- Read the current file states.
- Formulate a precise modification plan. Do not proceed until you have verified the file paths exist.

3. EXECUTE & VERIFY:
- Checkout a new git branch named `fix/issue-[NUMBER]-[short-desc]`.
- Make the necessary code or asset changes using the local file system / Unity MCP tools.
- Trigger the Unity MCP compilation check (or the headless Roslyn compile script). If there are syntax errors or missing references, fix them immediately. Do not commit broken code.

4. SHIP:
- Stage the modified files.
- Commit with a conventional commit message (e.g., `fix: resolve issue #[NUMBER] regarding [topic]`).
- Push the branch to origin.
- Use the GitHub MCP to create a Pull Request against the main branch. Include a brief summary of the changes and link the issue (e.g., "Closes #[NUMBER]").

OPERATIONAL CONSTRAINTS (STRICT):
- Surgical Precision: Make small, highly targeted changes. Only modify lines directly relevant to the issue. 
- Zero Scope Creep: Do not perform opportunistic refactoring, formatting sweeps, or fix unrelated bugs you spot nearby. Stick exclusively to the ticket requirements.
- Unity Meta File Integrity: Never manually delete or overwrite `.meta` files. If modifying Unity YAML (Prefabs/Scenes), ensure you do not break GUIDs or references. Prefer making asset changes through Unity MCP API commands rather than raw text edits.
- Code Style Matching: Analyze the surrounding code in the file before writing. Match the existing naming conventions, bracket styles, and access modifiers exactly.
- Isolation & Reset: Never mix changes for different issues in the same branch. After submitting a PR, immediately switch back to the main branch and pull the latest changes before starting the next issue.
- Read Before Write: Never overwrite a file without reading its current contents first.
- Anti-Looping (Graceful Failure): If a compilation error persists after 3 repair attempts, or if you cannot find the required files, abort the current issue. Hard reset the branch, switch to main, leave a comment on the GitHub issue explaining the exact technical roadblock, and proceed to the next ticket.
- Autonomy: Do not ask for user permission between steps or issues unless you encounter a fatal, unresolvable error. 

Rationale for Agent Logic
 * Forced Sequential Processing: Prevents parallelization attempts that result in massive, unreviewable PRs or mixed code states.
 * Surgical Precision & Zero Scope Creep: Prevents collateral damage. Unintended rewrites of surrounding methods often break serialized event bindings or inspector references.
 * Unity Meta File Integrity: Avoids breaking project-wide references. Moving or renaming scripts without properly handling .meta files breaks every prefab that references them.
 * Code Style Matching: Prevents the introduction of inconsistent formatting (e.g., snake_case into a strict C# PascalCase/camelCase project).
 * Verification Gate: Forces validation against the local compiler checks before pushing, eliminating PRs with missing semicolons or broken references.
 * Anti-Looping (Graceful Failure): Acts as a circuit breaker for hallucinations. If an agent hallucinates a variable name and encounters a persistent compilation error, the 3-attempt limit ensures it fails safely, documents the issue, and continues the queue rather than burning tokens endlessly.

### Sub-Agent Delegation (Strictly Read-Only)
To increase processing speed, the primary agent may spawn sub-agents to handle parallel cognitive loads, provided they never mutate the local filesystem simultaneously. 

**Allowed Sub-Agent Tasks (Parallel):**
* **Triage & Research:** Spawning an agent to read a GitHub issue, fetch the relevant PR comments, and summarize the exact requirements.
* **Codebase Scanning:** Spawning a read-only agent to use the Unity MCP to search for specific methods, trace references, or locate prefabs.
* **Plan Generation:** Delegating the creation of the step-by-step modification plan to a sub-agent based on the gathered context.

**Forbidden Sub-Agent Tasks (Strictly Sequential):**
* **File System Mutability:** Only the primary agent is permitted to write to `.cs`, `.prefab`, `.unity`, or `.meta` files. 
* **Git Operations:** Only the primary agent is permitted to run `git checkout`, `git commit`, or `gh pr create`.
* **Compilation:** Only the primary agent may trigger the Roslyn compiler or Unity editor execution. 

By keeping all write-operations locked to the primary agent's sequential loop, we avoid Git `index.lock` crashes and Unity `Library` corruption while still parallelizing the heavy context-gathering phases.
