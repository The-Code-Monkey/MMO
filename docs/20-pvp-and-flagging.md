# 20 — PvP and the flag

The map in [19](19-world-map-and-zone-topology.md) says which zones are dangerous. This chapter says
what *dangerous* means for players fighting players: who may harm whom in each risk tier, how a
player opts in and out, and where the server decides. It deliberately does **not** restate the death
costs — those belong to the tier and are defined once in
[05, section 1a](05-gameplay-stats-combat-abilities.md#1a-death-and-the-risk-tiers).

It was written as the specification for #250 and implemented in #271–#274 (section 7). Where the
prose below describes a rule, `PvPRules` is the thing that decides it, and the sections name the
seams it had to reach.

## 1. Two separate questions

It is tempting to think a risk tier turns PvP on. It does not, quite. Two questions are asked at
every hit, in this order:

1. **May this player be harmed by another player here?** Answered by the zone tier plus the
   target's flag.
2. **What does dying here cost?** Answered by the tier alone, by `RiskRules.ResolveDeath`
   (`Assets/Scripts/Gameplay/RiskRules.cs:99`).

The flag only ever affects the first question. It never changes a repair bill, a drop, or a
durability loss — a flagged player who dies in a `Yellow` zone pays exactly what an unflagged one
pays. Keeping the two apart is what lets the death rules stay in one pure table instead of growing a
PvP dimension.

## 2. The per-tier rule

| Tier | Player damage allowed when | Who is a valid target |
|---|---|---|
| `Safe` | never | nobody |
| `Yellow` | the target's flag is on | flagged players only |
| `Red` | always | everybody, flag ignored |
| `Black` | always | everybody, flag ignored |

`Red` and `Black` are **forced**: the tier overrides the flag in both directions. Entering a forced
zone does not clear a player's stored preference; it only makes the preference irrelevant while they
are inside. Leaving restores it.

In `Yellow` an unflagged player is untouchable by *any* player damage — direct or area. This is the
one place where the current code is actively wrong (see section 5): a fireball centre is currently
damage, not a duel.

Attacking a flagged player is consent. If an unflagged player lands a hit on a flagged one in
`Yellow`, the server flags the attacker in the same step, before the damage is applied. From then
on the fight is symmetric.

## 3. The flag lifecycle

The flag is a **preference**, not a status:

- **Opt in** — from anywhere, at any time, through the HUD. No NPC, no capital city, no gold. The
  request is one server call; the flag is on the moment the server accepts it.
- **Opt out** — allowed only after **five minutes** with no player-versus-player damage dealt or
  taken. The timer is reset by player damage in either direction; NPC damage neither starts nor
  extends it. The server holds the timestamp; the client only displays a countdown.
- **Refused in forced zones** — the toggle reports "PvP is forced here" and does nothing in `Red`
  and `Black`. The stored preference is untouched.
- **Survives zoning** — a zone transfer carries the flag with the rest of the save record
  ([04, section 5](04-networking-and-the-join-flow.md#5-zone-transfers)), so a flagged player is
  still flagged on arrival.
- **Survives death and relog** — dying does not clear the flag, and it is persisted in
  `PlayerSaveRecord` so a reconnect restores it. Only the player clears it, and only by opting out.

The cancelled-by-combat window exists so that opting out cannot be used to escape a fight that is
already underway. It does not make the player invulnerable: while the cooldown runs, the flag is
still on.

## 4. Where the server decides

The verdict is **one pure function** so it can be pinned by EditMode tests and read by anyone:

```
PvPRules.CanHarm(tier, attackerFlagged, targetFlagged)
    Safe              -> false
    Yellow            -> targetFlagged
    Red, Black        -> true

PvPRules.AutoFlags(tier, attackerFlagged, targetFlagged)
    Yellow && targetFlagged && !attackerFlagged -> true
    otherwise                                   -> false
```

`CanHarm` answers the question in section 2; `AutoFlags` answers "does this hit make the attacker
consent". The order matters — the server evaluates `AutoFlags` first and, when it returns true,
writes the flag before applying damage, so the hit is legal under the flag it just created.

The inputs are the server's, never the client's:

| Input | Source | Can a client fake it? |
|---|---|---|
| `tier` | `ZoneRiskConfig.ResolveTierAt` (`Assets/Scripts/Gameplay/ZoneRiskConfig.cs:75`) on the server | no — position and scene are server-side, and the tier is scanned from them |
| `targetFlagged` | the target's `SyncVar<bool>` on the server | no — FishNet refuses client writes; the client copy is a replica |
| `attackerFlagged` | the attacker's `SyncVar<bool>` on the server | no — same, and the auto-flag write is `[Server]` only |

The opt-out timestamp is equally server-owned: it is set when the server applies or receives player
damage, and read when the server validates an opt-out request. A client that lies about its clock
changes a countdown it draws, not a decision the server makes. The UI state is decoration.

## 5. The damage sites

Player damage reaches a victim in four places. The spec names them so that a fifth one appearing
later is a documentation bug, not a silent PvP hole.

| Site | What it does today | What it must do |
|---|---|---|
| `PlayerCombat.Attack` (`Assets/Scripts/Gameplay/PlayerCombat.cs:105`) | resolves the target as `NPCStats` or `TargetDummy` and refuses anything else, so **players cannot be meleed at all** | gain a player branch, gated by `CanHarm` before any damage |
| `PlayerAbilities.ApplyEffect` (`Assets/Scripts/Gameplay/PlayerAbilities.cs:225`) | takes every `PlayerStats` from `AbilityEffects.SelectVictims` (`Assets/Scripts/Gameplay/AbilityEffects.cs:61`) and calls `TakeDamage` on each — **in every zone, including `Safe`** | filter the victim list through `CanHarm` / `AutoFlags` |
| `PlayerAbilities.ApplyAura` (`Assets/Scripts/Gameplay/PlayerAbilities.cs:293`) | the same selection, then applies a damage-over-time aura | the same filter when the aura is applied |
| `AuraController.ApplyTick` (`Assets/Scripts/Content/AuraController.cs:169`) | applies a damage-over-time tick to a player with no PvP check, so an aura outlives the fight that justified it | the same verdict per tick, skipped when the tier or the flags no longer allow it |

`PlayerStats.TakeDamage` (`Assets/Scripts/Gameplay/PlayerStats.cs:149`) stays exactly as it is: a
source-less `[Server]` mutator. It cannot be the gate because it does not know who is hitting whom,
and the gate must not be pushed down into it — a future fourth call site would then bypass every
rule in this chapter without anyone noticing.

`EnemyBrain`, auras cast by NPCs and any other non-player damage source are not PvP and are not
gated by any of this.

## 6. How it is built

- **The flag** — `PlayerPvP` (`Assets/Scripts/Gameplay/PlayerPvP.cs:27`) carries the `SyncVar<bool>`,
  the opt-out timestamp, the replicated countdown and the one `ServerRpc` a client may call. It is
  on the player prefab and is persisted by `PlayerSaveTrigger` through `PlayerSnapshotFactory` and
  `PlayerSaveApplier`, which is what makes it survive a zone transfer and a reconnect.
- **The verdict** — `PvPRules` is static, pure and has no engine types. `PlayerPvP.TryEngage` is the
  only place that pairs it with a position, and every damage site calls it before damage lands.
- **Area abilities** are gated in `PlayerAbilities` (`SelectPvPVictims`), melee in
  `PlayerCombat.Attack`, and damage-over-time in `AuraController.CanHarmFromSource`. An unflagged
  player in a yellow zone is now untouchable by all three.
- **The HUD** — `PvPFlagView` (`Assets/Scripts/UI/PvPFlagView.cs:15`) is built in code, bound by
  `LocalPlayerHud` to the player's own `IPvPSource`, and repaints only from replicated state.
- **Nothing reads the flag for AI, loot or death.** `PlayerDeath` (`Assets/Scripts/Gameplay/PlayerDeath.cs:32`)
  reads the tier and nothing else, which is correct and stays that way.

## 7. How it was split

Four tickets, each landing with its own tests:

1. **Pure rules** (#271) — `PvPRules` with `CanHarm`, `AutoFlags` and the opt-out cooldown
   arithmetic. No engine types; every tier and both flag states are pinned in EditMode.
2. **Flag state and persistence** (#272) — `PlayerPvP` on the player prefab, the `PlayerSaveRecord`
   field with the schema bump, and the load/capture round trip.
3. **Enforcement** (#273) — the four sites in section 5, including auto-flagging an unflagged
   attacker.
4. **HUD** (#274) — the toggle and its countdown, built by the code-built UI pattern of
   [08](08-ui-main-menu-and-hud.md), disabled and labelled `forced` in `Red`/`Black`.

## 8. Common tasks

- **Add a tier** — extend `ZoneRiskTier` (`Assets/Scripts/Gameplay/RiskRules.cs:13`), decide its
  row in section 2, and add the case to `CanHarm`. A tier without a PvP rule is a compile error by
  design; do not give it a default.
- **Add a damage site** — add it to the table in section 5 in the same pull request, and gate it
  before the damage lands through `PlayerPvP.TryEngage`. A new site that skips `CanHarm` is the failure mode this chapter exists
  to prevent.
- **Change the cooldown** — it lives in one place, the opt-out rule in #271. Do not duplicate the
  number into the HUD; the HUD asks the server.
- **Change what dying costs** — that is
  [05](05-gameplay-stats-combat-abilities.md#1a-death-and-the-risk-tiers), never this chapter.

## Deferred

- **Duels and consensual arena fights** — a `Safe` zone refusing all player damage is the rule; a
  duel would need an explicit exception rather than a flag.
- **Party, guild and faction friendly-fire** — no relationship rules are applied. Flagged guild
  members can hit each other today by this spec, and faction PvP (#98) will want to change that.
- **Territory war and claim flags** — the vulnerability windows of
  [16](16-trading-and-territory.md) are a separate consent mechanism; how they interact with the
  flag is undecided.
- **Zone-entry warning** — a client-side banner when crossing into a forced zone is not specified
  here.
- **Bounties, kill tracking and PvP rewards** — nothing scores a player kill yet.
