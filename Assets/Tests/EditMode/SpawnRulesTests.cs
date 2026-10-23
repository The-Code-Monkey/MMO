using MMO.Content;
using NUnit.Framework;

namespace MMO.Tests
{
    /// <summary>
    /// The pure spawn-controller arithmetic: target population from nearby players, clamped to
    /// the authored range, and the respawn timer.
    /// </summary>
    public sealed class SpawnRulesTests
    {
        [Test]
        public void TargetAlive_ClampsToTheAuthoredRange()
        {
            Assert.AreEqual(10, SpawnRules.TargetAlive(minAlive: 2, maxAlive: 10, playersNear: 50, perPlayer: 3));
        }

        [Test]
        public void TargetAlive_IsZeroWhenNoPlayersAreNear()
        {
            Assert.AreEqual(0, SpawnRules.TargetAlive(minAlive: 2, maxAlive: 10, playersNear: 0, perPlayer: 3),
                "An empty zone must not keep a minimum population alive.");
        }

        [Test]
        public void TargetAlive_KeepsTheMinimumWhenAtLeastOnePlayerIsNear()
        {
            Assert.AreEqual(2, SpawnRules.TargetAlive(minAlive: 2, maxAlive: 10, playersNear: 1, perPlayer: 1));
        }

        [Test]
        public void TargetAlive_ToleratesNegativeAuthoring()
        {
            Assert.AreEqual(0, SpawnRules.TargetAlive(minAlive: -5, maxAlive: -1, playersNear: 1, perPlayer: 1));
            Assert.AreEqual(0, SpawnRules.TargetAlive(minAlive: 0, maxAlive: 5, playersNear: 1, perPlayer: -3));
        }

        [Test]
        public void IsRespawnDue_OnlyAfterTheTimer()
        {
            Assert.IsFalse(SpawnRules.IsRespawnDue(lastDeathAt: 0d, respawnSeconds: 30d, now: 29d));
            Assert.IsTrue(SpawnRules.IsRespawnDue(lastDeathAt: 0d, respawnSeconds: 30d, now: 30d));
            Assert.IsTrue(SpawnRules.IsRespawnDue(lastDeathAt: 0d, respawnSeconds: 30d, now: 31d));
        }

        [Test]
        public void IsRespawnDue_ZeroOrNegativeDelayIsImmediate()
        {
            Assert.IsTrue(SpawnRules.IsRespawnDue(lastDeathAt: 10d, respawnSeconds: 0d, now: 10d));
            Assert.IsTrue(SpawnRules.IsRespawnDue(lastDeathAt: 10d, respawnSeconds: -5d, now: 10d));
        }
    }
}
