using MMO.Client;
using NUnit.Framework;

namespace MMO.Tests
{
    /// <summary>
    /// Settings sliders are linear; mixer groups are decibels. The conversion is small but a
    /// wrong scale makes 50% sound almost silent, so it is pinned here.
    /// </summary>
    public sealed class MixerVolumeTests
    {
        [Test]
        public void ToDecibels_FullVolume_IsZero()
        {
            Assert.AreEqual(0f, MixerVolume.ToDecibels(1f), 0.001f);
        }

        [Test]
        public void ToDecibels_HalfVolume_IsAboutMinusSix()
        {
            Assert.AreEqual(-6.0206f, MixerVolume.ToDecibels(0.5f), 0.01f);
        }

        [Test]
        public void ToDecibels_Zero_IsTheSilenceFloor()
        {
            Assert.AreEqual(MixerVolume.MinimumDecibels, MixerVolume.ToDecibels(0f));
        }

        [Test]
        public void ToDecibels_AboveOne_ClampsToFullVolume()
        {
            Assert.AreEqual(0f, MixerVolume.ToDecibels(4f), 0.001f);
        }

        [Test]
        public void ToDecibels_Negative_IsTheSilenceFloor()
        {
            Assert.AreEqual(MixerVolume.MinimumDecibels, MixerVolume.ToDecibels(-2f));
        }
    }
}
