using System;
using System.Threading;
using System.Threading.Tasks;
using MMO.Persistence;
using UnityEngine;

namespace MMO.Social
{
    /// <summary>
    /// Best-effort <c>social_notify</c> delivery. A lost hint is recovered by the reconcile pass,
    /// so this never throws or blocks the write that triggered it.
    /// </summary>
    internal sealed class SocialHintClient
    {
        private readonly Func<NakamaRestClient> _rest;

        public SocialHintClient(Func<NakamaRestClient> rest)
        {
            _rest = rest;
        }

        public async Task SendAsync(string type, string target, params string[] accounts)
        {
            NakamaRestClient rest = _rest?.Invoke();
            if (rest == null || !rest.IsConnected)
                return;

            try
            {
                await rest.RpcAsync("social_notify", JsonUtility.ToJson(new NotifyPayload
                {
                    t = type,
                    target = target,
                    accounts = accounts,
                }), CancellationToken.None);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Social] social_notify ({type}) failed: {e.Message}");
            }
        }

        [Serializable]
        private sealed class NotifyPayload
        {
            public string t;
            public string target;
            public string[] accounts;
        }
    }
}
