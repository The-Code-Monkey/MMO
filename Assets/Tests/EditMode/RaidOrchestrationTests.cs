using System;
using MMO.Net;
using MMO.Social;
using NUnit.Framework;

namespace MMO.Tests
{
    /// <summary>
    /// Tests for the pure orchestration FSM (<see cref="OrchestrationTick.Advance"/>). These verify
    /// the state transitions without any real time, network, or Docker dependencies — exactly as
    /// specified in issue #61's test plan.
    /// </summary>
    public sealed class RaidOrchestrationTests
    {
        [Test]
        public void Advance_Healthy_ReachesReady()
        {
            OrchestrationState result = OrchestrationTick.Advance(
                OrchestrationState.Starting,
                containerHealthy: true,
                failed: false,
                elapsedSeconds: 1,
                timeoutSeconds: 90);

            Assert.AreEqual(OrchestrationState.Ready, result);
        }

        [Test]
        public void Advance_NotHealthyPastTimeout_TimesOut()
        {
            OrchestrationState result = OrchestrationTick.Advance(
                OrchestrationState.Starting,
                containerHealthy: false,
                failed: false,
                elapsedSeconds: 91,
                timeoutSeconds: 90);

            Assert.AreEqual(OrchestrationState.TimedOut, result);
        }

        [Test]
        public void Advance_ContainerFailed_Fails()
        {
            OrchestrationState result = OrchestrationTick.Advance(
                OrchestrationState.Starting,
                containerHealthy: false,
                failed: true,
                elapsedSeconds: 1,
                timeoutSeconds: 90);

            Assert.AreEqual(OrchestrationState.Failed, result);
        }

        [Test]
        public void Advance_RequestedToStarting_WhenNotHealthyAndNotTimedOut()
        {
            OrchestrationState result = OrchestrationTick.Advance(
                OrchestrationState.Requested,
                containerHealthy: false,
                failed: false,
                elapsedSeconds: 1,
                timeoutSeconds: 90);

            Assert.AreEqual(OrchestrationState.Starting, result);
        }

        [Test]
        public void Advance_StaysInStarting_WhenNotHealthyAndNotTimedOut()
        {
            OrchestrationState result = OrchestrationTick.Advance(
                OrchestrationState.Starting,
                containerHealthy: false,
                failed: false,
                elapsedSeconds: 30,
                timeoutSeconds: 90);

            Assert.AreEqual(OrchestrationState.Starting, result);
        }

        [Test]
        public void Advance_StaysInRequested_WhenHealthyImmediately()
        {
            OrchestrationState result = OrchestrationTick.Advance(
                OrchestrationState.Requested,
                containerHealthy: true,
                failed: false,
                elapsedSeconds: 0,
                timeoutSeconds: 90);

            Assert.AreEqual(OrchestrationState.Ready, result);
        }

        [Test]
        public void Advance_StaysInReady_OnceReached()
        {
            OrchestrationState result = OrchestrationTick.Advance(
                OrchestrationState.Ready,
                containerHealthy: false,
                failed: false,
                elapsedSeconds: 100,
                timeoutSeconds: 90);

            Assert.AreEqual(OrchestrationState.Ready, result);
        }

        [Test]
        public void Advance_StaysInFailed_OnceReached()
        {
            OrchestrationState result = OrchestrationTick.Advance(
                OrchestrationState.Failed,
                containerHealthy: true,
                failed: false,
                elapsedSeconds: 0,
                timeoutSeconds: 90);

            Assert.AreEqual(OrchestrationState.Failed, result);
        }

        [Test]
        public void Advance_StaysInTimedOut_OnceReached()
        {
            OrchestrationState result = OrchestrationTick.Advance(
                OrchestrationState.TimedOut,
                containerHealthy: true,
                failed: false,
                elapsedSeconds: 0,
                timeoutSeconds: 90);

            Assert.AreEqual(OrchestrationState.TimedOut, result);
        }

        [Test]
        public void Advance_TimeoutFromRequested()
        {
            OrchestrationState result = OrchestrationTick.Advance(
                OrchestrationState.Requested,
                containerHealthy: false,
                failed: false,
                elapsedSeconds: 91,
                timeoutSeconds: 90);

            Assert.AreEqual(OrchestrationState.TimedOut, result);
        }

        [Test]
        public void Advance_FailedTakesPrecedenceOverTimeout()
        {
            OrchestrationState result = OrchestrationTick.Advance(
                OrchestrationState.Starting,
                containerHealthy: false,
                failed: true,
                elapsedSeconds: 100,
                timeoutSeconds: 90);

            Assert.AreEqual(OrchestrationState.Failed, result);
        }

        [Test]
        public void Advance_HealthyTakesPrecedenceOverTimeout()
        {
            OrchestrationState result = OrchestrationTick.Advance(
                OrchestrationState.Starting,
                containerHealthy: true,
                failed: false,
                elapsedSeconds: 100,
                timeoutSeconds: 90);

            Assert.AreEqual(OrchestrationState.Ready, result);
        }

        [Test]
        public void OrchestrationHandle_IsValid_WhenInstanceIdPresent()
        {
            var handle = new OrchestrationHandle("instance-123", "raid-1", "raid", "secret");
            Assert.IsTrue(handle.IsValid);
        }

        [Test]
        public void OrchestrationHandle_IsInvalid_WhenInstanceIdMissing()
        {
            var handle = new OrchestrationHandle("", "raid-1", "raid", "secret");
            Assert.IsFalse(handle.IsValid);
        }

        [Test]
        public void OrchestrationHandle_ToZoneRegistryRecord_PopulatesCorrectly()
        {
            var handle = new OrchestrationHandle("instance-123", "raid-1", "raid", "secret");
            var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

            var record = handle.ToZoneRegistryRecord("10.0.0.1", 7777, "orchestrator-instance-123", 1, now);

            Assert.AreEqual("orchestrator-instance-123", record.ServerId);
            Assert.AreEqual("10.0.0.1", record.Host);
            Assert.AreEqual((ushort)7777, record.Port);
            Assert.AreEqual("raid", record.ZoneTemplateId);
            Assert.AreEqual(1, record.MaxInstances);
            Assert.AreEqual(0, record.ActiveInstances);
            Assert.AreEqual(now.UtcDateTime.Ticks, record.HeartbeatAtTicks);
        }
    }
}