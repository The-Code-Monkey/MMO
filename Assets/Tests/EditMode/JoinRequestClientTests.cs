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
        private JoinRequestClient _client;

        [TearDown]
        public void TearDown()
        {
            if (_go != null)
                Object.DestroyImmediate(_go);
        }

        private JoinRequestClient NewClient()
        {
            _go = new GameObject("JoinClientUnderTest");
            _client = _go.AddComponent<JoinRequestClient>();
            return _client;
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

        #region AuthToken

        [Test]
        public void AuthToken_DefaultsToEmpty()
        {
            Assert.AreEqual(string.Empty, NewClient().AuthToken);
        }

        [Test]
        public void SetAuthToken_AnnouncesTheProvidedToken()
        {
            const string Token = "session-token-abc";

            NewClient().SetAuthToken(Token);

            Assert.AreEqual(Token, Build().AuthToken);
        }

        [Test]
        public void SetAuthToken_NullIsTreatedAsNoToken()
        {
            NewClient().SetAuthToken("token");
            NewClient().SetAuthToken(null);

            Assert.AreEqual(string.Empty, Build().AuthToken);
        }

        [Test]
        public void BuildJoinRequest_CarriesNameAccountIdAndToken()
        {
            JoinRequestClient client = NewClient();
            client.SetPlayerName("Bob");
            client.SetAuthToken("token-91");

            JoinRequestBroadcast request = client.BuildJoinRequest();

            Assert.AreEqual("Bob", request.PlayerName);
            Assert.AreNotEqual(string.Empty, request.AccountId);
            Assert.AreEqual(client.AccountId, request.AccountId);
            Assert.AreEqual("token-91", request.AuthToken);
        }

        [Test]
        public void BuildJoinRequest_WithNoToken_SetsTheAuthTokenFieldToEmpty()
        {
            JoinRequestBroadcast request = NewClient().BuildJoinRequest();

            Assert.AreEqual(string.Empty, request.AuthToken);
        }

        private JoinRequestBroadcast Build() => _client.BuildJoinRequest();

        #region TransferToken

        [Test]
        public void TransferToken_DefaultsToEmpty()
        {
            Assert.AreEqual(string.Empty, NewClient().TransferToken);
        }

        [Test]
        public void SetTransferToken_AnnouncesTheArmedToken()
        {
            const string Token = "zone-transfer-abc";

            NewClient().SetTransferToken(Token);

            Assert.AreEqual(Token, Build().TransferToken);
        }

        [Test]
        public void SetTransferToken_NullIsTreatedAsNoToken()
        {
            JoinRequestClient client = NewClient();
            client.SetTransferToken("token");
            client.SetTransferToken(null);

            Assert.AreEqual(string.Empty, client.BuildJoinRequest().TransferToken);
        }

        [Test]
        public void BuildJoinRequest_CarriesNameAccountIdAuthTokenAndTransferToken()
        {
            JoinRequestClient client = NewClient();
            client.SetPlayerName("Bob");
            client.SetAuthToken("session-tok");
            client.SetTransferToken("transfer-tok");

            JoinRequestBroadcast request = client.BuildJoinRequest();

            Assert.AreEqual("Bob", request.PlayerName);
            Assert.AreEqual("session-tok", request.AuthToken);
            Assert.AreEqual("transfer-tok", request.TransferToken);
            Assert.IsFalse(string.IsNullOrEmpty(request.AccountId));
        }

        [Test]
        public void BuildJoinRequest_WithNoTransferToken_SetsTheFieldToEmpty()
        {
            Assert.AreEqual(string.Empty, NewClient().BuildJoinRequest().TransferToken);
        }

        #endregion

        #endregion
    }
}