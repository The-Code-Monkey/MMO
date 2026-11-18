using System;
using MMO.Gameplay;
using NUnit.Framework;

namespace MMO.Tests
{
    /// <summary>
    /// The PvP verdict as arithmetic: who may harm whom per tier, when a hit is itself consent,
    /// and how long the opt-out window lasts. No network, no scene and no spawned player — the
    /// whole table is pinned here so the shell only has to pass the right three values in.
    /// </summary>
    public sealed class PvPRulesTests
    {
        [Test]
        public void CanHarm_Safe_RefusesEvenWhenBothPlayersAreFlagged()
        {
            Assert.IsFalse(PvPRules.CanHarm(ZoneRiskTier.Safe, attackerFlagged: true, targetFlagged: true));
            Assert.IsFalse(PvPRules.CanHarm(ZoneRiskTier.Safe, attackerFlagged: true, targetFlagged: false));
            Assert.IsFalse(PvPRules.CanHarm(ZoneRiskTier.Safe, attackerFlagged: false, targetFlagged: true));
            Assert.IsFalse(PvPRules.CanHarm(ZoneRiskTier.Safe, attackerFlagged: false, targetFlagged: false));
        }

        [Test]
        public void CanHarm_Yellow_RequiresTheTargetToBeFlagged()
        {
            Assert.IsTrue(PvPRules.CanHarm(ZoneRiskTier.Yellow, attackerFlagged: true, targetFlagged: true));
            Assert.IsTrue(PvPRules.CanHarm(ZoneRiskTier.Yellow, attackerFlagged: false, targetFlagged: true),
                "An unflagged attacker may strike a flagged target; the hit then flags them.");
            Assert.IsFalse(PvPRules.CanHarm(ZoneRiskTier.Yellow, attackerFlagged: true, targetFlagged: false),
                "A flagged player still cannot touch someone who never opted in.");
            Assert.IsFalse(PvPRules.CanHarm(ZoneRiskTier.Yellow, attackerFlagged: false, targetFlagged: false));
        }

        [Test]
        public void CanHarm_ForcedTiers_IgnoreBothFlags()
        {
            foreach (ZoneRiskTier tier in new[] { ZoneRiskTier.Red, ZoneRiskTier.Black })
            {
                Assert.IsTrue(PvPRules.CanHarm(tier, true, true), tier.ToString());
                Assert.IsTrue(PvPRules.CanHarm(tier, true, false), tier.ToString());
                Assert.IsTrue(PvPRules.CanHarm(tier, false, true), tier.ToString());
                Assert.IsTrue(PvPRules.CanHarm(tier, false, false), tier.ToString());
            }
        }

        [Test]
        public void AutoFlags_OnlyAnUnflaggedAttackerHittingAFlaggedTargetInYellow()
        {
            Assert.IsTrue(PvPRules.AutoFlags(ZoneRiskTier.Yellow, attackerFlagged: false, targetFlagged: true));
            Assert.IsFalse(PvPRules.AutoFlags(ZoneRiskTier.Yellow, attackerFlagged: true, targetFlagged: true));
            Assert.IsFalse(PvPRules.AutoFlags(ZoneRiskTier.Yellow, attackerFlagged: false, targetFlagged: false));
            Assert.IsFalse(PvPRules.AutoFlags(ZoneRiskTier.Safe, false, true));
            Assert.IsFalse(PvPRules.AutoFlags(ZoneRiskTier.Red, false, true));
            Assert.IsFalse(PvPRules.AutoFlags(ZoneRiskTier.Black, false, true));
        }

        [Test]
        public void IsForced_TrueForTheTwoFullRiskTiersOnly()
        {
            Assert.IsFalse(PvPRules.IsForced(ZoneRiskTier.Safe));
            Assert.IsFalse(PvPRules.IsForced(ZoneRiskTier.Yellow));
            Assert.IsTrue(PvPRules.IsForced(ZoneRiskTier.Red));
            Assert.IsTrue(PvPRules.IsForced(ZoneRiskTier.Black));
        }

