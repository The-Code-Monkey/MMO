using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MMO.Net;
using MMO.Social;
using NUnit.Framework;

namespace MMO.Tests
{
    /// <summary>
    /// Tests for <see cref="RaidOrchestrationService"/> covering the fallback chain and error
    /// handling as specified in issue #61's test plan.
    /// </summary>
    public sealed class RaidOrchestrationServiceTests
    {
        private const double TimeoutSeconds = 90.0;

        private PartyRecord MakeParty(string partyId = "party-1", string leaderId = "leader-1")
        {
            return new PartyRecord
            {
                PartyId = partyId,
                LeaderId = leaderId,
                Members = new List<PartyMemberRecord>
                {
                    new PartyMemberRecord { AccountId = leaderId, PlayerName = "Leader", JoinedAtTicks = DateTime.UtcNow.Ticks },
                    new PartyMemberRecord { AccountId = "member-1", PlayerName = "Member", JoinedAtTicks = DateTime.UtcNow.Ticks },
                    new PartyMemberRecord { AccountId = "member-2", PlayerName = "Member2", JoinedAtTicks = DateTime.UtcNow.Ticks },
                    new PartyMemberRecord { AccountId = "member-3", PlayerName = "Member3", JoinedAtTicks = DateTime.UtcNow.Ticks },
                    new PartyMemberRecord { AccountId = "member-4", PlayerName = "Member4", JoinedAtTicks = DateTime.UtcNow.Ticks },
                }
            };
        }

        private FakeZoneRegistryRepository MakeZoneRegistry()
            => new FakeZoneRegistryRepository();

        private sealed class FakeZoneRegistryRepository : IZoneRegistryRepository
        {
            public List<ZoneRegistryRecord> SavedZones = new List<ZoneRegistryRecord>();
            public List<string> DeletedZones = new List<string>();
            public bool ThrowOnSave = false;
            public bool ThrowOnDelete = false;

            public Task<List<ZoneRegistryRecord>> LoadZonesAsync(CancellationToken ct = default)
                => Task.FromResult(new List<ZoneRegistryRecord>());

            public Task SaveZoneAsync(ZoneRegistryRecord record, CancellationToken ct = default)
            {
                if (ThrowOnSave)
                    throw new InvalidOperationException("Simulated save failure");
                SavedZones.Add(record);
                return Task.CompletedTask;
            }

            public Task DeleteZoneAsync(string serverId, CancellationToken ct = default)
            {
                if (ThrowOnDelete)
                    throw new InvalidOperationException("Simulated delete failure");
                DeletedZones.Add(serverId);
                return Task.CompletedTask;
            }
        }

        [Test]
        public async Task EnterRaid_OrchestratorDisabled_FallsBackToSelection()
        {
            var client = new FakeOrchestratorClient { Enabled = false };
            var zoneRegistry = MakeZoneRegistry();
            bool fallbackCalled = false;

            var service = new RaidOrchestrationService(
                client, zoneRegistry,
                async (party, ct) => { fallbackCalled = true; return (RaidRule.Ok, new RaidInstanceRecord { InstanceId = "fallback-instance" }); },
                TimeoutSeconds);

            var result = await service.EnterRaidAsync(MakeParty(), CancellationToken.None);

            Assert.AreEqual(RaidRule.Ok, result.Rule);
            Assert.IsTrue(fallbackCalled);
            Assert.IsFalse(client.Started);
        }

        [Test]
        public async Task EnterRaid_OrchestratorThrowsOnStart_FallsBackToSelection()
        {
            var client = new FakeOrchestratorClient { Enabled = true, ThrowOnStart = true };
            var zoneRegistry = MakeZoneRegistry();
            bool fallbackCalled = false;

            var service = new RaidOrchestrationService(
                client, zoneRegistry,
                async (party, ct) => { fallbackCalled = true; return (RaidRule.Ok, new RaidInstanceRecord { InstanceId = "fallback-instance" }); },
                TimeoutSeconds);

            var result = await service.EnterRaidAsync(MakeParty(), CancellationToken.None);

            Assert.AreEqual(RaidRule.Ok, result.Rule);
            Assert.IsTrue(fallbackCalled);
            Assert.IsTrue(client.Started);
        }

        [Test]
        public async Task EnterRaid_TimesOut_StopsTheContainerAndFallsBack()
        {
            var client = new FakeOrchestratorClient { Enabled = true, NeverHealthy = true };
            var zoneRegistry = MakeZoneRegistry();
            bool fallbackCalled = false;

            var service = new RaidOrchestrationService(
                client, zoneRegistry,
                async (party, ct) => { fallbackCalled = true; return (RaidRule.Ok, new RaidInstanceRecord { InstanceId = "fallback-instance" }); },
                healthTimeoutSeconds: 0); // instant timeout

            var result = await service.EnterRaidAsync(MakeParty(), CancellationToken.None);

            Assert.AreEqual(RaidRule.Ok, result.Rule);
            Assert.IsTrue(fallbackCalled);
            Assert.IsTrue(client.Started);
            Assert.IsTrue(client.Stopped);
        }

