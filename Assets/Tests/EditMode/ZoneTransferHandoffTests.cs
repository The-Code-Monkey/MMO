using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MMO.Net;
using NUnit.Framework;

namespace MMO.Tests
{
    /// <summary>
    /// The handoff sequence in isolation: the save must land before the token is minted, a failed
    /// save aborts the handoff (no token, no redirect), and the redirect carries the destination
    /// and a token bound to the account being saved.
    /// </summary>
    public sealed class ZoneTransferHandoffTests
    {
        private sealed class RecordingStore : IZoneTransferTokenStore
        {
            public readonly List<string> Calls = new();
            public int IssueCount;

            public Task<string> IssueAsync(ZoneTransferToken token, CancellationToken ct)
            {
                IssueCount++;
                Calls.Add("issue:" + token.AccountId);
                return Task.FromResult("issued-token");
            }

            public Task<ZoneTransferToken> ConsumeAsync(string tokenString, CancellationToken ct)
                => Task.FromResult<ZoneTransferToken>(null);
        }

        private sealed class SaveTracker
        {
            public readonly List<string> Calls = new();
            public bool Succeed = true;

            public Task<bool> ForceSaveAsync(string accountId)
            {
                Calls.Add(accountId);
                return Task.FromResult(Succeed);
            }
        }

        private RecordingStore _store;
        private SaveTracker _save;

        private ZoneTransferHandoff NewHandoff()
            => new ZoneTransferHandoff(_store, "10.0.0.2", 7771, "server-b", "server-a", _save.ForceSaveAsync);

        [SetUp]
        public void SetUp()
        {
            _store = new RecordingStore();
            _save = new SaveTracker();
        }

        [Test]
        public async Task RunAsync_SavesBeforeIssuing()
        {
            ZoneTransferBroadcast? redirect = await NewHandoff().RunAsync("acc-1", "Entering Westtown");

            Assert.IsNotNull(redirect);
            CollectionAssert.AreEqual(new[] { "acc-1" }, _save.Calls, "One save, once.");
            CollectionAssert.AreEqual(new[] { "issue:acc-1" }, _store.Calls, "The token is minted only after the player is durable.");
            Assert.AreEqual(1, _store.IssueCount);
        }

        [Test]
        public async Task RunAsync_FailedSave_AbortsWithoutAToken()
        {
            _save.Succeed = false;

            ZoneTransferBroadcast? redirect = await NewHandoff().RunAsync("acc-1", "Entering Westtown");

            Assert.IsNull(redirect, "A player whose save did not land must not be handed off.");
            Assert.AreEqual(0, _store.IssueCount);
        }

        [Test]
        public async Task RunAsync_BuildsARedirectToTheTargetCarryingTheSavedAccount()
        {
            ZoneTransferBroadcast? redirect = await NewHandoff().RunAsync("acc-9");

            Assert.IsTrue(redirect.HasValue);
            Assert.AreEqual("10.0.0.2", redirect.Value.TargetHost);
            Assert.AreEqual(7771, redirect.Value.TargetPort);
            Assert.AreEqual("server-b", redirect.Value.TargetServerId);
            Assert.AreEqual("issued-token", redirect.Value.TransferToken);
            Assert.AreEqual(ZoneTransferHandoff.DefaultReason, redirect.Value.Reason);
            Assert.IsTrue(redirect.Value.IsValid);
        }

        [Test]
        public async Task RunAsync_NullOrBlankAccount_NeverSavesOrIssues()
        {
            ZoneTransferHandoff handoff = NewHandoff();

            Assert.IsNull(await handoff.RunAsync(null));
            Assert.IsNull(await handoff.RunAsync("   "));
            Assert.IsEmpty(_save.Calls);
            Assert.AreEqual(0, _store.IssueCount);
        }

        [Test]
        public async Task RunAsync_TokenIssued_IsConsumableAgainstTheSameStore_AndBindsTheAccount()
        {
            var realStore = new ZoneTransferTokenStore("secret", clock: () => DateTime.UtcNow);
            var handoff = new ZoneTransferHandoff(realStore, "10.0.0.2", 7771, "server-b", "server-a", _save.ForceSaveAsync);

            ZoneTransferBroadcast? redirect = await handoff.RunAsync("acc-zone-handoff");

            ZoneTransferToken consumed = await realStore.ConsumeAsync(redirect.Value.TransferToken, CancellationToken.None);

            Assert.IsNotNull(consumed);
            Assert.AreEqual("acc-zone-handoff", consumed.AccountId);
            Assert.AreEqual("10.0.0.2", consumed.TargetHost);
            Assert.AreEqual(7771, consumed.TargetPort);
            Assert.AreEqual("server-b", consumed.TargetServerId);
            Assert.AreEqual("server-a", consumed.SourceServerId);
        }
    }
}