        [Test]
        public void EveryDeclaredTier_HasARowInEveryRule()
        {
            foreach (ZoneRiskTier tier in Enum.GetValues(typeof(ZoneRiskTier)))
            {
                Assert.DoesNotThrow(() => PvPRules.CanHarm(tier, true, true), tier.ToString());
                Assert.DoesNotThrow(() => PvPRules.AutoFlags(tier, false, true), tier.ToString());
                Assert.DoesNotThrow(() => PvPRules.IsForced(tier), tier.ToString());
            }
        }

        [Test]
        public void IsOptOutReady_BoundaryIsExactlyFiveMinutes()
        {
            const double hit = 1000d;

            Assert.IsFalse(PvPRules.IsOptOutReady(hit + 299.999d, hit));
            Assert.IsTrue(PvPRules.IsOptOutReady(hit + PvPRules.OptOutCooldownSeconds, hit));
            Assert.IsTrue(PvPRules.IsOptOutReady(hit + 3600d, hit));
        }

        [Test]
        public void IsOptOutReady_NeverHit_IsAlwaysReady()
        {
            Assert.IsTrue(PvPRules.IsOptOutReady(0d, double.NegativeInfinity));
            Assert.IsTrue(PvPRules.IsOptOutReady(1234d, double.NegativeInfinity));
            Assert.IsFalse(PvPRules.IsOptOutReady(double.NaN, 0d));
        }

        [Test]
        public void RemainingOptOut_ClampsToTheWindow()
        {
            const double hit = 500d;

            Assert.AreEqual(PvPRules.OptOutCooldownSeconds, PvPRules.RemainingOptOut(hit, hit), 0.0001d);
            Assert.AreEqual(200d, PvPRules.RemainingOptOut(hit + 100d, hit), 0.0001d);
            Assert.AreEqual(0d, PvPRules.RemainingOptOut(hit + 500d, hit), 0.0001d);
            Assert.AreEqual(0d, PvPRules.RemainingOptOut(0d, double.NegativeInfinity), 0.0001d);
        }

        [Test]
        public void RemainingOptOut_ClockGoingBackwards_DoesNotExceedTheWindow()
        {
            Assert.AreEqual(
                PvPRules.OptOutCooldownSeconds,
                PvPRules.RemainingOptOut(now: 400d, lastPlayerHarmAt: 500d),
                0.0001d);
        }

        [Test]
        public void ResolveFlagRequest_ForcedZone_RefusesBothDirections()
        {
            foreach (ZoneRiskTier tier in new[] { ZoneRiskTier.Red, ZoneRiskTier.Black })
            {
                Assert.AreEqual(PvPRequestResult.ForcedZone,
                    PvPRules.ResolveFlagRequest(tier, currentlyFlagged: false, requestedFlagged: true, 0d, 0d));
                Assert.AreEqual(PvPRequestResult.ForcedZone,
                    PvPRules.ResolveFlagRequest(tier, currentlyFlagged: true, requestedFlagged: false, 1000d, 0d));
            }
        }

        [Test]
        public void ResolveFlagRequest_NoOp_ReportsAlreadySet()
        {
            Assert.AreEqual(PvPRequestResult.AlreadySet,
                PvPRules.ResolveFlagRequest(ZoneRiskTier.Yellow, true, true, 0d, 0d));
            Assert.AreEqual(PvPRequestResult.AlreadySet,
                PvPRules.ResolveFlagRequest(ZoneRiskTier.Safe, false, false, 0d, 0d));
        }

        [Test]
        public void ResolveFlagRequest_OptIn_IsAcceptedImmediately()
        {
            Assert.AreEqual(PvPRequestResult.Accepted,
                PvPRules.ResolveFlagRequest(ZoneRiskTier.Safe, false, true, now: 0d, lastPlayerHarmAt: 0d));
        }

        [Test]
        public void ResolveFlagRequest_OptOut_WaitsForTheCooldown()
        {
            const double hit = 2000d;

            Assert.AreEqual(PvPRequestResult.Cooldown,
                PvPRules.ResolveFlagRequest(ZoneRiskTier.Yellow, true, false, hit + 10d, hit));
            Assert.AreEqual(PvPRequestResult.Accepted,
                PvPRules.ResolveFlagRequest(ZoneRiskTier.Yellow, true, false, hit + 300d, hit));
        }
    }
}
