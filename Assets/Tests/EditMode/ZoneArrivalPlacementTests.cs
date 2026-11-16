using MMO.Gameplay;
using MMO.Net;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MMO.Tests
{
    /// <summary>The pure rules behind "spawn next to the gate you came through".</summary>
    public sealed class ZoneArrivalPlacementTests
    {
        private GameObject _first;
        private GameObject _second;

        [TearDown]
        public void Cleanup()
        {
            if (_first != null)
                Object.DestroyImmediate(_first);
            if (_second != null)
                Object.DestroyImmediate(_second);
        }

        private static ZoneTransferTrigger GateAt(Vector3 position, string targetServerId)
        {
            var go = new GameObject("Gate");
            go.transform.position = position;
            go.AddComponent<BoxCollider>();
            var gate = go.AddComponent<ZoneTransferTrigger>();
            var serialized = new SerializedObject(gate);
            serialized.FindProperty("_targetServerId").stringValue = targetServerId;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return gate;
        }

        [Test]
        public void FindGateBackTo_ReturnsTheGatePointingAtTheSourceZone()
        {
            ZoneTransferTrigger safeGate = GateAt(Vector3.zero, ZoneServerIds.SafeZone);
            _first = safeGate.gameObject;
            ZoneTransferTrigger wildernessGate = GateAt(Vector3.one, ZoneServerIds.Wilderness);
            _second = wildernessGate.gameObject;

            Assert.AreSame(safeGate, ZoneArrivalPlacement.FindGateBackTo(
                ZoneServerIds.SafeZone, new[] { wildernessGate, safeGate }));
        }

        [Test]
        public void FindGateBackTo_NoMatch_ReturnsNull()
        {
            ZoneTransferTrigger safeGate = GateAt(Vector3.zero, ZoneServerIds.SafeZone);
            _first = safeGate.gameObject;

            Assert.IsNull(ZoneArrivalPlacement.FindGateBackTo(ZoneServerIds.Wilderness, new[] { safeGate }));
        }

        [Test]
        public void NearGate_MovesInwardFromTheZoneEdge()
        {
            Vector3 arrival = ZoneArrivalPlacement.NearGate(new Vector3(0f, 1.5f, 79.5f), Vector3.zero);

            Assert.AreEqual(76.5f, arrival.z, 0.001f);
            Assert.AreEqual(1.5f, arrival.y, 0.001f);
        }
    }
}
