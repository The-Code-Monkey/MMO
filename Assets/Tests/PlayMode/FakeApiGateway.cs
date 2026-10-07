using System;
using System.Threading;
using System.Threading.Tasks;
using MMO.Persistence;

namespace MMO.Tests
{
    /// <summary>
    /// Backend substitute for the login flow tests: canned outcomes with a full log of what the
    /// connector asked for, so the tests assert on the <em>call shape</em> (credentials, token,
    /// zone passed to storage) rather than the bytes underneath. Behavior is switched per test via
    /// the <c>On*</c> delegates; a blocked login is released with <see cref="ReleaseLogin"/>.
    /// </summary>
    internal sealed class FakeApiGateway : IApiGateway
    {
        public Func<string, string, CancellationToken, Task<LoginResult>> OnLogin;
        public Func<string, string, CancellationToken, Task<LastZoneInfo>> OnLoadLastZone;
        public Func<string, string, LastZoneInfo, CancellationToken, Task> OnSaveLastZone;

        private TaskCompletionSource<LoginResult> _blockedLogin;

        public int LoginCalls;
        public string LastLoginUsername;
        public string LastLoginPassword;

        public int LoadCalls;
        public string LastLoadedUserId;
        public string LastLoadedToken;

        public int SaveCalls;
        public string SavedUserId;
        public string SavedToken;
        public LastZoneInfo SavedZone;

        public Task<LoginResult> LoginAsync(string username, string password, CancellationToken ct = default)
        {
            LoginCalls++;
            LastLoginUsername = username;
            LastLoginPassword = password;

            if (_blockedLogin != null)
                return _blockedLogin.Task;

            return OnLogin != null
                ? OnLogin(username, password, ct)
                : Success();
        }

        public Task<LastZoneInfo> LoadLastZoneAsync(string userId, string token, CancellationToken ct = default)
        {
            LoadCalls++;
            LastLoadedUserId = userId;
            LastLoadedToken = token;
            return OnLoadLastZone != null
                ? OnLoadLastZone(userId, token, ct)
                : Task.FromResult<LastZoneInfo>(null);
        }

        public Task SaveLastZoneAsync(string userId, string token, LastZoneInfo zone, CancellationToken ct = default)
        {
            SaveCalls++;
            SavedUserId = userId;
            SavedToken = token;
            SavedZone = zone;
            return OnSaveLastZone != null
                ? OnSaveLastZone(userId, token, zone, ct)
                : Task.CompletedTask;
        }

        /// <summary>Holds the next login open (for busy/race tests) until released.</summary>
        public Task<LoginResult> BlockLogin()
        {
            _blockedLogin = new TaskCompletionSource<LoginResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            return _blockedLogin.Task;
        }

        public void ReleaseLogin(LoginResult result)
        {
            _blockedLogin?.SetResult(result);
            _blockedLogin = null;
        }

        public static Task<LoginResult> Success(
            string token = "token-1", string userId = "user-1", string username = "Bob",
            LastZoneInfo zone = null)
            => Task.FromResult(LoginResult.Ok(token, userId, username, zone));

        public static Task<LoginResult> Failure(string message)
            => Task.FromResult(LoginResult.Fail(message));
    }
}