using MMO.Gameplay;
using NUnit.Framework;

namespace MMO.Tests
{
    public sealed class PlayerStatsTests
    {
        [Test]
        public void ComputeNewHealth_ReducesHealthByAmount()
        {
            Assert.AreEqual(70, PlayerStats.ComputeNewHealth(100, 30));
        }

        [Test]
        public void ComputeNewHealth_ClampsAtZero()
        {
            Assert.AreEqual(0, PlayerStats.ComputeNewHealth(50, 999));
        }

        [Test]
        public void ComputeNewHealth_ClampsAtMax()
        {
            Assert.AreEqual(PlayerStats.MaxHealth, PlayerStats.ComputeNewHealth(PlayerStats.MaxHealth, 0));
        }

        [Test]
        public void ComputeNewHealth_NegativeAmountDoesNotHeal()
        {
            Assert.AreEqual(50, PlayerStats.ComputeNewHealth(50, -10));
        }

        [Test]
        public void ComputeNewHealth_ZeroDamageKeepsHealth()
        {
            Assert.AreEqual(42, PlayerStats.ComputeNewHealth(42, 0));
        }

        [Test]
        public void MaxHealth_IsOneHundred()
        {
            Assert.AreEqual(100, PlayerStats.MaxHealth);
        }
    }
}
