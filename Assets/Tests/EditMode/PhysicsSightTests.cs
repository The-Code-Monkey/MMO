using System.Collections.Generic;
using MMO.Gameplay;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MMO.Tests
{
    /// <summary>
    /// The one impure LoS piece: the raycast. Everything else about line of sight is pure
    /// (<see cref="SightRulesTests"/>); here a couple of real colliders prove that a wall blocks,
    /// that the target's own body does not, and that triggers are not cover — the assumptions the
    /// rest of the system reads a clear/blocked boolean from.
    /// </summary>
    public sealed class PhysicsSightTests
    {
        private readonly List<GameObject> _sceneObjects = new();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _sceneObjects)
            {
                if (go != null)
                    Object.DestroyImmediate(go);
            }

            _sceneObjects.Clear();
        }

        [Test]
        public void HasLineOfSight_NothingBetween_IsClear()
        {
            GameObject target = Box("Target", new Vector3(0f, 0f, 10f));

            Assert.IsTrue(NewSight().HasLineOfSight(Vector3.zero, target.transform.position, target));
        }

        [Test]
        public void HasLineOfSight_CoverBetween_IsBlocked()
        {
            Box("Wall", new Vector3(0f, 0f, 5f));
            GameObject target = Box("Target", new Vector3(0f, 0f, 10f));

            Assert.IsFalse(NewSight().HasLineOfSight(Vector3.zero, target.transform.position, target),
                "A wall between the eye and the target must block the line.");
        }

        [Test]
        public void HasLineOfSight_TheTargetsOwnCollider_IsNotCover()
        {
            // The ray is aimed at the target's centre, so its own collider is the last thing it
            // meets. A hit that lands on the target is a clear line, not a blocked one.
            GameObject target = Box("Target", new Vector3(0f, 0f, 10f));

            Assert.IsTrue(NewSight().HasLineOfSight(Vector3.zero, target.transform.position, target));
        }

        [Test]
        public void HasLineOfSight_NoTargetAndCoverBetween_IsBlocked()
        {
            Box("Wall", new Vector3(0f, 0f, 5f));

            Assert.IsFalse(NewSight().HasLineOfSight(Vector3.zero, new Vector3(0f, 0f, 10f), null),
                "With no target to forgive, anything on the line is cover.");
        }

        [Test]
        public void HasLineOfSight_AZeroLengthLine_IsClear()
        {
            GameObject target = Box("Target", Vector3.zero);

            Assert.IsTrue(NewSight().HasLineOfSight(Vector3.zero, Vector3.zero, target),
                "A target at the eye has no direction to be blocked along.");
        }

        [Test]
        public void HasLineOfSight_ATriggerBetween_IsClear()
        {
            GameObject trigger = Box("Trigger", new Vector3(0f, 0f, 5f));
            trigger.GetComponent<BoxCollider>().isTrigger = true;
            GameObject target = Box("Target", new Vector3(0f, 0f, 10f));

            Assert.IsTrue(NewSight().HasLineOfSight(Vector3.zero, target.transform.position, target),
                "Triggers are volumes, not cover.");
        }

        private static PhysicsSight NewSight()
        {
            // Colliders created this frame are not in the physics scene until the transforms sync.
            Physics.SyncTransforms();
            return new PhysicsSight();
        }

        private GameObject Box(string name, Vector3 position)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.position = position;
            _sceneObjects.Add(go);
            return go;
        }
    }
}
