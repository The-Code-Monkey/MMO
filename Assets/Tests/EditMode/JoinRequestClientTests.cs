using MMO.Net;
using NUnit.Framework;
using UnityEngine;

namespace MMO.Tests
{
    /// <summary>
    /// The account id is the persistence key: the server loads and stores the player's record
    /// under it, so it must be generated once and then stay put across rebuilds — it names the
    /// device, not a session. PlayerPrefs persistence is exactly what makes the reeled-in player
    /// keep the same database record on the next run.
    /// </summary>
    public sealed class JoinRequestClientTests
    {
        private GameObject _go;

        [TearDown]
        public void TearDown()
        {
            if (_go != null)
                Object.DestroyImmediate(_go);
        }

        private JoinRequestClient NewClient()
        {
            _go = new GameObject("JoinClientUnderTest");
            return _go.AddComponent<JoinRequestClient>();
        }

        [Test]
        public void Awake_GeneratesANonEmptyAccountId()
        {
            Assert.IsFalse(string.IsNullOrEmpty(NewClient().AccountId));
        }

        [Test]
        public void AccountId_IsStableAcrossRebuilds()
        {
            string first = NewClient().AccountId;

            Object.DestroyImmediate(_go);
            string second = NewClient().AccountId;

            Assert.AreEqual(first, second, "The account id belongs to the device and must not change between sessions.");
        }

        [Test]
        public void TwoClients_ShareTheDeviceAccount()
        {
            string first = NewClient().AccountId;
            var secondGo = new GameObject("SecondClient");
            try
            {
                Assert.AreEqual(first, secondGo.AddComponent<JoinRequestClient>().AccountId);
            }
            finally
            {
                Object.DestroyImmediate(secondGo);
            }
        }

        [Test]
        public void AccountId_IsNeverBlankOnceGenerated()
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(NewClient().AccountId));
        }
    }
}