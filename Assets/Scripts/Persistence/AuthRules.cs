using System;

namespace MMO.Persistence
{
    /// <summary>
    /// The login contract shared by the Main Menu and the backend, so a bad submit fails fast
    /// with a readable message instead of a round trip. The rules mirror what Nakama enforces
    /// (email shape, minimum password length) and one thing the backend does not: a usable
    /// character set.
    ///
    /// <para>
    /// Nakama authenticates email accounts by <em>email address</em> and stores the token's
    /// username for display, so the username a player types here is the login identity: the email
    /// is derived deterministically from it (<c>&lt;username&gt;@mmolocal.dev</c>). That mapping
    /// must be *injective* — if two usernames could collapse onto one address they would silently
    /// share an account — so usernames are restricted to characters that are safe in an email
    /// local part, and consecutive/leading/trailing dots are banned (they would break email
    /// formatting). Usernames are lowercased for the address because Nakama lowercases addresses
    /// too; "Bob" and "bob" are therefore the same account, exactly as they would be two ways of
    /// spelling one real inbox.
    /// </para>
    /// </summary>
    public static class AuthRules
    {
        public const int MinPasswordLength = 8;
        public const int MaxUsernameLength = 128;
        public const string EmailDomain = "mmolocal.dev";

        /// <summary>
        /// Deterministic email address for <c>username</c>. Empty input maps to
        /// <c>"@mmolocal.dev"</c>, whose local part fails the backend's own email check — so a
        /// blank username can never accidentally authenticate.
        /// </summary>
        public static string EmailForUsername(string username)
            => string.Concat(username?.Trim().ToLowerInvariant(), "@", EmailDomain);

        /// <summary>True when the username may be displayed and derived into an email.</summary>
        public static bool IsValidUsername(string username, out string message)
        {
            username = username?.Trim();
            if (string.IsNullOrEmpty(username))
            {
                message = "A username is required.";
                return false;
            }

            if (username.Length > MaxUsernameLength)
            {
                message = $"Usernames are at most {MaxUsernameLength} characters.";
                return false;
            }

            if (!HasAllowedUsernameChars(username))
            {
                message = "Usernames may only contain letters, digits, '-', '.' or '_'.";
                return false;
            }

            if (username.StartsWith(".", StringComparison.Ordinal) ||
                username.EndsWith(".", StringComparison.Ordinal) ||
                username.Contains("..", StringComparison.Ordinal))
            {
                message = "Usernames cannot start or end with '.' or contain '..'.";
                return false;
            }

            message = null;
            return true;
        }

        /// <summary>True when the password meets the backend's minimum (and is not blank).</summary>
        public static bool IsValidPassword(string password, out string message)
        {
            if (string.IsNullOrWhiteSpace(password) || password.Length < MinPasswordLength)
            {
                message = $"Passwords need at least {MinPasswordLength} characters.";
                return false;
            }

            message = null;
            return true;
        }

        /// <summary>Validates both fields together; the combined error names the first failure.</summary>
        public static bool Validate(string username, string password, out string message)
        {
            string usernameError;
            if (!IsValidUsername(username, out usernameError))
            {
                message = usernameError;
                return false;
            }

            string passwordError;
            if (!IsValidPassword(password, out passwordError))
            {
                message = passwordError;
                return false;
            }

            message = null;
            return true;
        }

        private static bool HasAllowedUsernameChars(string username)
        {
            // .NET Standard 2.1 has no char.IsAsciiLetterOrDigit, and the check must stay ASCII
            // so the derived email local part is safe in any SMTP implementation.
            foreach (char c in username)
            {
                bool letter = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
                bool digit = c >= '0' && c <= '9';
                if (!letter && !digit && c != '-' && c != '_' && c != '.')
                    return false;
            }

            return true;
        }
    }
}