        [Test]
        public async Task EnterRaid_ContainerFails_StopsContainerAndFallsBack()
        {
            var client = new FakeOrchestratorClient { Enabled = true, FailImmediately = true };
            var zoneRegistry = MakeZoneRegistry();
            bool fallbackCalled = false;

            var service = new RaidOrchestrationService(
                client, zoneRegistry,
                async (party, ct) => { fallbackCalled = true; return (RaidRule.Ok, new RaidInstanceRecord { InstanceId = "fallback-instance" }); },
                TimeoutSeconds);

            var result = await service.EnterRaidAsync(MakeParty(), CancellationToken.None);

            Assert.AreEqual(RaidRule.Ok, result.Rule);
            Assert.IsTrue(fallbackCalled);
            Assert.IsTrue(client.Started);
            Assert.IsTrue(client.Stopped);
        }

        [Test]
        public async Task EnterRaid_Succeeds_RegistersZoneAndCallsFallback()
        {
            var client = new FakeOrchestratorClient { Enabled = true };
            var zoneRegistry = MakeZoneRegistry();
            bool fallbackCalled = false;

            var service = new RaidOrchestrationService(
                client, zoneRegistry,
                async (party, ct) => { fallbackCalled = true; return (RaidRule.Ok, new RaidInstanceRecord { InstanceId = "fallback-instance" }); },
                TimeoutSeconds);

            var result = await service.EnterRaidAsync(MakeParty(), CancellationToken.None);

            Assert.AreEqual(RaidRule.Ok, result.Rule);
            Assert.IsTrue(fallbackCalled);
            Assert.IsTrue(client.Started);
            Assert.IsFalse(client.Stopped);
            Assert.AreEqual(1, zoneRegistry.SavedZones.Count);
            Assert.IsTrue(zoneRegistry.SavedZones[0].ServerId.StartsWith("orchestrator-"));
            Assert.AreEqual("raid", zoneRegistry.SavedZones[0].ZoneTemplateId);
        }

        [Test]
        public async Task EnterRaid_ZoneRegistrationFails_StopsContainerAndFallsBack()
        {
            var client = new FakeOrchestratorClient { Enabled = true };
            var zoneRegistry = MakeZoneRegistry();
            zoneRegistry.ThrowOnSave = true;
            bool fallbackCalled = false;

            var service = new RaidOrchestrationService(
                client, zoneRegistry,
                async (party, ct) => { fallbackCalled = true; return (RaidRule.Ok, new RaidInstanceRecord { InstanceId = "fallback-instance" }); },
                TimeoutSeconds);

            var result = await service.EnterRaidAsync(MakeParty(), CancellationToken.None);

            Assert.AreEqual(RaidRule.Ok, result.Rule);
            Assert.IsTrue(fallbackCalled);
            Assert.IsTrue(client.Started);
            Assert.IsTrue(client.Stopped);
        }

        [Test]
        public async Task EnterRaid_NullParty_ReturnsNoParty()
        {
            var client = new FakeOrchestratorClient { Enabled = true };
            var zoneRegistry = MakeZoneRegistry();

            var service = new RaidOrchestrationService(
                client, zoneRegistry,
                async (party, ct) => (RaidRule.Ok, new RaidInstanceRecord()),
                TimeoutSeconds);

            var result = await service.EnterRaidAsync(null, CancellationToken.None);

            Assert.AreEqual(RaidRule.NoParty, result.Rule);
            Assert.IsNull(result.Instance);
        }

        [Test]
        public async Task TeardownAsync_StopsContainerAndDeregistersZone()
        {
            var client = new FakeOrchestratorClient { Enabled = true };
            var zoneRegistry = MakeZoneRegistry();
            string instanceId = "test-instance-123";

            var service = new RaidOrchestrationService(
                client, zoneRegistry,
                async (party, ct) => (RaidRule.Ok, new RaidInstanceRecord()),
                TimeoutSeconds);

            await service.TeardownAsync(instanceId, CancellationToken.None);

            Assert.IsTrue(client.Stopped);
            Assert.AreEqual(1, zoneRegistry.DeletedZones.Count);
            Assert.AreEqual($"orchestrator-{instanceId}", zoneRegistry.DeletedZones[0]);
        }

        [Test]
        public async Task TeardownAsync_EmptyInstanceId_IsNoOp()
        {
            var client = new FakeOrchestratorClient { Enabled = true };
            var zoneRegistry = MakeZoneRegistry();

            var service = new RaidOrchestrationService(
                client, zoneRegistry,
                async (party, ct) => (RaidRule.Ok, new RaidInstanceRecord()),
                TimeoutSeconds);

            await service.TeardownAsync("", CancellationToken.None);
            await service.TeardownAsync(null, CancellationToken.None);

            Assert.IsFalse(client.Stopped);
            Assert.AreEqual(0, zoneRegistry.DeletedZones.Count);
        }
    }
}