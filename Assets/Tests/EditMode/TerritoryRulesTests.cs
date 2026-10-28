using System;
using MMO.Gameplay;
using MMO.Social;
using NUnit.Framework;
using UnityEngine;

namespace MMO.Tests.EditMode
{
    public sealed class TerritoryRulesTests
    {
        private static readonly VulnerabilityWindow SaturdayEvening = new()
        {
            Days = new[] { DayOfWeek.Saturday },
            StartMinuteUtc = 20 * 60,
            EndMinuteUtc = 22 * 60,
        };

        private static DateTimeOffset At(DayOfWeek day, int hour, int minute = 0)
        {
            DateTimeOffset monday = new(2026, 10, 26, 0, 0, 0, TimeSpan.Zero);
            int offset = ((int)day - (int)DayOfWeek.Monday + 7) % 7;
            return monday.AddDays(offset).AddHours(hour).AddMinutes(minute);
        }

        [Test]
        public void Territory_NotAttackable_OutsideWindow()
        {
            Assert.IsFalse(TerritoryRules.IsAttackable(SaturdayEvening, At(DayOfWeek.Saturday, 19, 59)));
            Assert.IsFalse(TerritoryRules.IsAttackable(SaturdayEvening, At(DayOfWeek.Saturday, 22)));
        }

        [Test]
        public void Territory_Attackable_InsideWindow()
        {
            Assert.IsTrue(TerritoryRules.IsAttackable(SaturdayEvening, At(DayOfWeek.Saturday, 20)));
            Assert.IsTrue(TerritoryRules.IsAttackable(SaturdayEvening, At(DayOfWeek.Saturday, 21, 59)));
        }

        [Test]
        public void Territory_OvernightWindow_CrossesMidnight()
        {
            var window = new VulnerabilityWindow
            {
                Days = new[] { DayOfWeek.Monday },
                StartMinuteUtc = 22 * 60,
                EndMinuteUtc = 2 * 60,
            };

            Assert.IsTrue(TerritoryRules.IsAttackable(window, At(DayOfWeek.Monday, 23)));
            Assert.IsTrue(TerritoryRules.IsAttackable(window, At(DayOfWeek.Tuesday, 1)));
            Assert.IsFalse(TerritoryRules.IsAttackable(window, At(DayOfWeek.Tuesday, 2)));
            Assert.IsFalse(TerritoryRules.IsAttackable(window, At(DayOfWeek.Tuesday, 23)));
        }

        [Test]
        public void Territory_InvalidOrEmptyWindow_FailsClosed()
        {
            Assert.IsFalse(TerritoryRules.IsAttackable(default, At(DayOfWeek.Saturday, 20)));
            Assert.IsFalse(TerritoryRules.IsAttackable(new VulnerabilityWindow
            {
                Days = new[] { DayOfWeek.Saturday },
                StartMinuteUtc = 60,
                EndMinuteUtc = 60,
            }, At(DayOfWeek.Saturday, 1)));
            Assert.IsFalse(TerritoryRules.IsAttackable(new VulnerabilityWindow
            {
                Days = new[] { DayOfWeek.Saturday },
                StartMinuteUtc = -1,
                EndMinuteUtc = 60,
            }, At(DayOfWeek.Saturday, 0)));
            Assert.IsFalse(TerritoryRules.IsAttackable(new VulnerabilityWindow
            {
                Days = new[] { DayOfWeek.Saturday },
                StartMinuteUtc = 60,
                EndMinuteUtc = 24 * 60,
            }, At(DayOfWeek.Saturday, 1)));
        }

        [Test]
        public void Territory_Unowned_CanBeCapturedOutsideWindow()
        {
            Assert.AreEqual(
                TerritoryRule.Ok,
                TerritoryRules.CanCapture(SaturdayEvening, At(DayOfWeek.Wednesday, 3), null, "g1"));
        }

        [Test]
        public void Territory_CurrentOwner_Refused()
        {
            Assert.AreEqual(
                TerritoryRule.AlreadyOwner,
                TerritoryRules.CanCapture(SaturdayEvening, At(DayOfWeek.Saturday, 20), "g1", "g1"));
        }

        [Test]
        public void Territory_OwnedOutsideWindow_Refused()
        {
            Assert.AreEqual(
                TerritoryRule.NotAttackable,
                TerritoryRules.CanCapture(SaturdayEvening, At(DayOfWeek.Wednesday, 3), "g1", "g2"));
        }

        [Test]
        public void Territory_NoGuild_Refused()
        {
            Assert.AreEqual(
                TerritoryRule.NoGuild,
                TerritoryRules.CanCapture(SaturdayEvening, At(DayOfWeek.Wednesday, 3), null, null));
        }

        [Test]
        public void ClaimFlag_Contains_WithAndWithoutCollider()
        {
            var go = new GameObject("ClaimFlagUnderTest");
            try
            {
                ClaimFlag flag = go.AddComponent<ClaimFlag>();

                Assert.IsTrue(flag.Contains(new Vector3(100f, 0f, 100f)),
                    "A flag with no collider covers its whole scene.");

                go.AddComponent<BoxCollider>().size = new Vector3(2f, 2f, 2f);

                Assert.IsTrue(flag.Contains(Vector3.zero));
                Assert.IsFalse(flag.Contains(new Vector3(10f, 0f, 0f)));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }
}
