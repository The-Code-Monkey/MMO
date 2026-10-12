# 10 — Testing

The project treats tests as part of the feature, not an afterthought. This chapter explains the two
test suites, the fakes that make offline testing possible, and how to run everything.

## 1. The two suites

Tests are split into two Unity Test Framework (UTF) assemblies, which run in two different modes:

| Assembly | Folder | Mode | What it tests |
|---|---|---|---|
| `MMO.Tests.EditMode` | `Assets/Tests/EditMode` | EditMode | Pure logic, DTO round-trips, validators, ScriptableObject data, prefab bakers/factories, the save queue with an explicit clock. No socket, no game loop. |
| `MMO.Tests.PlayMode` | `Assets/Tests/PlayMode` | PlayMode | Real runtime loop and a real local FishNet socket: connect → load → save, the Main Menu login end-to-end, the gate admitting/refusing by token, player spawning. |

Roughly, EditMode is the workhorse (hundreds of fast `[Test]` methods) and PlayMode is the smaller,
slower set of coroutine `[UnityTest]` methods. Both are configured the standard UTF way
(`overrideReferences` + `nunit.framework.dll`, `autoReferenced: false`, gated on
`UNITY_INCLUDE_TESTS`). Both are Editor-only; the PlayMode assembly does not reference `MMO.Editor`.

The exact editor version is `6000.6.5f1`, the test framework is `com.unity.test-framework` `1.8.0`
on NUnit `com.unity.ext.nunit` `2.1.0`.

## 2. Why so much is testable: the pure-core pattern

As chapter [01](01-architecture-overview.md) explains, gameplay logic is split into pure cores and
networked shells. The pure cores take every input as an argument — including `now` — so a test can
drive them with made-up numbers and no waiting. For example, every rule in `AbilityCastRules`
(see [05](05-gameplay-stats-combat-abilities.md)) has an isolated EditMode test, and
`AbilityCooldownTrackerTests` verifies cooldowns without a single frame elapsing.

## 3. The shared fakes and helpers

These types are used across many tests:

| Type | File | Purpose |
|---|---|---|
| `FakeApiGateway` | `Assets/Tests/PlayMode/FakeApiGateway.cs:14` | A stand-in for `IApiGateway`. Canned login outcomes, a full call log, and a "block login" switch for race tests. |
| `NetworkManagerTestHelper` | `Assets/Tests/PlayMode/NetworkManagerTestHelper.cs:16` | Because FishNet allows one `NetworkManager` per process, every PlayMode test shares one. Handles host start/stop, waiting for authentication, and finding spawned players/dummies. |
| `TestItemDefinitions` | `Assets/Tests/EditMode/TestItemDefinitions.cs:14` | Builds and registers `ItemDefinition` objects in code by writing every field through a `SerializedObject` (the Inspector path). |
| `PlayerPrefabTestAsset` | `Assets/Tests/EditMode/PlayerPrefabTestAsset.cs:13` | Tracks whether the player prefab/folder pre-existed so `[TearDown]` removes only what the test created. |
| `SpawnablePrefabTestAsset` | `Assets/Tests/EditMode/SpawnablePrefabTestAsset.cs:13` | The same idea for the player, dummy and resource-node prefabs. |
| `InMemoryPlayerRepository` | nested in `Assets/Tests/EditMode/PlayerPersistenceServiceTests.cs:209` | An `IPlayerRepository` backed by a dictionary, with a `ThrowOnSave` switch to inject failures. |

### The in-memory repository (the most important fake)

```csharp
private sealed class InMemoryPlayerRepository : IPlayerRepository
{
    private readonly Dictionary<string, PlayerSaveRecord> _records = new();
    public bool ThrowOnSave;

    public Task<PlayerSaveRecord> LoadAsync(string userId, CancellationToken ct = default)
        => Task.FromResult(_records.TryGetValue(userId, out var r) ? r : null);

    public Task SaveAsync(PlayerSaveRecord record, CancellationToken ct = default)
    {
        if (ThrowOnSave) throw new InvalidOperationException("injected save failure");
        _records[record.UserId] = record;
        return Task.CompletedTask;
    }
}
```

