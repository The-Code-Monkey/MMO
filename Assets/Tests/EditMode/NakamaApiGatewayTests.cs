using MMO.Net;
using MMO.Persistence;
using NUnit.Framework;

namespace MMO.Tests
{
    /// <summary>
    /// The gateway's URL/payload builders and its response parsers — the exact bytes sent to Nakama
    /// and the exact shapes read back — tested without a socket. Shapes below mirror the real
    /// Nakama REST gateway as exercised against a live 3.41.0 backend.
    /// </summary>
    public sealed class NakamaApiGatewayTests
    {
        #region Builders

        [Test]
        public void BuildAuthenticateUrl_UsesCreateAndEscapesTheUsername()
        {
            string url = NakamaApiGateway.BuildAuthenticateUrl("http://127.0.0.1:7350", "Bob Smith");

            Assert.IsTrue(url.StartsWith("http://127.0.0.1:7350/v2/account/authenticate/email?create=true&username="));
            Assert.IsTrue(url.EndsWith("Bob%20Smith"), $"expected the username URL-escaped, got {url}");
        }

        [Test]
        public void BuildAuthenticateUrl_HandlesABlankUsernameHarmlessly()
        {
            string url = NakamaApiGateway.BuildAuthenticateUrl("http://127.0.0.1:7350", null);

            Assert.IsTrue(url.EndsWith("username="));
        }

        private sealed class EmailMirror
        {
            public string email;
            public string password;
        }

        [Test]
        public void BuildAuthenticateJson_SerializesTheCredentials()
        {
            EmailMirror mirror = UnityEngine.JsonUtility.FromJson<EmailMirror>(
                NakamaApiGateway.BuildAuthenticateJson("bob@mmolocal.dev", "password123"));

            Assert.IsNotNull(mirror);
            Assert.AreEqual("bob@mmolocal.dev", mirror.email);
            Assert.AreEqual("password123", mirror.password);
        }

        [Test]
        public void BuildZoneReadJson_AsksForThePlayersOwnRecord()
        {
            string json = NakamaApiGateway.BuildZoneReadJson("user-42");

            Assert.IsTrue(json.Contains("player_metadata"), json);
            Assert.IsTrue(json.Contains("last_zone"), json);
            Assert.IsTrue(json.Contains("user-42"), json);
        }

        [Test]
        public void BuildZoneWriteJson_StampWritesThePlayersOwnRecordUnconditionally()
        {
            LastZoneInfo zone = LastZoneInfo.Create("10.1.2.3", 9001, new System.DateTime(2026, 1, 1, 0, 0, 0, System.DateTimeKind.Utc));
            string json = NakamaApiGateway.BuildZoneWriteJson(zone);

            Assert.IsTrue(json.Contains("player_metadata"), json);
            Assert.IsTrue(json.Contains("last_zone"), json);

            WriteMirror mirror = UnityEngine.JsonUtility.FromJson<WriteMirror>(json);
            Assert.IsNotNull(mirror);
            Assert.IsNotNull(mirror.objects);
            Assert.AreEqual(1, mirror.objects.Count);
            Assert.AreEqual("player_metadata", mirror.objects[0].collection);
            Assert.AreEqual("last_zone", mirror.objects[0].key);
            Assert.AreEqual(string.Empty, mirror.objects[0].version, "empty version = unconditional upsert");
            Assert.AreEqual(1, mirror.objects[0].permissionRead);
            Assert.AreEqual(1, mirror.objects[0].permissionWrite);

            LastZoneInfo inner = LastZoneInfo.FromJson(mirror.objects[0].value);
            Assert.IsNotNull(inner);
            Assert.AreEqual("10.1.2.3", inner.Ip);
            Assert.AreEqual(9001, inner.Port);
        }

        [System.Serializable]
        private sealed class WriteMirror
        {
            public System.Collections.Generic.List<WriteObject> objects;
        }

        [System.Serializable]
        private sealed class WriteObject
        {
            public string collection;
            public string key;
            public string value;
            public string version;
            public int permissionRead;
            public int permissionWrite;
        }

        #endregion

        #region ParseSessionToken

        [Test]
        public void ParseSessionToken_GrabsTheBearerTokenFromTheAuthResponse()
        {
            Assert.AreEqual(
                "abc123",
                NakamaApiGateway.ParseSessionToken("{\"created\":true,\"token\":\"abc123\",\"refresh_token\":\"z\"}"));
        }

