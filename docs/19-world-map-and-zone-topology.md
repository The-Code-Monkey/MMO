<!-- coderabbit-ai-generated:v1 -->

<!-- coderabbit-ai-generated-section:v1:e08659bfe3c541e3496822b8c4bbb35be1fb11caa5c060864406a97efe43cf1b -->

# 19 — World map and zone topology

The world is not one open landscape. It is a web of **zones** joined by portals, and the roads
between them are deliberately arranged so that reward rises with risk. This chapter is the macro
map: the capital, the low-tier roads, the contested ring and the bottlenecks that gate full-loot
PvP. It also describes the planned data asset and tools that will encode and check the same map.

This chapter currently records the intended map. Once implemented, the **topology asset** will be
the machine artefact — which zones exist, which portals join them, and which tier each zone carries.
The asset will then be authoritative for connectivity, and disagreements with this chapter must be
corrected in the same pull request.

## 1. What a zone is

A **zone** is one server process running one scene with its own `NetworkManager` (see
[01, section 2](01-architecture-overview.md#2-the-two-halves-of-the-repository)). Players move
between zones through portals: a `ZoneTransferTrigger`
(`Assets/Scripts/Gameplay/ZoneTransferTrigger.cs:33`) on the ground announces a destination server
and zone name. The client asks to use it; the server re-checks the player's position and performs
the handoff (see [04, section 5](04-networking-and-the-join-flow.md#5-zone-transfers)).

Four zone scenes exist today, all built by `ZoneSceneBaker` (`Assets/Editor/ZoneSceneBaker.cs:38`):

| Zone id | Scene | Port | Risk tier |
|---|---|---|---|
| `tutorial` | `Tutorial.unity` | 7772 | `Safe` |
| `safe-zone` | `SafeZone.unity` | 7770 | `Safe` |
| `wilderness` | `Wilderness.unity` | 7771 | `Red` |
| `test-zone` | `TestZone.unity` | 7773 | `Safe` |

All four share one 40 m graybox coordinate space, so a transfer can keep the player's relative
position. They form one connected group: `safe-zone` has portals to `wilderness` and `test-zone`,
and `tutorial` has a portal to `safe-zone`.

## 2. The tier ladder

A zone's **risk tier** decides what dying there costs. `ZoneRiskTier` lives in
`Assets/Scripts/Gameplay/RiskRules.cs:13`; the death consequences are documented in
[05, section 1a](05-gameplay-stats-combat-abilities.md#1a-death-and-the-risk-tiers) and are not
repeated here.

| Tier | PvP | Death |
|---|---|---|
| `Safe` | none | gear dent, repair with gold |
| `Yellow` | PvE unless the player's PvP flag is on | heavier repair bill, no permanent loss |
| `Red` | forced PvP | respawn, permanent loss of progress, keep loot, durability loss |
| `Black` | forced PvP | drop carried and worn gear, profile wipe, durability loss |

"Blue zone" is the genre term for what this project calls **`Yellow`**. There is no `Blue` tier and
no resource-tier data in this chapter — resource rarity is a separate concern.

The PvP column above is **design intent, not implemented**. No code models a PvP flag yet; the
specification is tracked in #250 and listed under [Deferred](#deferred).

## 3. The macro map

Risk escalates outward from the capital. Low-tier roads fan out from the safe hub, feed a contested
ring, and every full-loot zone sits behind a single door.

```
Built today — one connected group

        tutorial
           │
           ▼
        safe-zone ◄────► wilderness (Red)
           ▲
           └────────────► test-zone

Planned — a separate group, not yet linked to the built group

        yellow-1 ─── red-1             black-1 (1 door)
           │              \           /
           │               pass-1 (Black choke)
           │              /           \
        yellow-2 ─── red-2             black-2 (1 door)
```

`◄────►` is one portal pair, usable in both directions. Every planned line also represents a
two-way portal pair. Planned links:

- `yellow-1 ↔ yellow-2`
- `yellow-1 ↔ red-1`
- `yellow-2 ↔ red-2`
- `red-1 ↔ pass-1`
- `red-2 ↔ pass-1`
- `black-1 ↔ pass-1 ↔ black-2`

Both red zones enter the shared black choke, `pass-1`; each non-choke black zone has only its
single portal to that choke, as required by section 4b.

The two groups have **no portal between them yet**. A player cannot walk from the capital into the
planned cluster, and that is intentional: the cluster is a design in progress, not content.

The map implies **11 zones total** — the four built scenes and seven planned zones. Seven new zone
scenes will eventually exist; none is baked yet.

## 4. Road rules

The shape of the intended map is not freeform. Three rules constrain it; the planned topology
validator will enforce them once implemented.

### 4a. The tier adjacency matrix

A portal may only join zones of these tiers. Every pair not listed is forbidden.

| From | May connect to |
|---|---|
| `Safe` | `Safe`, `Yellow` |
| `Yellow` | `Yellow`, `Safe`, `Red` |
| `Red` | `Red`, `Black`, `Yellow` |
| `Black` | `Black`, `Red` |

The matrix is exhaustive. A `Safe` zone touching a `Red` zone is a risk cliff with no warning
signal; a `Yellow` zone touching a `Black` zone skips the contested ring; both are rejected.

### 4b. Choke points

A **choke point** is a narrow zone — a canyon pass, a bridge, a mountain path — that a zone's single
portal runs through. It is a zone, not a marker on a portal, so players can be fought over and
blocked inside it.

- Every non-choke `Black` zone has **exactly one** portal. That is what makes a black zone a
  commitment rather than a place to kite through.
- A `Black` zone may connect to another `Black` zone, but only through a `Black` zone flagged as a
  choke point. Choke zones are corridors, so they are the only black zones allowed more than one
  portal.
- Because `Black` only connects to `Red` and `Black`, every black zone is necessarily gated by a
  red zone or a black choke. There is no direct road from a safe or yellow zone into full-loot
  space.

### 4c. What is not constrained

- **Zones may be unreachable.** Some zones are planned before their roads exist, and the map must
  be able to describe them. An isolated zone is reported as a warning, never an error.

- **Portals are intended to be two-way.** Both zones will list each other in the planned topology.
  The built group currently has an exception: `Tutorial → SafeZone` has no return portal
  (`ZoneSceneBaker.BuildTutorial` and `BuildSafeZone`). The planned symmetric model cannot represent
  that existing one-way link; see [Deferred](#deferred).

- **Tiers may be undecided.** A node records whether its tier has been settled. An unsettled node
  warns and is skipped by the geometry rules, so a draft map never fails on data that has not been
  chosen yet.

## 5. The topology asset

`WorldTopology`, `ZoneNode`, the asset, and its creation menu are **planned, not implemented**.
The proposed ScriptableObject will hold one `ZoneNode` per zone at
`Assets/Data/World/WorldTopology.asset`, created from the planned
`Assets > Create > MMO > World Topology` menu.
It will be **editor-consumed only**, outside `Resources`. The tier a player actually dies under
comes from the scene's `ZoneRiskConfig` (`Assets/Scripts/Gameplay/ZoneRiskConfig.cs:17`).

Each proposed node will carry:

| Field | Meaning |
|---|---|
| `ZoneId` | Stable name used by portals and by this chapter. Renaming one is a data change. |
| `SceneName` | The scene that will host it. Empty means planned and not yet baked. |
| `Tier` | The zone risk tier. |
| `TierAssigned` | False while the tier is still a draft. Settled tiers are validated. |
| `Neighbours` | The other zones it shares a portal with, listed on both sides. |
| `IsChokePoint` | True for the narrow zones of section 4b. |

The planned asset will record the four built zones with the tiers their scenes actually carry,
but mark those tiers unsettled: the capital currently opens straight onto `wilderness` (`Red`), which the matrix
in section 4a forbids. Making that geography legal is a design task, tracked with the planned
cluster's tier assignment in #249.

## 6. What checks the map

The following checks are **planned, not implemented**:

- **Topology rules / validator** — a pure static class that will report structural and geometric
  problems as errors and warnings. It will touch no engine state and will have EditMode coverage.
- **Topology build guard** — a build preprocessor that will reject a missing topology asset, any
  topology error, or a named scene absent from enabled Build Settings. Warnings will be logged.
- **Chapter coverage test** — a test that will assert every zone id in the asset appears in this
  chapter, to detect drift between prose and data.

## 7. Common tasks

These workflows apply **only after the topology asset, creation menu, validator, build guard and
coverage test have been implemented**. Today, use section 3 for the intended map and
`ZoneSceneBaker` for the built portals.

- **Add a planned zone** — add a node to the topology asset, add its portals on both sides, and add
  it to the map in section 3. Unsettled tiers are fine; the validator will skip them.
- **Settle a zone's tier** — set `Tier`, tick `TierAssigned`, then run the rules. The matrix and the
  choke rules now apply to that node, including any portal it already had.
- **Move a portal** — update `Neighbours` on *both* nodes. A one-sided change is an error, not a
  one-way road.
- **Add a baked zone** — set `SceneName` on the node and register the scene in Build Settings. A
  built node whose scene is missing fails the build.
- **Read the map without opening Unity** — section 3 is the same information; if it disagrees with
  the asset, fix the chapter.

## Deferred

- **Topology tooling and scene baking.** The topology asset and checks in sections 5–6 still need
  implementation. `ZoneSceneBaker` hard-codes the four zones and their tiers; making it read the
  future topology is a follow-up to #67.
- **One-way links in the topology model.** The planned model supports only two-way portals. To
  represent the existing `Tutorial → SafeZone` exception, it will need an explicit edge direction
  and a relaxation of the symmetry rule, or the built portals must be made symmetric.

- **PvP rules.** The flag lifecycle and per-tier PvP enforcement are specified in #250.
- **Tier assignment for both groups.** #249 settles the planned cluster and the built group's
  capital-to-wilderness clash.
- **Terrain and biomes** (#76) and **population** (#78) decide what the zones look like and contain.
- **Lore and faction identity** (#75) explain why the geography is shaped this way.


<!-- /coderabbit-ai-generated:v1 -->