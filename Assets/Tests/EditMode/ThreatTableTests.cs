using System;
using MMO.Gameplay;
using NUnit.Framework;

namespace MMO.Tests
{
    /// <summary>
    /// Pure threat table logic: accumulation, forced taunt placement, removal on death,
    /// and deterministic tie-breaking by insertion order.
    /// </summary>
    public sealed class ThreatTableTests
    {
        [Test]
        public void ThreatTable_TargetsHighestAccumulated()
        {
            var t = new ThreatTable();
            t.Add("tank", 50f);
            t.Add("healer", 30f);

            Assert.AreEqual("tank", t.CurrentTarget);
        }

        [Test]
        public void ThreatTable_TieBreaksByInsertionOrder()
        {
            var t = new ThreatTable();
            t.Add("first", 100f);
            t.Add("second", 100f);

            Assert.AreEqual("first", t.CurrentTarget);
        }

        [Test]
        public void Add_ClampsNegativeAmountsToZero()
        {
            var t = new ThreatTable();
            t.Add("tank", -50f);

            Assert.AreEqual(0f, t.Get("tank"));
        }

        [Test]
        public void Add_NullOrEmptyEntityId_Throws()
        {
            var t = new ThreatTable();

            Assert.Throws<ArgumentException>(() => t.Add(null, 10f));
            Assert.Throws<ArgumentException>(() => t.Add(string.Empty, 10f));
        }

        [Test]
        public void Set_PutsEntityAtExactValue()
        {
            var t = new ThreatTable();
            t.Set("tank", 500f);

            Assert.AreEqual(500f, t.Get("tank"));
        }

        [Test]
        public void Taunt_PutsTankOnTop_EvenBelowExistingThreat()
        {
            var t = new ThreatTable();
            t.Add("dps", 1000f);
            t.Set("tank", ThreatRules.TauntFloor);

            Assert.AreEqual("tank", t.CurrentTarget);
        }

        [Test]
        public void Remove_DeadEntity_IsNeverTargeted()
        {
            var t = new ThreatTable();
            t.Add("tank", 500f);
            t.Add("healer", 300f);
            t.Remove("tank");

            Assert.AreEqual("healer", t.CurrentTarget);
        }

        [Test]
        public void Remove_NullOrEmptyEntityId_Throws()
        {
            var t = new ThreatTable();

            Assert.Throws<ArgumentException>(() => t.Remove(null));
            Assert.Throws<ArgumentException>(() => t.Remove(string.Empty));
        }

        [Test]
        public void Get_ReturnsZeroForUnknownEntity()
        {
            var t = new ThreatTable();

            Assert.AreEqual(0f, t.Get("unknown"));
        }

        [Test]
        public void Get_NullOrEmptyEntityId_Throws()
        {
            var t = new ThreatTable();

            Assert.Throws<ArgumentException>(() => t.Get(null));
            Assert.Throws<ArgumentException>(() => t.Get(string.Empty));
        }

        [Test]
        public void Clear_EmptiesTheTable()
        {
            var t = new ThreatTable();
            t.Add("tank", 500f);
            t.Add("healer", 300f);
            t.Clear();

            Assert.IsNull(t.CurrentTarget);
            Assert.AreEqual(0f, t.Get("tank"));
        }

        [Test]
        public void CurrentTarget_ReturnsNullWhenEmpty()
        {
            var t = new ThreatTable();

            Assert.IsNull(t.CurrentTarget);
        }
    }
}