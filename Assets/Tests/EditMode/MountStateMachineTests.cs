using System.Collections.Generic;
using MMO.Gameplay;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MMO.Tests
{
    /// <summary>
    /// The pure mount state machine: cast timing, takeoff legality (mount kind and zone), landing,
    /// dismount safety and the speed multiplier that follows state. All time is passed in, so these
    /// pin the boundaries without a live server or a frame of waiting.
    /// </summary>
    public sealed class MountStateMachineTests
    {
        private readonly List<MountData> _assets = new();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _assets.Count; i++)
            {
                if (_assets[i] != null)
                    Object.DestroyImmediate(_assets[i]);
            }

            _assets.Clear();
        }

        [Test]
        public void Mounting_BecomesMounted_AfterCastTime()
        {
            MountStateMachine machine = new(NewMount(flying: false, castSeconds: 1.5f));
            machine.RequestMount(now: 0d);

            Assert.AreEqual(MountState.Mounting, machine.State);
            Assert.AreEqual(MountState.Mounting, machine.Tick(1.49d));
            Assert.AreEqual(MountState.Mounted, machine.Tick(1.5d));
        }

        [Test]
        public void ZeroCast_MountsOnTheSameTick()
        {
            MountStateMachine machine = new(NewMount(flying: false, castSeconds: 0f));

            Assert.IsTrue(machine.RequestMount(now: 3d));
            Assert.AreEqual(MountState.Mounted, machine.Tick(now: 3d));
        }

        [Test]
        public void RequestMount_WhileAlreadyMounted_IsRefused()
        {
            MountStateMachine machine = Mounted(NewMount(flying: false, castSeconds: 0f), now: 0d);

            Assert.IsFalse(machine.RequestMount(now: 10d), "A second mount click must not restart anything.");
            Assert.AreEqual(MountState.Mounted, machine.State);
        }

        [Test]
        public void Takeoff_WhileMounting_IsRefused()
        {
            var machine = new MountStateMachine(NewMount(flying: true, castSeconds: 5f));
            machine.RequestMount(now: 0d);

            Assert.IsFalse(machine.RequestTakeoff(zoneAllowsFlying: true));
            Assert.AreEqual(MountState.Mounting, machine.State);
        }

        [Test]
        public void GroundMount_CannotTakeOff()
        {
            MountStateMachine machine = Mounted(NewMount(flying: false, castSeconds: 0f), now: 0d);

            Assert.IsFalse(machine.RequestTakeoff(zoneAllowsFlying: true));
            Assert.AreEqual(MountState.Mounted, machine.State);
        }

        [Test]
        public void Takeoff_Refused_WhenZoneDisallowsFlying()
        {
            MountStateMachine machine = Mounted(NewMount(flying: true, castSeconds: 0f), now: 0d);

            Assert.IsFalse(machine.RequestTakeoff(zoneAllowsFlying: false));
            Assert.AreEqual(MountState.Mounted, machine.State);
        }

        [Test]
        public void Takeoff_Allowed_ForAFlyingMountInAFlyZone()
        {
            MountStateMachine machine = Mounted(NewMount(flying: true, castSeconds: 0f), now: 0d);

            Assert.IsTrue(machine.RequestTakeoff(zoneAllowsFlying: true));
            Assert.AreEqual(MountState.Flying, machine.State);
        }

        [Test]
        public void SpeedMultiplier_MatchesState()
        {
            MountData data = NewMount(flying: true, ground: 1.5f, flight: 2.5f, castSeconds: 0f);
            MountStateMachine machine = Mounted(data, now: 0d);

            Assert.AreEqual(1.5f, machine.SpeedMultiplier, 0.0001f);

            Assert.IsTrue(machine.RequestTakeoff(zoneAllowsFlying: true));
            Assert.AreEqual(2.5f, machine.SpeedMultiplier, 0.0001f);
        }

        [Test]
        public void Land_RestoresGroundMovement()
        {
            MountStateMachine machine = Mounted(NewMount(flying: true, ground: 1.5f, flight: 2.5f, castSeconds: 0f), now: 0d);
            machine.RequestTakeoff(zoneAllowsFlying: true);

            Assert.IsTrue(machine.RequestLand());
            Assert.AreEqual(MountState.Mounted, machine.State);
            Assert.AreEqual(1.5f, machine.SpeedMultiplier, 0.0001f);
        }

        [Test]
        public void Dismount_MidAir_IsRefused()
        {
            MountStateMachine machine = Mounted(NewMount(flying: true, castSeconds: 0f), now: 0d);
            machine.RequestTakeoff(zoneAllowsFlying: true);

            Assert.IsFalse(machine.Dismount(), "Dropping a flying mount would drop the player through geometry.");
            Assert.AreEqual(MountState.Flying, machine.State);
        }

        [Test]
        public void Dismount_OnTheGround_ReturnsToUnmounted()
        {
            MountStateMachine machine = Mounted(NewMount(flying: true, castSeconds: 0f), now: 0d);

            Assert.IsTrue(machine.Dismount());
            Assert.AreEqual(MountState.Unmounted, machine.State);
            Assert.AreEqual(1f, machine.SpeedMultiplier, 0.0001f);
        }

        [Test]
        public void CastProgress_TracksTheMountCast()
        {
            var machine = new MountStateMachine(NewMount(flying: false, castSeconds: 5f));
            machine.RequestMount(now: 0d);

            Assert.AreEqual(0f, machine.CastProgress(0d), 0.0001f);
            Assert.AreEqual(0.5f, machine.CastProgress(2.5d), 0.0001f);
            Assert.AreEqual(1f, machine.CastProgress(5d), 0.0001f);
        }

        [Test]
        public void CastProgress_TracksTheDismountCast()
        {
            MountStateMachine machine = Mounted(NewMount(flying: false, castSeconds: 5f), now: 0d);
            machine.RequestDismount(now: 10d);

            Assert.AreEqual(0f, machine.CastProgress(10d), 0.0001f);
            Assert.AreEqual(0.5f, machine.CastProgress(12.5d), 0.0001f);
            Assert.AreEqual(1f, machine.CastProgress(15d), 0.0001f);
        }

        [Test]
        public void CastProgress_IsZeroWhenNoCastIsRunning()
        {
            MountStateMachine machine = Mounted(NewMount(flying: false, castSeconds: 0f), now: 0d);

            Assert.AreEqual(0f, machine.CastProgress(100d), 0.0001f);
        }

        [Test]
        public void RequestDismount_CompletesAfterTheCastTime()
        {
            MountStateMachine machine = Mounted(NewMount(flying: false, castSeconds: 5f), now: 0d);

            Assert.IsTrue(machine.RequestDismount(now: 10d), "Getting off has to take the cast, not be instant.");
            Assert.AreEqual(MountState.Dismounting, machine.State);
            Assert.AreEqual(MountState.Dismounting, machine.Tick(14.99d));
            Assert.AreEqual(MountState.Unmounted, machine.Tick(15d));
        }

        [Test]
        public void RequestDismount_WhileFlying_IsRefused()
        {
            MountStateMachine machine = Mounted(NewMount(flying: true, castSeconds: 0f), now: 0d);
            machine.RequestTakeoff(zoneAllowsFlying: true);

            Assert.IsFalse(machine.RequestDismount(now: 1d), "A flying mount must land before it can be dismissed.");
            Assert.AreEqual(MountState.Flying, machine.State);
        }

        [Test]
        public void RequestMount_WhileDismounting_IsRefused()
        {
            MountStateMachine machine = Mounted(NewMount(flying: false, castSeconds: 5f), now: 0d);
            machine.RequestDismount(now: 1d);

            Assert.IsFalse(machine.RequestMount(now: 2d));
            Assert.AreEqual(MountState.Dismounting, machine.State);
        }

        private MountStateMachine Mounted(MountData data, double now)
        {
            var machine = new MountStateMachine(data);
            machine.RequestMount(now);
            machine.Tick(now + data.MountCastSeconds);
            return machine;
        }

        private MountData NewMount(
            bool flying,
            float ground = 1.5f,
            float flight = 2.5f,
            float castSeconds = 1.5f)
        {
            MountData data = ScriptableObject.CreateInstance<MountData>();
            _assets.Add(data);

            var inspector = new SerializedObject(data);
            inspector.FindProperty("_mountName").stringValue = "Test Mount";
            inspector.FindProperty("_itemName").stringValue = "Test Mount Item";
            inspector.FindProperty("_isFlyingMount").boolValue = flying;
            inspector.FindProperty("_groundSpeedMultiplier").floatValue = ground;
            inspector.FindProperty("_flightSpeedMultiplier").floatValue = flight;
            inspector.FindProperty("_mountCastSeconds").floatValue = castSeconds;
            inspector.ApplyModifiedPropertiesWithoutUndo();

            return data;
        }
    }
}
