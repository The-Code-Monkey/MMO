using MMO.Persistence;
using NUnit.Framework;
using UnityEngine;

namespace MMO.Tests
{
    /// <summary>
    /// The persisted half of the flag: an old record loads unflagged, a flagged one survives the
    /// JSON round trip the backend performs, and the schema was bumped with the new field.
    /// </summary>
    public sealed class PlayerPvPFlagSaveTests
    {
        [Test]
        public void NewRecord_DefaultsToUnflagged()
        {
            Assert.IsFalse(new PlayerSaveRecord().PvPFlag);
        }

        [Test]
        public void LegacyRecordWithoutTheField_LoadsUnflagged()
        {
            // What a save written before the field existed looks like: no key at all.
            var record = JsonUtility.FromJson<PlayerSaveRecord>("{\"UserId\":\"u-1\",\"Level\":3}");

            Assert.IsNotNull(record);
            Assert.IsFalse(record.PvPFlag);
        }

        [Test]
        public void CaptureAndRoundTrip_KeepsTheFlag()
        {
            var record = new PlayerSaveRecord { UserId = "u-1", PvPFlag = true };

            string json = JsonUtility.ToJson(record);
            var loaded = JsonUtility.FromJson<PlayerSaveRecord>(json);

            Assert.IsTrue(loaded.PvPFlag);
        }

        [Test]
        public void SchemaVersion_WasBumpedForTheNewField()
        {
            Assert.GreaterOrEqual(PlayerSaveRecord.CurrentSchemaVersion, 6);
        }
    }
}
