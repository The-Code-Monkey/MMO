using System;
using MMO.Persistence;
using NUnit.Framework;

namespace MMO.Tests
{
    /// <summary>
    /// The player-owned "where did I last stand" record: a plain JSON DTO whose parsing must be
    /// tolerant, because a record written by another version of the game must never take a login
    /// down. <see cref="LastZoneInfo.IsValid"/> is what the menu consults to pick between the stored
    /// zone and the default.
    /// </summary>
    public sealed class LastZoneInfoTests
    {
        [Test]
        public void Create_SetsTheIpPortAndSavedAtStamp()
        {
            var stamp = new DateTime(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);
            LastZoneInfo zone = LastZoneInfo.Create("127.0.0.1", 7770, stamp);

            Assert.AreEqual("127.0.0.1", zone.Ip);
            Assert.AreEqual(7770, zone.Port);
            Assert.AreEqual("2026-10-06T12:00:00.000Z", zone.SavedAt);
        }

        [Test]
        public void Create_DefaultsTheStampToNowUtc()
        {
            DateTime before = DateTime.UtcNow;
            LastZoneInfo zone = LastZoneInfo.Create("play.example.com", 7780);
            DateTime after = DateTime.UtcNow;

            Assert.IsNotEmpty(zone.SavedAt);
            DateTime stamp = DateTime.Parse(zone.SavedAt, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AdjustToUniversal);
            Assert.LessOrEqual(before.AddSeconds(-1), stamp);
            Assert.GreaterOrEqual(after.AddSeconds(1), stamp);
        }

        [Test]
        public void ToJson_FromJson_RoundTripPreservesEveryField()
        {
            LastZoneInfo source = LastZoneInfo.Create("1.2.3.4", 8001, new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc));

            LastZoneInfo parsed = LastZoneInfo.FromJson(source.ToJson());

            Assert.IsNotNull(parsed);
            Assert.AreEqual(source.Ip, parsed.Ip);
            Assert.AreEqual(source.Port, parsed.Port);
            Assert.AreEqual(source.SavedAt, parsed.SavedAt);
        }

        [Test]
        public void IsValid_AcceptsIpAndHostnameOnARealPort()
        {
            Assert.IsTrue(LastZoneInfo.Create("127.0.0.1", 7770).IsValid);
            Assert.IsTrue(LastZoneInfo.Create("play.example.com", 81).IsValid);
            Assert.IsTrue(LastZoneInfo.Create("::1", 65535).IsValid);
        }

        [Test]
        public void IsValid_RejectsBlankUnspecifiedAndBadPorts()
        {
            Assert.IsFalse(LastZoneInfo.Create("", 7770).IsValid);
            Assert.IsFalse(LastZoneInfo.Create("   ", 7770).IsValid);
            Assert.IsFalse(LastZoneInfo.Create(LastZoneInfo.UnspecifiedAddress, 7770).IsValid);
            Assert.IsFalse(LastZoneInfo.Create("127.0.0.1", 0).IsValid);
            Assert.IsFalse(LastZoneInfo.Create("127.0.0.1", -1).IsValid);
            Assert.IsFalse(LastZoneInfo.Create("127.0.0.1", 70000).IsValid);
        }

        [Test]
        public void IsValid_DefaultsToFalseWhenNothingWasSet()
        {
            Assert.IsFalse(new LastZoneInfo().IsValid);
        }

        [Test]
        public void FromJson_NullBlankAndMalformed_ReadAsNoZone()
        {
            Assert.IsNull(LastZoneInfo.FromJson(null));
            Assert.IsNull(LastZoneInfo.FromJson(string.Empty));
            Assert.IsNull(LastZoneInfo.FromJson("   "));
            Assert.IsNull(LastZoneInfo.FromJson("{ this is not json"));
        }

        [Test]
        public void FromJson_ToleratesAnUnknownSchemaByIgnoringIt()
        {
            // A record saved by another build: unknown fields are dropped, the zone stays usable.
            LastZoneInfo zone = LastZoneInfo.FromJson(
                "{\"Ip\":\"10.0.0.5\",\"Port\":7779,\"gameVersion\":\"0.2.0\"}");

            Assert.IsNotNull(zone);
            Assert.IsTrue(zone.IsValid);
            Assert.AreEqual("10.0.0.5", zone.Ip);
        }

        [Test]
        public void ToString_LabelsInvalidZones()
        {
            Assert.AreEqual("(no zone)", new LastZoneInfo().ToString());
            Assert.AreEqual("127.0.0.1:7770", LastZoneInfo.Create("127.0.0.1", 7770).ToString());
        }
    }
}