This is what lets the entire save-lifecycle be tested with no Nakama. `PlayerPersistenceService`
exposes `Configure(IPlayerRepository)` and a fake-clock entry point (`ProcessDueSavesAsync(double now)`)
precisely so tests can control time and failure.

## 4. What a good test looks like here

The house style is small, focused, and named for the rule it pins:

```csharp
[Test]
public void Validate_RejectsACastTheCasterCannotPayFor()
{
    AbilityData fireball = NewAbility("Fireball", manaCost: 20);
    AbilityCooldownTracker tracker = new();

    Assert.AreEqual(
        AbilityCastResult.NotEnoughMana,
        AbilityCastRules.Validate(0, Slots(fireball), tracker,
            currentMana: 5, distanceToTarget: 10f, now: 0d));
}
```

And for a validator, mirroring the `agents.md` example:

```csharp
[Test]
public void CanEquip_False_WhenPlayerBelowRequiredLevel()
{
    ItemDefinition helm = TestItemDefinitions.Create("Iron Helm", requiredLevel: 10);

    Assert.IsFalse(InventoryValidator.CanEquip(playerLevel: 5, definition: helm));
}
```

Notes:

- `now` is always explicit. No test sleeps to make a cooldown elapse.
- Prefabs and assets created by a test are cleaned up in `[TearDown]`, and the `*TestAsset` helpers
  make sure a test never deletes something that was already there.
- No `[TestCase]` style is used; tests are individual methods.

## 5. PlayMode specifics

- `NetworkManagerTestHelper.TestPort = 7979` (`:22`) — away from the shipped 7770 so tests do not
  collide with a running editor server.
- `TestHandshakeTimeoutSeconds = 30f` (`:28`) — a generous CI timeout.
- Waiting is done by polling frames with a cap (for example `WaitForPlayerObject` polls up to 600
  frames), never by a fixed sleep.
- The test repository and gateway mean no Nakama is required for any test.

## 6. Running the tests

### In the editor

Open **Window > General > Test Runner**. You get tabs for EditMode and PlayMode; use **Run All** or
select individual tests. This is the fastest feedback loop.

### In CI

`.github/workflows/unity-tests.yml` runs both suites on every push using
`game-ci/unity-test-runner@v4` with `testMode: all` and `unityVersion: auto` (which reads the
project version). It uploads the results as an artifact. `coverageEnabled` is `false` — Unity
coverage collection was found to be crash-prone in this project, so coverage is tracked by discipline
rather than a gate.

### Headless locally

Unity batch mode can run both platforms (`-runTests -testPlatform EditMode`, then `PlayMode`), but
the Test Runner window is usually more convenient.

## 7. The coverage rule

`agents.md` mandates **90%+ coverage** of core game logic and network payloads, with tests written
**before** the feature (TDD). The practical version:

1. Write the failing tests first (EditMode if the logic is pure, PlayMode if it needs a socket).
2. Implement until they pass.
3. Make sure nothing existing broke.

See [12](12-conventions-and-checklist.md) for the full checklist.

## Common tasks

- **Add a test for new pure logic.** Put it in `Assets/Tests/EditMode`. Drive the class directly with
  explicit inputs, including `now`.
- **Add a test that needs a connection.** Put it in `Assets/Tests/PlayMode`, use
  `NetworkManagerTestHelper.GetOrCreate()`, and poll for state with a frame cap.
- **Fake the backend.** Implement `IApiGateway` like `FakeApiGateway`, or `IPlayerRepository` like
  `InMemoryPlayerRepository`. Do not add a network dependency to a test.
- **Create item/recipe fixtures in code.** Use `TestItemDefinitions.Create(...)` or build a named
  `ScriptableObject` and register it, then clear the registry in teardown.
- **Test a baker.** Follow the `PlayerPrefabTestAsset` / `SpawnablePrefabTestAsset` pattern so the
  test deletes only what it created.
- **Find what covers a class.** Search `Assets/Tests` for the class name; the convention is a
  matching `*Tests.cs` file.
