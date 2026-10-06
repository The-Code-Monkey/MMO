using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FishNet.Managing;
using MMO.Gameplay;
using MMO.Net;
using MMO.Persistence;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace MMO.Tests
{
    /// <summary>
    /// The persistence wiring end to end over a live host connection: the join waits for the save,
    /// the spawned player spawns with it, and disconnecting pushes the final state back. The backend
    /// is an in-memory fake swapped in through <see cref="PlayerPersistenceService.Configure"/>, so
    /// these tests need no running Nakama — the point here is the wiring, not the transport.
    /// </summary>
    public sealed class PlayerPersistencePlayModeTests
    {
        private sealed class InMemoryPlayerRepository : IPlayerRepository
        {
            public readonly Dictionary<string, PlayerSaveRecord> Records = new();

            public Task<PlayerSaveRecord> LoadAsync(string userId, CancellationToken ct)
                => Task.FromResult(Records.TryGetValue(userId, out PlayerSaveRecord record) ? record : null);

            public Task SaveAsync(PlayerSaveRecord record, CancellationToken ct)
            {
                Records[record.UserId] = record;
                return Task.CompletedTask;
            }
        }

        private sealed class DeferredPlayerLoader : IPlayerLoader
        {
            private readonly TaskCompletionSource<PlayerSaveRecord> _source =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public bool LoadCalled;
            public PlayerSaveRecord Result;

            public Task<PlayerSaveRecord> LoadAsync(string accountId, CancellationToken ct)
            {
                LoadCalled = true;
                return _source.Task;
            }

            public void Release() => _source.SetResult(Result);
        }

        private NetworkManager _manager;
        private MMOConnectionGate _gate;
        private PlayerPersistenceService _service;
        private IPlayerLoader _originalLoader;
        private InMemoryPlayerRepository _repo;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _manager = NetworkManagerTestHelper.GetOrCreate();
            _gate = NetworkManagerTestHelper.Gate(_manager);
            _service = _manager.GetComponent<PlayerPersistenceService>();
            Assert.IsNotNull(_service, "The factory must place the persistence service to wire the seam.");

            _originalLoader = _gate.PlayerLoader;

            // NetworkManager.Awake runs on the frame the object activates and the join gate
            // subscribes in Start, so give the manager a frame before anything connects.
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            NetworkManagerTestHelper.StopIfListening(_manager);
            _gate.PlayerLoader = _originalLoader;
            _service.Configure(new NakamaPlayerRepository(new BackendSettings()));
            _gate.SetSettings(new MMOConnectionSettings(
                NetworkManagerTestHelper.TestPort,
                MMOConnectionSettings.DefaultMaxClients,
                NetworkManagerTestHelper.TestHandshakeTimeoutSeconds));

            yield return null;
        }

        [UnityTest]
        public IEnumerator Factory_WiresTheServiceAsTheJoinLoader()
        {
            Assert.AreSame(_service, _gate.PlayerLoader, "The gate must await the service so saves reach the player.");

            yield return null;
        }

        [UnityTest]
        public IEnumerator Join_IsWithheldUntilTheSaveLoads_ThenSpawnsWithIt()
        {
            var loader = new DeferredPlayerLoader();
            _gate.PlayerLoader = loader;

            NetworkManagerTestHelper.StartHost(_manager);

            int frames = 0;
            while ((_gate.PendingHandshakes < 1 || !loader.LoadCalled) && frames++ < 1200)
                yield return null;

            Assert.IsTrue(loader.LoadCalled, "The join must trigger a save load before it can be decided.");
            Assert.AreEqual(1, _gate.PendingHandshakes);
            Assert.IsFalse(IsClientAuthenticated(), "The host must stay parked while the save is loading.");

            for (int i = 0; i < 10; i++)
                yield return null;

            Assert.IsFalse(IsClientAuthenticated(), "A fresh handshake must not be admitted before its record lands.");
            Assert.AreEqual(1, _gate.PendingHandshakes, "Nothing reaped the handshake; it is simply waiting.");

            loader.Result = new PlayerSaveRecord { UserId = "deferred", Level = 12, Health = 90, Mana = 45 };
            loader.Release();

            yield return NetworkManagerTestHelper.WaitForHostAuthenticated(_manager);
            yield return NetworkManagerTestHelper.WaitForPlayerObject(_manager);

            PlayerStats stats = NetworkManagerTestHelper.FindPlayerObject(_manager).GetComponent<PlayerStats>();
            Assert.AreEqual(12, stats.Level, "The player must spawn from the loaded record, not defaults.");
            Assert.AreEqual(90, stats.Health);
            Assert.AreEqual(45, stats.Mana);
            Assert.AreEqual(0, _gate.PendingHandshakes);
        }

        [UnityTest]
        public IEnumerator SpawnAppliesTheLoadedSave_AndDisconnectPushesTheFinalState()
        {
            _repo = new InMemoryPlayerRepository();
            _service.Configure(_repo);

            string accountId = _manager.GetComponent<JoinRequestClient>().AccountId;
            _repo.Records[accountId] = new PlayerSaveRecord
            {
                UserId = accountId,
                Level = 9,
                Health = 90,
                Mana = 40,
            };

            NetworkManagerTestHelper.StartHost(_manager);
            yield return NetworkManagerTestHelper.WaitForHostAuthenticated(_manager);
            yield return NetworkManagerTestHelper.WaitForPlayerObject(_manager);

            Assert.AreSame(_service, _gate.PlayerLoader, "The factory must keep the service as the join loader.");

            PlayerStats stats = NetworkManagerTestHelper.FindPlayerObject(_manager).GetComponent<PlayerStats>();
            Assert.AreEqual(9, stats.Level, "The player must spawn with the database level, not the default.");
            Assert.AreEqual(90, stats.Health);
            Assert.AreEqual(40, stats.Mana);

            // Change the level and drop the connection without yielding a frame in between, so the
            // only possible writer of the new level is the disconnect's final snapshot.
            stats.SetLevel(14);
            _manager.ClientManager.StopConnection();

            yield return WaitForSavedLevel(accountId, 14);
        }

        private bool IsClientAuthenticated()
        {
            var connection = _manager.ClientManager.Connection;
            return connection != null && connection.IsAuthenticated;
        }

        private IEnumerator WaitForSavedLevel(string accountId, int level, int maxFrames = 600)
        {
            for (int i = 0; i < maxFrames; i++)
            {
                if (_repo.Records.TryGetValue(accountId, out PlayerSaveRecord record) &&
                    record != null && record.Level == level)
                {
                    yield break;
                }

                yield return null;
            }

            Assert.Fail($"A save with level {level} for '{accountId}' never reached the backend.");
        }
    }
}