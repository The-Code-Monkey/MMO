using MMO.Persistence;
using NUnit.Framework;

namespace MMO.Tests
{
    /// <summary>
    /// The login contract. The crucial property is that the username round-trips into an email
    /// address injectively: two distinct usernames must never map to the same address, or two
    /// players would silently share an account. Everything else is the backend's own rules checked
    /// before a round trip.
    /// </summary>
    public sealed class AuthRulesTests
    {
        #region EmailForUsername

        [Test]
        public void EmailForUsername_LowercasesAndAppendsTheDomain()
        {
            Assert.AreEqual("bob@mmolocal.dev", AuthRules.EmailForUsername("Bob"));
        }

        [Test]
        public void EmailForUsername_TrimsSurroundingWhitespace()
        {
            Assert.AreEqual("bob@mmolocal.dev", AuthRules.EmailForUsername("  Bob  "));
        }

        [Test]
        public void EmailForUsername_BlankUsername_YieldsAnEmailAddressWithNoLocalPart()
        {
            Assert.AreEqual("@mmolocal.dev", AuthRules.EmailForUsername(null));
            Assert.AreEqual("@mmolocal.dev", AuthRules.EmailForUsername("   "));
        }

        [Test]
        public void EmailForUsername_IsInjectiveOverValidUsernames()
        {
            // "bob.1" and "bob_1" are both valid usernames that must stay distinguishable.
            Assert.AreNotEqual(
                AuthRules.EmailForUsername("bob.1"),
                AuthRules.EmailForUsername("bob_1"));
        }

        #endregion

        #region IsValidUsername

        [Test]
        public void IsValidUsername_RejectsNullEmptyAndWhitespace()
        {
            Assert.IsFalse(IsValid(null));
            Assert.IsFalse(IsValid(string.Empty));
            Assert.IsFalse(IsValid("   "));
        }

        [Test]
        public void IsValidUsername_AcceptsLettersDigitsAndCommonSeparators()
        {
            Assert.IsTrue(IsValid("Bob"));
            Assert.IsTrue(IsValid("bob_77"));
            Assert.IsTrue(IsValid("a-b.c"));
            Assert.IsTrue(IsValid("X"));
        }

        [Test]
        public void IsValidUsername_RejectsCharactersThatBreakTheEmail()
        {
            Assert.IsFalse(IsValid("Bob Smith"));
            Assert.IsFalse(IsValid("bob@mmo"));
            Assert.IsFalse(IsValid("bob!"));

            string message;
            AuthRules.IsValidUsername("Bob Smith", out message);
            Assert.IsNotNull(message);
            Assert.IsNotEmpty(message);
        }

        [Test]
        public void IsValidUsername_RejectsBrokenDotPlacement()
        {
            Assert.IsFalse(IsValid(".bob"));
            Assert.IsFalse(IsValid("bob."));
            Assert.IsFalse(IsValid("bob..smith"));
        }

        [Test]
        public void IsValidUsername_RejectsOverlongUsernames()
        {
            Assert.IsFalse(IsValid(new string('b', AuthRules.MaxUsernameLength + 1)));
            Assert.IsTrue(IsValid(new string('b', AuthRules.MaxUsernameLength)));
        }

        #endregion

        #region IsValidPassword

        [Test]
        public void IsValidPassword_EnforcesTheBackendMinimumLength()
        {
            Assert.IsFalse(AuthRules.IsValidPassword("short", out _));
            Assert.IsFalse(AuthRules.IsValidPassword("1234567", out _));
            Assert.IsTrue(AuthRules.IsValidPassword("password123", out _));
        }

        [Test]
        public void IsValidPassword_RejectsNullAndBlank()
        {
            Assert.IsFalse(AuthRules.IsValidPassword(null, out _));
            Assert.IsFalse(AuthRules.IsValidPassword("        ", out _));
        }

        #endregion

        #region Validate

        [Test]
        public void Validate_PassesOnlyWhenBothFieldsPass()
        {
            Assert.IsTrue(AuthRules.Validate("Bob", "password123", out _));
            Assert.IsFalse(AuthRules.Validate(null, "password123", out _));
            Assert.IsFalse(AuthRules.Validate("Bob", "short", out _));
            Assert.IsFalse(AuthRules.Validate("bob@mmo", "password123", out _));
        }

        [Test]
        public void Validate_NamesTheFirstFailure()
        {
            string message;
            AuthRules.Validate("   ", "short", out message);
            Assert.IsNotNull(message);
            Assert.IsNotEmpty(message);
        }

        #endregion

        private static bool IsValid(string username)
            => AuthRules.IsValidUsername(username, out _);
    }
}