<!-- coderabbit-ai-generated:v1 -->

<!-- coderabbit-ai-generated-section:v1:81a3a10675432513b9658eeaf1f55ad79314572c15d03bc4a7a5e2dcb2b88dd0 -->

# ADR-0001: The world topology is data, not scene code

## Status

Accepted design. Recorded by #74. Implementation is planned: `WorldTopology`, `ZoneNode`, the
asset, creation menu, validator, build guard and chapter coverage test do not exist yet.

## Context

The intended world is a web of zones joined by two-way portals. The built group currently includes
a one-way `Tutorial → SafeZone` portal with no return portal. The risk tier of each zone decides what
dying there costs. Today that knowledge is spread across scene authoring: `ZoneSceneBaker` hard-codes
which zones exist, which portals they carry and which tier each one has. Adding a zone means editing
editor code. The consequence is that "how many scenes will the world need" cannot be answered
without reading a baker, and no tool can check the map's shape.

## Decision

1. The macro map will be a **data asset** (`WorldTopology`, with one `ZoneNode` per zone). Once
   implemented, it will be the authority for zones and connectivity. The planned asset path is
   `Assets/Data/World/WorldTopology.asset`, with an `Assets > Create > MMO > World Topology` menu.

2. The human-readable chapter (`docs/19-world-map-and-zone-topology.md`) explains the geography and
   the rationale; a planned coverage test will assert every zone id in the asset appears there.
3. The planned validator will check the map with **pure rules** plus a **build guard**:

   - a tier adjacency matrix, exhaustive, errors on any unlisted pair:
     `Safe → Safe, Yellow`; `Yellow → Yellow, Safe, Red`; `Red → Red, Black, Yellow`;
     `Black → Black, Red`;
   - every non-choke `Black` zone has exactly one portal;
   - a `Black ↔ Black` portal must pass through a `Black` choke zone, which is exempt from the
     one-door rule because it is a corridor.
4. **Unreachable zones are legal.** There is no reachability rule. A zone with no portals warns; the
   map may describe planned geography that is not yet cabled.

5. **Portals are intended to be two-way.** The planned validator will require both nodes to list
   each other. The existing `Tutorial → SafeZone` exception needs an explicit direction in the
   model or symmetric built portals before it can be represented without an error.

6. **Tiers may be undecided.** A node records whether its tier is settled; unsettled nodes warn and
   are skipped by the geometry rules, so a draft map never fails on data nobody has chosen yet.
7. Scene baking is **not** rewired by this decision. `ZoneSceneBaker` keeps working from its own
   constants until a follow-up makes it read the topology; the duplication is recorded as deferred
   debt rather than fixed in the same change.

## Consequences

- After the tooling exists, adding or moving a zone will become a data edit plus a chapter edit,
  and the map's shape will be verifiable in EditMode tests and at build time. The chapter's Common
  tasks describe those future workflows.
- Once the asset exists, tier facts will temporarily exist in both baker constants and the asset.
  Until the baker reads the topology, they will need to be kept in step by hand.
- The planned model cannot represent one-way teleports, including the built Tutorial exit.
  Supporting them requires an explicit portal direction and relaxation of the symmetry rule in
  a further decision.

- Runtime behaviour does not change: the tier a player dies under still comes from the scene's
  `ZoneRiskConfig`, and nothing loads the topology at runtime.


<!-- /coderabbit-ai-generated:v1 -->