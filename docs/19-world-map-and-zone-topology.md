# 19 — World map and zone topology

The world is not one open landscape. It is a web of **zones** joined by portals, and the roads
between them are deliberately arranged so that reward rises with risk. This chapter is the macro
map: the capital, the low-tier roads, the contested ring and the bottlenecks that gate full-loot
PvP. It also describes the data asset that encodes the same map, which is what the tools and the
build guard actually read.

The map is written down twice on purpose. This chapter is the human artefact — why the world is
shaped this way. The **topology asset** is the machine artefact — which zones exist, which portals
join them, and which tier each zone carries. When the two disagree, the asset wins for
connectivity and this chapter must be corrected in the same pull request.

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
| `wilderness` | `Wilderness.unity` | 7771 | `Yellow` |
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
| `Red` | forced PvP | respawn, keep loot, durability loss |
| `Black` | forced PvP | drop carried and worn gear, durability loss |

"Blue zone" is the genre term for what this project calls **`Yellow`**. There is no `Blue` tier and
no resource-tier data in this chapter — resource rarity is a separate concern.

The PvP column above is **design intent, not implemented**. Who may harm whom per tier, and how the
PvP flag works, is specified in [20](20-pvp-and-flagging.md); the implementation is tracked in the
tickets listed there. No code models the flag yet.

## 3. The macro map

Risk escalates outward from the capital. Low-tier roads fan out from the safe hub, feed a contested
ring, and every full-loot zone sits behind a single door.

```
Built today — one connected group

        tutorial
           │
           ▼
        safe-zone ◄────► wilderness (Yellow)
           ▲
           │
           └──────►  test-zone

Planned — a separate group, not yet linked to the built one

        yellow-1 ─────► red-2
           │               │
           ▼               ▼
        yellow-2 ─────► red-1
                           │
                           ▼
                        pass-1   (black choke: the only way into either black zone)
                        │    │
                        ▼    ▼
                    black-1  black-2
```

`◄────►` is one portal pair, usable in both directions. Planned links:

- `yellow-1 ↔ yellow-2 ↔ red-1`
- `red-2 ↔ yellow-1`
- `red-2 ↔ red-1`
- `red-1 ↔ pass-1`
- `pass-1 ↔ black-1`
- `pass-1 ↔ black-2`

The two groups have **no portal between them yet**. A player cannot walk from the capital into the
planned cluster, and that is intentional: the cluster is a design in progress, not content.

The planned cluster's tiers are settled:

| Zone | Tier | Doors | Role |
|---|---|---|---|
| `yellow-1` | `Yellow` | 2 | low-tier hub at the cluster's mouth |
| `yellow-2` | `Yellow` | 2 | second hub, the first step toward the ring |
| `red-1` | `Red` | 3 | contested gate to the pass |
| `red-2` | `Red` | 2 | contested side road |
| `black-1` | `Black` | 1 | full loot, one door |
| `black-2` | `Black` | 1 | full loot, one door |
| `pass-1` | `Black` | 3 | the choke: the only door to either black zone |

"Doors" is the number of portals in `Neighbours`. The built group settles as `Safe` hubs
(`tutorial`, `safe-zone`, `test-zone`) with the `wilderness` as a `Yellow` hunting ground off the
capital. A capital road must never open straight onto `Red`, so the `Red`/`Black` ladder belongs to
the planned cluster; the `wilderness` keeps two `Red` hazard volumes, because `Red` next to
`Yellow` is a legal pair and the pits are where the `Red` death rule is exercised today.

The map implies **11 zones total** — the four built scenes and seven planned zones. Seven new zone
scenes will eventually exist; none is baked yet.

## 4. Road rules

The shape of the map is not freeform. Three rules constrain it, and the topology validator enforces
them.

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
- **Portals are two-way.** Both zones must list each other. One-way links are not supported today;
  see [Deferred](#deferred).
- **Tiers may be undecided.** A node records whether its tier has been settled. An unsettled node
  warns and is skipped by the geometry rules, so a draft map never fails on data that has not been
  chosen yet.

## 5. The topology asset

`WorldTopology` is a ScriptableObject holding one `ZoneNode` per zone. It lives at
`Assets/Data/World/WorldTopology.asset` and is created from `Assets > Create > MMO > World Topology`.
It is **editor-consumed only**: it is not under `Resources`, nothing loads it at runtime, and the
tier a player actually dies under still comes from the scene's `ZoneRiskConfig`
(`Assets/Scripts/Gameplay/ZoneRiskConfig.cs:17`).

Each node carries:

| Field | Meaning |
|---|---|
| `ZoneId` | Stable name used by portals and by this chapter. Renaming one is a data change. |
| `SceneName` | The scene that will host it. Empty means planned and not yet baked. |
| `Tier` | The zone risk tier. |
| `TierAssigned` | False while the tier is still a draft. Settled tiers are validated. |
| `Neighbours` | The other zones it shares a portal with, listed on both sides. |
| `IsChokePoint` | True for the narrow zones of section 4b. |

Every node's tier is settled: `TierAssigned` is true on all eleven, so the matrix and the choke
rules apply to the whole map and both rules report neither errors nor warnings. The four built zones
are recorded with the tier their scenes actually carry — `Safe` apart from the `Yellow`
`wilderness`, which keeps its two `Red` hazard volumes. A test opens each built scene and compares
its zone-root `ZoneRiskConfig` to the node, so the asset, the chapter and the scenes cannot drift
apart silently.

## 6. What checks the map

Two pieces keep the asset honest:

- **Topology rules** — a pure static class that reports every structural and geometric problem as a
  list of errors plus a list of warnings. It touches no engine state, so every branch is an EditMode
  test.
- **Topology build guard** — a build preprocessor that refuses to produce a build when the topology
  asset is missing, has any error, or names a scene that is not in enabled Build Settings. Warnings
  are logged, not fatal.

Tests also assert that every zone id in the asset is mentioned in this chapter, so the prose and the
data cannot drift apart.

## 7. Common tasks

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

- **The topology does not yet drive scene baking.** `ZoneSceneBaker` still hard-codes the four
  zones and their tiers. Making the baker read the topology is a follow-up to #67; until then the
  same fact exists in two places.
- **One-way links.** Only two-way portals are modelled. A one-way teleport will need an explicit
  direction on the edge and a relaxation of the symmetry rule.
- **PvP rules.** The flag lifecycle and per-tier PvP enforcement are specified in
  [20](20-pvp-and-flagging.md); implementation is split into #271–#274.
- **Terrain and biomes** (#76) and **population** (#78) decide what the zones look like and contain.
- **Lore and faction identity** (#75) explain why the geography is shaped this way.
