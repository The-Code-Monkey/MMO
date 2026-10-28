using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MMO.Persistence;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace MMO.Tests
{
    /// <summary>
    /// The save loop in isolation: a service on the scene and an in-memory repository, with
    /// <see cref="PlayerPersistenceService.Configure"/> proving a fake needs no backend. Debounce,
    /// final-record priority and failure-retry are all driven with an explicit clock through
    /// <see cref="PlayerPersistenceService.ProcessDueSavesAsync"/> instead of waiting real frames.
    /// </summary>
    public sealed class PlayerPersistenceServiceTests
    {
        private GameObject _go;
        private PlayerPersistenceService _service;
        private InMemoryPlayerRepository _repo;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("PersistenceServiceUnderTest");
            _service = _go.AddComponent<PlayerPersistenceService>();
            _repo = new InMemoryPlayerRepository();
            _service.Configure(_repo);
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null)
                Object.DestroyImmediate(_go);
        }

        [Test]
        public async Task Configure_Fake_LoadsWithoutAConnection()
        {
            Assert.IsNull(await _service.LoadAsync("never-saved", CancellationToken.None));
        }

        [Test]
        public async Task LoadAsync_ReturnsTheSeededRecord()
        {
            _repo.Records["u-1"] = new PlayerSaveRecord { UserId = "u-1", Level = 5 };

            PlayerSaveRecord loaded = await _service.LoadAsync("u-1", CancellationToken.None);

            Assert.IsNotNull(loaded);
            Assert.AreEqual(5, loaded.Level);
        }

        [Test]
        public async Task LoadAsync_Unconnected_FailsOpenToAFreshPlayer()
        {
            var unconfigured = new GameObject("UnconnectedService").AddComponent<PlayerPersistenceService>();

            PlayerSaveRecord loaded = await unconfigured.LoadAsync("u-1", CancellationToken.None);

            Assert.IsNull(loaded, "A downed backend must read as a fresh player, not a refused join.");

            Object.DestroyImmediate(unconfigured.gameObject);
        }

        [Test]
        public async Task ImmediateDirty_IsDueRightAway()
        {
            _service.RegisterLivePlayer("u-1", Capture("u-1", 9));
            _service.MarkDirty("u-1", immediate: true);

            double now = Time.unscaledTimeAsDouble;
            int flushed = await _service.ProcessDueSavesAsync(now + 2d);

            Assert.AreEqual(1, flushed);
            Assert.AreEqual(9, _repo.Records["u-1"].Level);
        }

        [Test]
        public async Task DebouncedDirty_IsWithheldUntilItsDeadline()
        {
            _service.RegisterLivePlayer("u-1", Capture("u-1", 1));
            _service.MarkDirty("u-1", immediate: false);

            double now = Time.unscaledTimeAsDouble;
            Assert.AreEqual(0, await _service.ProcessDueSavesAsync(now + 1d), "Inside the debounce window nothing writes.");
            Assert.AreEqual(1, await _service.ProcessDueSavesAsync(now + 60d), "After the window the burst flushes once.");
            Assert.AreEqual(1, _repo.Records["u-1"].Level);
        }

        [Test]
        public async Task FlushAsync_PersistsEveryDirtyPlayer()
        {
            _service.RegisterLivePlayer("u-1", Capture("u-1", 3));
            _service.RegisterLivePlayer("u-2", Capture("u-2", 4));
            _service.MarkDirty("u-1", immediate: true);
            _service.MarkDirty("u-2", immediate: true);

            int flushed = await _service.FlushAsync();

            Assert.AreEqual(2, flushed);
            Assert.AreEqual(3, _repo.Records["u-1"].Level);
            Assert.AreEqual(4, _repo.Records["u-2"].Level);
            Assert.AreEqual(0, await _service.FlushAsync(), "A clean queue saves nothing.");
        }

        [Test]
        public async Task QueueFinalSave_TakesPriorityOverTheLiveCapture()
        {
            _service.RegisterLivePlayer("u-1", Capture("u-1", 1));
            _service.QueueFinalSave("u-1", new PlayerSaveRecord { UserId = "u-1", Level = 9 });

            double now = Time.unscaledTimeAsDouble;
            Assert.AreEqual(1, await _service.ProcessDueSavesAsync(now + 2d));
            Assert.AreEqual(9, _repo.Records["u-1"].Level, "The disconnect snapshot, not the live capture, wins the last write.");
            Assert.AreEqual(0, await _service.ProcessDueSavesAsync(now + 4d), "The final record is one-shot.");
        }

        [Test]
        public async Task FailedSave_KeepsThePlayerDirty_ForLaterRetries()
        {
            _repo.ThrowOnSave = true;
            _service.RegisterLivePlayer("u-1", Capture("u-1", 4));
            _service.MarkDirty("u-1", immediate: true);

            double now = Time.unscaledTimeAsDouble;
            LogAssert.Expect(LogType.Error, "[Persistence] Failed to save 'u-1': injected save failure");
            Assert.AreEqual(0, await _service.ProcessDueSavesAsync(now + 2d), "A failed write is not counted as flushed.");
            LogAssert.Expect(LogType.Error, "[Persistence] Failed to save 'u-1': injected save failure");
            Assert.AreEqual(0, await _service.ProcessDueSavesAsync(now + 4d), "Still dirty, still re-debounced, still retried.");

            _repo.ThrowOnSave = false;
            Assert.AreEqual(1, await _service.FlushAsync());
            Assert.AreEqual(4, _repo.Records["u-1"].Level);
        }

        [Test]
        public void RegisterLivePlayer_NullCapture_Throws()
        {
            Assert.That(() => _service.RegisterLivePlayer("u-1", null), Throws.ArgumentNullException);
        }

        [Test]
        public async Task FlushAsync_ForOnePlayer_SavesItNow_IgnoringTheDebounceWindow()
        {
            _service.RegisterLivePlayer("u-1", Capture("u-1", 5));
            _service.RegisterLivePlayer("u-2", Capture("u-2", 6));
            _service.MarkDirty("u-1", immediate: false);

            bool saved = await _service.FlushAsync("u-1");

            Assert.IsTrue(saved);
            Assert.AreEqual(5, _repo.Records["u-1"].Level, "The flush ignores the debounce window.");
            Assert.IsFalse(_repo.Records.ContainsKey("u-2"), "Only the handed-off player is saved, not everyone.");
        }

        [Test]
        public async Task FlushAsync_ForOnePlayer_UsesACapturedNowSnapshot()
        {
            int captures = 0;
            _service.RegisterLivePlayer("u-1", () => { captures++; return new PlayerSaveRecord { UserId = "u-1", Level = 11 }; });

            await _service.FlushAsync("u-1");

            Assert.AreEqual(11, _repo.Records["u-1"].Level);
            Assert.AreEqual(1, captures, "Exactly one snapshot for one forced save.");
        }

        [Test]
        public async Task FlushAsync_ForOnePlayer_RequireConnected_PersistsReservedGoldBeforeReturning()
        {
            long liveGold = 500;
            _service.RegisterLivePlayer("u-1", () => new PlayerSaveRecord { UserId = "u-1", Gold = liveGold });
            liveGold -= 125;

            bool saved = await _service.FlushAsync("u-1", requireConnected: true);

            Assert.IsTrue(saved);
            Assert.AreEqual(375L, _repo.Records["u-1"].Gold);
        }

        [Test]
        public async Task FlushAsync_ForOnePlayer_RequireConnected_FailsWhenBackendIsUnavailable()
        {
            var unconnected = new GameObject("UnconnectedService").AddComponent<PlayerPersistenceService>();

            bool saved = await unconnected.FlushAsync("u-1", requireConnected: true);

            Assert.IsFalse(saved, "A reserved wallet must not be treated as durable without a connected repository.");
            Object.DestroyImmediate(unconnected.gameObject);
        }

        [Test]
        public async Task FlushAsync_ForOnePlayer_RequireConnected_FailsWithoutLiveSnapshot()
        {
            bool saved = await _service.FlushAsync("u-1", requireConnected: true);

            Assert.IsFalse(saved, "A missing live capture cannot prove the reserved wallet was saved.");
        }

        [Test]
        public async Task AddWalletReservationAsync_PersistsReservationWithCurrentWallet()
        {
            const long reservedGold = 375;
            _service.RegisterLivePlayer("u-1", () => new PlayerSaveRecord { UserId = "u-1", Gold = reservedGold });

            bool saved = await _service.AddWalletReservationAsync("u-1", new WalletReservationRecord
            {
                ReservationId = "attempt-1",
                TargetId = "listing-1",
                Kind = WalletReservationKind.AuctionPurchase,
                Amount = 125,
            });

            Assert.IsTrue(saved);
            Assert.AreEqual(reservedGold, _repo.Records["u-1"].Gold);
            Assert.AreEqual(1, _repo.Records["u-1"].PendingWalletReservations.Count);
            Assert.AreEqual("attempt-1", _repo.Records["u-1"].PendingWalletReservations[0].ReservationId);
        }

        [Test]
        public async Task CompleteWalletReservationAsync_PersistsClearedReservation()
        {
            _service.RegisterLivePlayer("u-1", () => new PlayerSaveRecord { UserId = "u-1", Gold = 375 });
            await _service.AddWalletReservationAsync("u-1", new WalletReservationRecord
            {
                ReservationId = "attempt-1",
                TargetId = "listing-1",
                Kind = WalletReservationKind.AuctionPurchase,
                Amount = 125,
            });

            bool saved = await _service.CompleteWalletReservationAsync("u-1", "attempt-1");

            Assert.IsTrue(saved);
            Assert.AreEqual(0, _repo.Records["u-1"].PendingWalletReservations.Count);
        }

        [Test]
        public async Task FlushAsync_ForOnePlayer_FailedWrite_ReturnsFalseAndStaysDirty()
        {
            _repo.ThrowOnSave = true;
            _service.RegisterLivePlayer("u-1", Capture("u-1", 4));

            LogAssert.Expect(LogType.Error, "[Persistence] Failed to save 'u-1': injected save failure");
            bool saved = await _service.FlushAsync("u-1", requireConnected: true);
            Assert.IsFalse(saved, "A durable reserve must know the save did not land.");
            LogAssert.Expect(LogType.Error, "[Persistence] Failed to save 'u-1': injected save failure");
            Assert.AreEqual(0, await _service.FlushAsync(), "Still dirty, still failing.");
            Assert.IsFalse(_repo.Records.ContainsKey("u-1"));

            _repo.ThrowOnSave = false;
            Assert.AreEqual(1, await _service.FlushAsync());
            Assert.AreEqual(4, _repo.Records["u-1"].Level);
        }

        [Test]
        public async Task FlushAsync_UnknownOrBlankAccount_IsAnEmptySuccess()
        {
            Assert.IsTrue(await _service.FlushAsync("never-registered"));
            Assert.IsTrue(await _service.FlushAsync(null));
            Assert.IsTrue(await _service.FlushAsync(" "));
        }

        [Test]
        public async Task FlushAsync_Unconnected_FailsOpenToDone()
        {
            var unconnected = new GameObject("UnconnectedService").AddComponent<PlayerPersistenceService>();

            bool saved = await unconnected.FlushAsync("u-1");

            Assert.IsTrue(saved, "A downed backend must read as done, matching the fresh-player load.");
            Object.DestroyImmediate(unconnected.gameObject);
        }

        private static Func<PlayerSaveRecord> Capture(string userId, int level)
            => () => new PlayerSaveRecord { UserId = userId, Level = level };

        private sealed class InMemoryPlayerRepository : IPlayerRepository
        {
            public readonly Dictionary<string, PlayerSaveRecord> Records = new();
            public bool ThrowOnSave;

            public Task<PlayerSaveRecord> LoadAsync(string userId, CancellationToken ct)
                => Task.FromResult(Records.TryGetValue(userId, out PlayerSaveRecord record) ? record : null);

            public Task SaveAsync(PlayerSaveRecord record, CancellationToken ct)
            {
                if (ThrowOnSave)
                    return Task.FromException(new InvalidOperationException("injected save failure"));

                Records[record.UserId] = record;
                return Task.CompletedTask;
            }
        }
    }
}
