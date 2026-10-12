# 13 — Social: party and guild

Parties and guilds are **global** (they are not tied to one zone) and they survive sessions. This
chapter explains the records, the rules, the hub that owns the state, and how clients ask for changes.

## 1. The shape of the system

- **Clients** send intents through `PlayerParty` / `PlayerGuild` (FishNet `ServerRpc`s).
- The zone's **`SocialHubService`** owns all social state for its players. It is a server-side
  component built by `NetworkManagerFactory`.
- State is stored in Nakama through the `ISocialRepository` interface, so it is shared by every zone.
- Every mutation is a **read-modify-write with an optimistic version check** (the "CAS loop" —
  compare-and-swap). On a version conflict it re-reads and retries, up to `_casRetries` (default 5).
- A periodic reconcile (`SyncAsync`) diffs live players against backend state and pushes results to
  the networked rosters, which is how a party change in one zone reaches a member in another.

## 2. The records (what is stored)

### Party

`PartyRecord` (`Assets/Scripts/Social/PartyRecord.cs:31`):

| Field | Meaning |
|---|---|
| `PartyId` | Unique id. |
| `LeaderId` | The leader's account id. |
| `Members` | A list of `PartyMemberRecord`. |
| `RaidInstanceId` | Reserved for future dynamic raid instances. |
| `StorageVersion` | Backend revision hash for the optimistic write. |

`PartyMemberRecord` (`:8`) holds `AccountId`, `PlayerName`, `ZoneId` (refreshed by presence
heartbeats) and `JoinedAtTicks` (used to pick the new leader when the leader leaves).

### Guild

`GuildRecord` (`Assets/Scripts/Social/GuildRecord.cs:37`):

| Field | Meaning |
|---|---|
| `GuildId`, `Name`, `LeaderId` | Identity. |
| `Members` | A list of `GuildMemberRecord`. |
| `StorageVersion` | Backend revision hash. |

`GuildMemberRecord` (`:18`) holds `AccountId`, `PlayerName`, `Rank` (an int — see below) and
`JoinedAtTicks`. `GuildRank` (`:10`) is an enum: `Member` (0), `Officer` (1), `Leader` (2). It is
stored as an int because `JsonUtility` does not round-trip enum names.

### Presence and the social index

- `PresenceRecord` (`Assets/Scripts/Social/PresenceRecord.cs:13`) — an online heartbeat with a
  `HeartbeatTtl` of 90 seconds (`:16`). `IsHeartbeatFresh` (`:28`) treats a player as online only if
  they are online *and* their heartbeat is recent, so a crashed zone's players go stale rather than
  appearing online forever.
- `SocialIndexRecord` (`Assets/Scripts/Social/SocialIndexRecord.cs:33`) — a per-player pointer to
  their `PartyId`, `GuildId`, and any pending `PendingPartyInvite` (`:7`) / `PendingGuildInvite`
  (`:17`). This is **derived** state: the authoritative membership is in the party/guild object, and
  the reconcile pass repairs the pointer if a companion write failed.

## 3. The rules (pure, tested)

Party and guild rules are pure static classes, so every rule has an EditMode test with no network.

### PartyRules

`PartyRules` (`Assets/Scripts/Social/PartyRules.cs:29`), `MaxMembers = 5` (`:32`),
`InviteTtl = 5 minutes` (`:35`):

- `CanInvite` (`:42`) — target exists, is not self, is online, is not already in a party; if the
  inviter has a party, they must be in it and it must not be full.
- `CanAccept` (`:81`) — a pending invite exists, is not expired, the party is not already full and
  the player is not already a member.
- `Remove` (`:118`) — removes a member; if the party becomes empty it returns `null` (disbanded); if
  the leader left, the earliest-joined remaining member becomes leader.
- `CanKick` (`:139`) — only the leader may kick, and cannot kick themselves.

### GuildRules

`GuildRules` (`Assets/Scripts/Social/GuildRules.cs:32`), `MaxMembers = 50` (`:34`),
`MaxNameLength = 32` (`:35`), `InviteTtl = 5 minutes` (`:36`):

- `CanCreate` (`:39`) — non-empty account, valid name (trimmed, ≤ 32 chars via `IsValidName` `:54`),
  and not already in a guild.
- `CanInvite` (`:64`) — target online, not self, not already in a guild; inviter is a member of rank
  Officer or above; guild not full.
- `CanAccept` (`:102`), `Remove` (`:138`), `Promote` (`:219`), `CanKick` (`:169`), `CanPromote`
  (`:193`) — the membership lifecycle. A kick requires the actor to *strictly* outrank the target; a
  promotion is leader-only and only up to Officer.
- `Successor` (`:271`) — deterministic leader succession when the leader leaves: the earliest-joined
  officer, otherwise the earliest-joined member.

## 4. The repository

`ISocialRepository` (`Assets/Scripts/Social/ISocialRepository.cs:15`) is the storage boundary. It
carries the version-on-record contract: every load stamps `StorageVersion`, every save expects it to
match, and a losing race throws `Persistence.StorageVersionConflictException` so the hub can retry.

Methods cover parties (`LoadPartyAsync`, `SavePartyAsync`, `DeletePartyAsync`), the index
(`LoadIndexAsync`, `LoadIndexesAsync`, `SaveIndexAsync` — the batch form is used because Nakama
reads up to 100 ids per call), presence, the name index (`ResolveNameAsync`, `SaveNameAsync`) and
guilds. The production implementation is `NakamaSocialRepository`
(`Assets/Scripts/Persistence/NakamaSocialRepository.cs:17`), which translates records to and from
`NakamaRestClient` primitives under the shared service account.

## 5. The hub service

