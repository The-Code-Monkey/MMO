# ADR-0001: The world topology is data, not scene code

## Status

Accepted. Recorded by #74.

## Context

The world is a web of zones joined by two-way portals, and the risk tier of each zone decides what
dying there costs. Today that knowledge is spread across scene authoring: `ZoneSceneBaker` hard-codes
which zones exist, which portals they carry and which tier each one has. Adding a zone means editing
editor code. The consequence is that "how many scenes will the world need" cannot be answered
without reading a baker, and no tool can check the map's shape.

## Decision

1. The macro map is a **data asset** (`WorldTopology`, with one `ZoneNode` per zone), and the asset
   is the authority for which zones exist and which portals join them.
2. The human-readable chapter (`docs/19-world-map-and-zone-topology.md`) explains the geography and
   the rationale; a test asserts every zone id in the asset appears in the chapter.
3. The map is checked by **pure rules** plus a **build guard**, never only by review:
   - a tier adjacency matrix, exhaustive, errors on any unlisted pair:
     `Safe → Safe, Yellow`; `Yellow → Yellow, Safe, Red`; `Red → Red, Black, Yellow`;
     `Black → Black, Red`;
   - every non-choke `Black` zone has exactly one portal;
   - a `Black ↔ Black` portal must pass through a `Black` choke zone, which is exempt from the
     one-door rule because it is a corridor.
4. **Unreachable zones are legal.** There is no reachability rule. A zone with no portals warns; the
   map may describe planned geography that is not yet cabled.
5. **Portals are two-way.** Both nodes list each other; a one-sided link is an error.
6. **Tiers may be undecided.** A node records whether its tier is settled; unsettled nodes warn and
   are skipped by the geometry rules, so a draft map never fails on data nobody has chosen yet.
7. Scene baking is **not** rewired by this decision. `ZoneSceneBaker` keeps working from its own
   constants until a follow-up makes it read the topology; the duplication is recorded as deferred
   debt rather than fixed in the same change.

## Consequences

- Adding or moving a zone becomes a data edit plus a chapter edit, not a code edit, and the map's
  shape is verifiable in EditMode tests and at build time.
- The same tier fact temporarily exists in two places (baker constants and the asset). Until the
  baker reads the topology, they must be kept in step by hand.
- One-way teleports are not representable. Adding them means an explicit direction on the portal and
  a relaxation of the symmetry rule, in their own decision.
- Runtime behaviour does not change: the tier a player dies under still comes from the scene's
  `ZoneRiskConfig`, and nothing loads the topology at runtime.
