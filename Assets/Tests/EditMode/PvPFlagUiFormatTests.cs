using MMO.UI;
using NUnit.Framework;

namespace MMO.Tests
{
    /// <summary>
    /// The toggle's caption, pinned without a Canvas: what a player reads in each of the four
    /// states the flag can be in, and whether the button can be pressed at all.
    /// </summary>
    public sealed class PvPFlagUiFormatTests
    {
        [Test]
        public void Describe_ForcedZone_WinsOverTheFlag()
        {
            Assert.AreEqual(PvPFlagUiFormat.ForcedLabel,
                PvPFlagUiFormat.Describe(forced: true, flagged: true, cooldownSeconds: 42f));
            Assert.AreEqual(PvPFlagUiFormat.ForcedLabel,
                PvPFlagUiFormat.Describe(forced: true, flagged: false, cooldownSeconds: 0f));
        }

        [Test]
        public void Describe_Unflagged_IsOffAndShowsNoCountdown()
        {
            Assert.AreEqual(PvPFlagUiFormat.UnflaggedLabel,
                PvPFlagUiFormat.Describe(forced: false, flagged: false, cooldownSeconds: 120f));
        }

        [Test]
        public void Describe_FlaggedWithoutCooldown_IsOn()
        {
            Assert.AreEqual(PvPFlagUiFormat.FlaggedLabel,
                PvPFlagUiFormat.Describe(forced: false, flagged: true, cooldownSeconds: 0f));
        }

        [Test]
        public void Describe_FlaggedWithCooldown_ShowsWholeSeconds()
        {
            Assert.AreEqual("PvP: On (5s)",
                PvPFlagUiFormat.Describe(forced: false, flagged: true, cooldownSeconds: 4.2f));
        }

        [Test]
        public void SecondsRemaining_RoundsUpSoZeroMeansReady()
        {
            Assert.AreEqual(0, PvPFlagUiFormat.SecondsRemaining(0f));
            Assert.AreEqual(0, PvPFlagUiFormat.SecondsRemaining(-3f));
            Assert.AreEqual(0, PvPFlagUiFormat.SecondsRemaining(float.NaN));
            Assert.AreEqual(1, PvPFlagUiFormat.SecondsRemaining(0.1f));
            Assert.AreEqual(300, PvPFlagUiFormat.SecondsRemaining(300f));
        }

        [Test]
        public void CanToggle_OnlyOutsideForcedZones()
        {
            Assert.IsTrue(PvPFlagUiFormat.CanToggle(forced: false));
            Assert.IsFalse(PvPFlagUiFormat.CanToggle(forced: true));
        }
    }
}