`SocialHubService` (`Assets/Scripts/Social/SocialHubService.cs:28`) is the server-side owner. It is a
singleton (`Instance`, `:30`) and is configured by `NetworkManagerFactory`. Tunable fields:
`_syncInterval` (2 s, `:32`), `_casRetries` (5, `:33`), `_presenceInterval` (30 s, `:34`),
`_connectRetrySeconds` (10 s, `:37`) and `_zoneId` (`:39`).

Events it raises for the shells to relay:

| Event | Fired when |
|---|---|
| `PartyChanged(PartyRecord)` | A party's roster changed (or disbanded, with `null`). |
| `GuildChanged(GuildRecord)` | A guild changed (or disbanded). |
| `PartyInviteReceived(accountId, invite)` | A pending party invite appeared for a player. |
| `GuildInviteReceived(accountId, invite)` | A pending guild invite appeared. |

Lifecycle:

- `Configure(ISocialRepository)` (`:96`) injects a repository. With the real Nakama repository it
  adopts the shared `NakamaRestClient`; with a fake it is treated as always connected.
- `RegisterLocal(accountId, playerName)` (`:126`) is called when a player spawns; it announces
  presence. `UnregisterLocal` (`:142`) is called on despawn.
- `Update` (`:148`) subscribes to the server connection once, then on a timer sends presence
  heartbeats (`_presenceInterval`) and runs `SyncAsync` (`_syncInterval`).

Party operations: `InviteAsync` (`:238`), `AcceptAsync` (`:305`), `DeclineAsync` (`:338`),
`LeaveAsync` (`:356`), `KickAsync` (`:395`). Guild operations: `CreateGuildAsync` (`:439`),
`InviteToGuildAsync` (`:484`), `AcceptGuildInviteAsync` (`:532`), `LeaveGuildAsync` (`:571`),
`PromoteAsync` (`:610`), `KickFromGuildAsync` (`:638`).

A subtle but important ordering rule appears in `InviteAsync` and `CreateGuildAsync`: the index
pointer is claimed **before** the party/guild object is written, so a failed companion write cannot
leave an orphaned party or guild with no owner.

### The reconcile pass

`SyncAsync` (`SocialHubService.cs:685`) is what makes social state global. It batch-loads all local
players' indexes, compares each against the last seen value, and when something changed:

- a vanished party/guild fires `PartyChanged(null)` / `GuildChanged(null)`,
- a new/changed pending invite fires the matching invite event,
- then it loads and fires `PartyChanged` / `GuildChanged` for the current party/guild.

Presence is handled by `AnnounceAsync` (`:754`), `HeartbeatAsync` (`:771`) and `SavePresenceAsync`
(`:777`), which carries the existing `StorageVersion` forward so a heartbeat upserts rather than
collides.

## 6. The networked shells

### PlayerParty

`PlayerParty` (`Assets/Scripts/Social/PlayerParty.cs:22`) is the client/server shell. The roster
travels as a JSON string in a `SyncVar<string>` (`:24-25`) — the same object-form SyncVar pattern as
everywhere else. Client intents (`RequestInvite`, `RequestAccept`, `RequestDecline`, `RequestLeave`,
`RequestKick`) each guard ownership and send a `[ServerRpc]`, which re-checks the sender and calls the
matching `SocialHubService` method. Results that fail are sent back to the owner with a `[TargetRpc]`
so the UI can show the reason. `ApplyRoster` (`:207`) is the `[Server]` method that writes the roster
into the SyncVar.

### PlayerGuild

`PlayerGuild` (`Assets/Scripts/Social/PlayerGuild.cs:16`) mirrors `PlayerParty`: intents
(`RequestCreate`, `RequestInvite`, `RequestAcceptInvite`, `RequestLeave`, `RequestPromote`,
`RequestKick`) → `[ServerRpc]` → hub → `ApplyRoster` (`:211`). Guild membership persists across
sessions; when a player spawns, the hub tells the shell their guild (if any) and the roster is
replicated.

## 7. A party invite, end to end

```
PLAYER A (zone 1)                    HUB (zone 1)                     Nakama / other zones
  RequestInvite("Bob") ─ ServerRpc ─► InviteAsync
                                        resolve "Bob" → account (name index)
                                        load Bob's presence (online?)
                                        PartyRules.CanInvite
                                        create party if A has none
                                        write PendingPartyInvite on Bob's index
                                        fire PartyInviteReceived(Bob)
                                                                          ◄── Bob's zone SyncAsync sees
                                                                              the pending invite and
                                                                              relays it to Bob
PLAYER B (zone 2)
  sees the invite; RequestAccept ─ ServerRpc ─► AcceptAsync
                                        PartyRules.CanAccept
                                        PartyRules.Accept
                                        save party, update index
                                        SyncAsync pushes the roster to both
  rosters update via SyncVar on both clients
```

## Common tasks

- **Tune party/guild size.** Edit `PartyRules.MaxMembers` (`:32`) or `GuildRules.MaxMembers` (`:34`).
  There are tests pinning these rules.
- **Change invite expiry.** Edit the `InviteTtl` on either rules class (currently 5 minutes).
- **Change the reconcile/presence cadence.** Edit `_syncInterval` / `_presenceInterval` on the
  `SocialHubService` component.
- **Add a new social action.** Add the rule to `PartyRules`/`GuildRules` (with tests), add the intent
  and `[ServerRpc]` on the shell, and add the operation to `SocialHubService` using the existing CAS
  loop. Add a repository method only if it needs new storage.
- **Test social logic offline.** Use `FakeSocialRepository` (in `SocialHubServiceTests` and
  `PlayerPartyPlayModeTests`) instead of Nakama; see [10](10-testing.md).
