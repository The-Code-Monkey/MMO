using System;
using System.Collections.Generic;
using MMO.Social;
using NUnit.Framework;

namespace MMO.Tests
{
    /// <summary>
    /// The raid host-selection policy (#57): a pure function over the zone registry. These pin the
    /// freshness, headroom, lowest-load and deterministic tie-break rules, plus the empty-candidate
    /// case that must surface as <see cref="RaidHostChoice.NoCandidate"/> rather than a silent pick.
    /// </summary>
    public sealed class RaidHostSelectionTests
    {
        private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        private static ZoneRegistryRecord Zone(
            string serverId, int activeInstances = 0, int maxInstances = 10, bool fresh = true,
            string host = null, ushort port = 7000, string template = "raid")
            => new ZoneRegistryRecord
            {
                ServerId = serverId,
                Host = host ?? serverId + ".host",
                Port = port,
                ZoneTemplateId = template,
                MaxInstances = maxInstances,
                ActiveInstances = activeInstances,
                HeartbeatAtTicks = (fresh ? Now : Now.AddHours(-1)).UtcDateTime.Ticks,
            };

        [Test]
        public void Choose_PicksLowestLoadHealthyZone()
        {
            var zones = new[] { Zone("a", activeInstances: 3), Zone("b", activeInstances: 1) };

            Assert.AreEqual(RaidHostChoice.Ok, RaidHostSelection.Choose(zones, Now, out ZoneRegistryRecord chosen));
            Assert.AreEqual("b", chosen.ServerId);
        }

        [Test]
        public void Choose_IgnoresStaleHeartbeats()
        {
            var zones = new[] { Zone("a", activeInstances: 0, fresh: false) };

            Assert.AreEqual(RaidHostChoice.NoCandidate, RaidHostSelection.Choose(zones, Now, out ZoneRegistryRecord chosen));
            Assert.IsNull(chosen);
        }

        [Test]
        public void Choose_IgnoresZonesAtCapacity()
        {
            var zones = new[] { Zone("a", activeInstances: 5, maxInstances: 5) };

            Assert.AreEqual(RaidHostChoice.NoCandidate, RaidHostSelection.Choose(zones, Now, out _));
        }

        [Test]
        public void Choose_IgnoresZonesOverCapacity()
        {
            var zones = new[] { Zone("a", activeInstances: 7, maxInstances: 5) };

            Assert.AreEqual(RaidHostChoice.NoCandidate, RaidHostSelection.Choose(zones, Now, out _));
        }

        [Test]
        public void Choose_TieBreaksOnServerId_SoSelectionIsDeterministic()
        {
            var zones = new[] { Zone("b", activeInstances: 2), Zone("a", activeInstances: 2) };

            Assert.AreEqual(RaidHostChoice.Ok, RaidHostSelection.Choose(zones, Now, out ZoneRegistryRecord chosen));
            Assert.AreEqual("a", chosen.ServerId);
        }

        [Test]
        public void Choose_SkipsStaleAndFullZonesToFindAHealthyOne()
        {
            var zones = new[]
            {
                Zone("stale", fresh: false),
                Zone("full", activeInstances: 4, maxInstances: 4),
                Zone("ok", activeInstances: 2),
            };

            Assert.AreEqual(RaidHostChoice.Ok, RaidHostSelection.Choose(zones, Now, out ZoneRegistryRecord chosen));
            Assert.AreEqual("ok", chosen.ServerId);
        }

        [Test]
        public void Choose_EmptyRegistry_IsNoCandidate()
        {
            Assert.AreEqual(
                RaidHostChoice.NoCandidate,
                RaidHostSelection.Choose(Array.Empty<ZoneRegistryRecord>(), Now, out _));
        }

        [Test]
        public void Choose_NullRegistry_IsNoCandidate()
        {
            Assert.AreEqual(RaidHostChoice.NoCandidate, RaidHostSelection.Choose(null, Now, out _));
        }

        [Test]
        public void Choose_IgnoresNullEntriesAndPreservesTheHostDetails()
        {
            var zones = new List<ZoneRegistryRecord>
            {
                null,
                Zone("z9", activeInstances: 0, host: "10.0.0.9", port: 7999, template: "arena"),
            };

            Assert.AreEqual(RaidHostChoice.Ok, RaidHostSelection.Choose(zones, Now, out ZoneRegistryRecord chosen));
            Assert.AreEqual("z9", chosen.ServerId);
            Assert.AreEqual("10.0.0.9", chosen.Host);
            Assert.AreEqual((ushort)7999, chosen.Port);
            Assert.AreEqual("arena", chosen.ZoneTemplateId);
        }

        [Test]
        public void Headroom_ClampsAtZeroWhenOverCapacity()
        {
            Assert.AreEqual(0, Zone("a", activeInstances: 9, maxInstances: 5).Headroom);
            Assert.AreEqual(2, Zone("a", activeInstances: 3, maxInstances: 5).Headroom);
        }

        [Test]
        public void IsHeartbeatFresh_HandlesMissingFutureAndExpiredHeartbeats()
        {
            ZoneRegistryRecord zone = Zone("a");
            Assert.IsTrue(zone.IsHeartbeatFresh(Now));

            zone.HeartbeatAtTicks = 0;
            Assert.IsFalse(zone.IsHeartbeatFresh(Now), "A never-heartbeated zone is not fresh.");

            zone.HeartbeatAtTicks = Now.AddSeconds(30).UtcDateTime.Ticks;
            Assert.IsFalse(zone.IsHeartbeatFresh(Now), "A future-stamped heartbeat has a negative age and is rejected.");

            zone.HeartbeatAtTicks = Now.Subtract(ZoneRegistryRecord.HeartbeatTtl).AddSeconds(-1).UtcDateTime.Ticks;
            Assert.IsFalse(zone.IsHeartbeatFresh(Now), "An expired heartbeat drops the zone out of selection.");
        }
    }
}
