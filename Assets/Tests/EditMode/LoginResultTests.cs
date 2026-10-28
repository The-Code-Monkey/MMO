using MMO.Persistence;
using NUnit.Framework;

namespace MMO.Tests
{
    /// <summary>Shape of the login outcome: success carries the vouch, failure carries a message.</summary>
    public sealed class LoginResultTests
    {
        [Test]
        public void Ok_CarriesTheTokenIdentityAndZone()
        {
            LastZoneInfo zone = LastZoneInfo.Create("127.0.0.1", 7770);
            LoginResult result = LoginResult.Ok("token-1", "user-1", "Carol", zone);

            Assert.IsTrue(result.Success);
            Assert.AreEqual("token-1", result.Token);
            Assert.AreEqual("user-1", result.UserId);
            Assert.AreEqual("Carol", result.Username);
            Assert.AreSame(zone, result.LastZone);
            Assert.IsNull(result.Error);
        }

        [Test]
        public void Ok_CarriesTheRefreshTokenForRenewingTheSession()
        {
            LoginResult result = LoginResult.Ok("token-1", "user-1", "Carol", null, "refresh-1");

            Assert.AreEqual("refresh-1", result.RefreshToken);
        }

        [Test]
        public void Ok_AllowsANullZoneForFirstEverLogin()
        {
            LoginResult result = LoginResult.Ok("token-1", "user-1", "Carol");

            Assert.IsTrue(result.Success);
            Assert.IsNull(result.LastZone);
        }

        [Test]
        public void Fail_CarriesTheMessageAndNothingElse()
        {
            LoginResult result = LoginResult.Fail("Invalid credentials.");

            Assert.IsFalse(result.Success);
            Assert.AreEqual("Invalid credentials.", result.Error);
            Assert.IsNull(result.Token);
            Assert.IsNull(result.UserId);
            Assert.IsNull(result.Username);
            Assert.IsNull(result.LastZone);
        }

        [Test]
        public void Fail_NeverLeavesTheMessageBlank()
        {
            Assert.AreEqual("Sign-in failed.", LoginResult.Fail(null).Error);
            Assert.AreEqual("Sign-in failed.", LoginResult.Fail("   ").Error);
        }
    }
}