        [Test]
        public void ParseSessionToken_NullBlankAndAbsent_ReadAsNoToken()
        {
            Assert.IsNull(NakamaApiGateway.ParseSessionToken(null));
            Assert.IsNull(NakamaApiGateway.ParseSessionToken("   "));
            Assert.IsNull(NakamaApiGateway.ParseSessionToken("{}"));
            Assert.IsNull(NakamaApiGateway.ParseSessionToken("not json at all"));
        }

        #endregion

        #region ParseAccountResponse

        [Test]
        public void ParseAccountResponse_ReadsTheVouchedIdentity()
        {
            TokenIdentity identity = NakamaApiGateway.ParseAccountResponse(
                "{\"user\":{\"id\":\"0f49e72d\",\"username\":\"Bob\"},\"email\":\"bob@mmolocal.dev\"}");

            Assert.IsTrue(identity.IsValid);
            Assert.AreEqual("0f49e72d", identity.UserId);
            Assert.AreEqual("Bob", identity.Username);
        }

        [Test]
        public void ParseAccountResponse_RejectedSession_IsAnInvalidIdentity()
        {
            Assert.IsFalse(NakamaApiGateway.ParseAccountResponse("{\"code\":16,\"message\":\"Auth token invalid\"}").IsValid);
            Assert.IsFalse(NakamaApiGateway.ParseAccountResponse("{}").IsValid);
            Assert.IsFalse(NakamaApiGateway.ParseAccountResponse("garbage").IsValid);
            Assert.IsFalse(NakamaApiGateway.ParseAccountResponse(null).IsValid);
        }

        [Test]
        public void ParseAccountResponse_RejectsAnIdentityWithNoId()
        {
            Assert.IsFalse(NakamaApiGateway.ParseAccountResponse("{\"user\":{\"username\":\"NoId\"}}").IsValid);
        }

        #endregion

        #region ParseZoneReadResponse

        [Test]
        public void ParseZoneReadResponse_ReadsTheValueOutOfTheStorageResponse()
        {
            string json = "{\"objects\":[{\"collection\":\"player_metadata\",\"key\":\"last_zone\"," +
                          "\"user_id\":\"u1\",\"value\":\"{\\\"Ip\\\":\\\"127.0.0.1\\\",\\\"Port\\\":7770}\"," +
                          "\"version\":\"abc\"}]}";

            LastZoneInfo zone = NakamaApiGateway.ParseZoneReadResponse(json);

            Assert.IsNotNull(zone);
            Assert.IsTrue(zone.IsValid);
            Assert.AreEqual("127.0.0.1", zone.Ip);
            Assert.AreEqual(7770, zone.Port);
        }

        [Test]
        public void ParseZoneReadResponse_NoRecord_IsNoZone()
        {
            Assert.IsNull(NakamaApiGateway.ParseZoneReadResponse("{}"));                     // backend omits empty lists
            Assert.IsNull(NakamaApiGateway.ParseZoneReadResponse("{\"objects\":[]}"));       // explicit empty list
            Assert.IsNull(NakamaApiGateway.ParseZoneReadResponse(null));
            Assert.IsNull(NakamaApiGateway.ParseZoneReadResponse("garbage"));
        }

        [Test]
        public void ParseZoneReadResponse_MalformedValue_IsNoZone()
        {
            string json = "{\"objects\":[{\"value\":\"{ not even close\"}]}";
            Assert.IsNull(NakamaApiGateway.ParseZoneReadResponse(json));
        }

        #endregion

        #region ParseErrorMessage

        [Test]
        public void ParseErrorMessage_ExtractsTheNakamaMessage()
        {
            Assert.AreEqual(
                "Invalid credentials.",
                NakamaApiGateway.ParseErrorMessage("{\"code\":16,\"message\":\"Invalid credentials.\"}"));
        }

        [Test]
        public void ParseErrorMessage_VacuousBodiesYieldNull()
        {
            Assert.IsNull(NakamaApiGateway.ParseErrorMessage(null));
            Assert.IsNull(NakamaApiGateway.ParseErrorMessage(""));
            Assert.IsNull(NakamaApiGateway.ParseErrorMessage("{}"));
            Assert.IsNull(NakamaApiGateway.ParseErrorMessage("html error page"));
        }

        #endregion
    }
}