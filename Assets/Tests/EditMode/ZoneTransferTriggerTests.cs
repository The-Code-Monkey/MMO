using MMO.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace MMO.Tests
{
    /// <summary>
    /// The gate's client-facing range test: the prompt and the server's re-check must agree on who
    /// is standing at the gate.
    /// </summary>
    public sealed class ZoneTransferTriggerTests
    {
        private GameObject _gate;

        [TearDown]
        public void TearDown()
        {
            if (_gate != null)
                Object.DestroyImmediate(_gate);
        }

        [Test]
        public void IsPositionInRange_TrueInsideAndWithinPadding()
        {
            ZoneTransferTrigger trigger = NewGate(new Vector3(2f, 2f, 2f));

            Assert.IsTrue(trigger.IsPositionInRange(_gate.transform.position));
            Assert.IsTrue(trigger.IsPositionInRange(_gate.transform.position + new Vector3(1.5f, 0f, 0f)),
                "A player just outside the volume still reads as at the gate.");
        }

        [Test]
        public void IsPositionInRange_FalseBeyondPadding()
        {
            ZoneTransferTrigger trigger = NewGate(new Vector3(2f, 2f, 2f));

            Assert.IsFalse(trigger.IsPositionInRange(_gate.transform.position + new Vector3(2.5f, 0f, 0f)));
        }

        [Test]
        public void TargetZoneName_FallsBackToServerIdWhenBlank()
        {
            ZoneTransferTrigger trigger = NewGate(Vector3.one);

            Assert.AreEqual("server-b", trigger.TargetZoneName);
        }

        private ZoneTransferTrigger NewGate(Vector3 size)
        {
            _gate = new GameObject("Gate");
            var collider = _gate.AddComponent<BoxCollider>();
            collider.size = size;
            return _gate.AddComponent<ZoneTransferTrigger>();
        }
    }
}
