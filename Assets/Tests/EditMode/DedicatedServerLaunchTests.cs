using MMO.Net;
using NUnit.Framework;

namespace MMO.Tests
{
    /// <summary>
    /// The command-line contract that turns the standalone binary into a dedicated zone server.
    /// <see cref="DedicatedServerArgs.ParseZone"/> is pure, so its rules are pinned here without a
    /// live process; the launcher and the world bootstrap both build on it.
    /// </summary>
    public sealed class DedicatedServerArgsTests
    {
        [Test]
        public void ParseZone_ReadsTheValueAfterTheZoneFlag()
        {
            Assert.AreEqual("Tutorial", DedicatedServerArgs.ParseZone(new[] { "server.x86_64", "-zone", "Tutorial" }));
            Assert.AreEqual("SafeZone", DedicatedServerArgs.ParseZone(new[] { "-zone", "SafeZone" }));
        }

        [Test]
        public void ParseZone_IsCaseInsensitiveOnTheFlagButPreservesTheValue()
        {
            Assert.AreEqual("Wilderness", DedicatedServerArgs.ParseZone(new[] { "-ZONE", "Wilderness" }));
        }

        [Test]
        public void ParseZone_FindsTheFlagAmongOtherUnityArguments()
        {
            string[] args =
            {
                "server.x86_64", "-batchmode", "-nographics", "-logFile", "out.log", "-zone", "Tutorial",
            };

            Assert.AreEqual("Tutorial", DedicatedServerArgs.ParseZone(args));
        }

        [Test]
        public void ParseZone_ReturnsNullWhenTheFlagIsAbsent()
        {
            Assert.IsNull(DedicatedServerArgs.ParseZone(new[] { "server.x86_64", "-batchmode" }));
            Assert.IsNull(DedicatedServerArgs.ParseZone(System.Array.Empty<string>()));
            Assert.IsNull(DedicatedServerArgs.ParseZone(null));
        }

        [Test]
        public void ParseZone_ReturnsNullWhenTheFlagHasNoValue()
        {
            Assert.IsNull(DedicatedServerArgs.ParseZone(new[] { "server.x86_64", "-zone" }));
            Assert.IsNull(DedicatedServerArgs.ParseZone(new[] { "-zone", " " }));
        }
    }

    /// <summary>
    /// The process-wide server switch. It starts off, flips on when the launcher commits, and a
    /// client that never commits stays a client.
    /// </summary>
    public sealed class DedicatedServerModeTests
    {
        [SetUp]
        [TearDown]
        public void ResetMode() => DedicatedServerMode.Reset();

        [Test]
        public void IsInactiveUntilEnabled()
        {
            Assert.IsFalse(DedicatedServerMode.IsActive);
            Assert.IsNull(DedicatedServerMode.ZoneName);
        }

        [Test]
        public void Enable_RecordsTheZoneAndMarksTheProcess()
        {
            DedicatedServerMode.Enable("Tutorial");

            Assert.IsTrue(DedicatedServerMode.IsActive);
            Assert.AreEqual("Tutorial", DedicatedServerMode.ZoneName);
        }

        [Test]
        public void Reset_ClearsTheSwitch()
        {
            DedicatedServerMode.Enable("SafeZone");
            DedicatedServerMode.Reset();

            Assert.IsFalse(DedicatedServerMode.IsActive);
            Assert.IsNull(DedicatedServerMode.ZoneName);
        }
    }
}
