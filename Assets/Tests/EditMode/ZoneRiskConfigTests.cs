using System.Collections.Generic;
using MMO.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace MMO.Tests
{
    /// <summary>
    /// The zone marker: which tier it carries, how a raw value is normalised, and — with several
    /// zones in one scene — which tier a point resolves to. All of it is engine-only, so it runs
    /// in EditMode with plain GameObjects.
    /// </summary>
    public sealed class ZoneRiskConfigTests
    {
        private readonly List<GameObject> _created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _created)
            {
                if (go != null)
                    Object.DestroyImmediate(go);
            }

            _created.Clear();
        }

        [Test]
        public void Normalize_KeepsDeclaredTiers()
        {
            Assert.AreEqual(ZoneRiskTier.Safe, ZoneRiskConfig.Normalize((int)ZoneRiskTier.Safe));
            Assert.AreEqual(ZoneRiskTier.Red, ZoneRiskConfig.Normalize((int)ZoneRiskTier.Red));
            Assert.AreEqual(ZoneRiskTier.Black, ZoneRiskConfig.Normalize((int)ZoneRiskTier.Black));
        }

        [Test]
        public void Normalize_UndeclaredValue_FallsBackToSafe()
        {
            Assert.AreEqual(ZoneRiskTier.Safe, ZoneRiskConfig.Normalize(-1));
            Assert.AreEqual(ZoneRiskTier.Safe, ZoneRiskConfig.Normalize(99));
        }

        [Test]
        public void Contains_WithNoCollider_CoversEverything()
        {
            ZoneRiskConfig zone = NewZone(ZoneRiskTier.Black);

            Assert.IsTrue(zone.Contains(new Vector3(500f, 0f, -500f)),
                "A zone with no volume is a whole-map flag.");
        }

        [Test]
        public void Contains_WithACollider_IsBoundedByIt()
        {
            ZoneRiskConfig zone = NewZone(ZoneRiskTier.Black, new Vector3(0f, 0f, 0f), new Vector3(10f, 10f, 10f));

            Assert.IsTrue(zone.Contains(Vector3.zero), "The centre is inside a 10 m box.");
            Assert.IsFalse(zone.Contains(new Vector3(100f, 0f, 0f)), "A point far outside the box is not in the zone.");
        }

        [Test]
        public void ResolveTierAt_NoZones_IsSafe()
        {
            Assert.AreEqual(ZoneRiskTier.Safe, ZoneRiskConfig.ResolveTierAt(Vector3.zero));
        }

        [Test]
        public void ResolveTierAt_ReturnsTheZoneContainingThePoint()
        {
            ZoneRiskConfig safe = NewZone(ZoneRiskTier.Safe, Vector3.zero, new Vector3(10f, 10f, 10f));
            ZoneRiskConfig black = NewZone(
                ZoneRiskTier.Black, new Vector3(100f, 0f, 0f), new Vector3(10f, 10f, 10f));

            Assert.AreEqual(ZoneRiskTier.Safe, safe.Tier);
            Assert.AreEqual(ZoneRiskTier.Black, black.Tier);
            Assert.AreEqual(ZoneRiskTier.Black, ZoneRiskConfig.ResolveTierAt(new Vector3(100f, 0f, 0f)));
            Assert.AreEqual(ZoneRiskTier.Safe, ZoneRiskConfig.ResolveTierAt(Vector3.zero));
        }

        [Test]
        public void ResolveTierAt_OverlappingZones_TheDangerousOneWins()
        {
            NewZone(ZoneRiskTier.Safe, Vector3.zero, new Vector3(20f, 20f, 20f));
            NewZone(ZoneRiskTier.Black, Vector3.zero, new Vector3(20f, 20f, 20f));

            Assert.AreEqual(ZoneRiskTier.Black, ZoneRiskConfig.ResolveTierAt(Vector3.zero),
                "A safe pocket inside a black zone is still a black zone.");
        }

        private ZoneRiskConfig NewZone(ZoneRiskTier tier, Vector3? center = null, Vector3? size = null)
        {
            var go = new GameObject($"Zone-{tier}");
            _created.Add(go);

            if (center.HasValue && size.HasValue)
            {
                go.transform.position = center.Value;
                BoxCollider collider = go.AddComponent<BoxCollider>();
                collider.size = size.Value;
            }

            var zone = go.AddComponent<ZoneRiskConfig>();
            zone.SetTier(tier);
            return zone;
        }
    }
}
