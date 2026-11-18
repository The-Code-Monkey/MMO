using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FishNet.Connection;
using FishNet.Object;
using MMO.Social;
using UnityEngine;

namespace MMO.Moderation
{
    /// <summary>
    /// One player's moderation surface — the intent-only shell over
    /// <see cref="ModerationService"/>.
    ///
    /// <para>
    /// Reporting is a <c>[ServerRpc]</c>: the client supplies a target, a category and a short
    /// note, and the server re-checks ownership, resolves the reporter's account id from the join
    /// cache and applies the throttle. The target is never told, and the reporter receives only a
    /// rule code. Notices travel the other way as JSON through a <c>[TargetRpc]</c>, which is what
    /// a kick/warn uses before the connection closes and what a reconnect asks for.
    /// </para>
    /// </summary>
    public sealed class PlayerModeration : NetworkBehaviour
    {
        [Serializable]
        private sealed class NoticeList
        {
            public List<ModerationNotice> Notices = new List<ModerationNotice>();
        }

        private string _accountId;
        private ModerationService _service;

        /// <summary>Raised on the owning client for each notice the server delivers.</summary>
        public event Action<ModerationNotice> NoticeReceived;

        /// <summary>Raised on the owning client when a report is refused (throttle, duplicate, backend).</summary>
        public event Action<ReportRule> ReportFailed;

        public override void OnStartNetwork()
        {
            if (IsServer)
                _accountId = ResolveAccountId();
        }

        /// <summary>
        /// Client intent: file a report about <paramref name="targetAccountId"/>. A blank target is
        /// dropped locally; every other decision belongs to the server.
        /// </summary>
        public void RequestReport(string targetAccountId, ModerationCategory category, string note)
        {
            if (!OwnsSelf() || string.IsNullOrEmpty(targetAccountId))
                return;

            RpcReport(targetAccountId, (int)category, note ?? string.Empty);
        }

        /// <summary>Client intent: ask for the notices still in force after a reconnect.</summary>
        public void RequestNotices()
        {
            if (OwnsSelf())
                RpcRequestNotices();
        }

        /// <summary>
        /// Server: persist a notice for this player and deliver it immediately when the client is
        /// still connected. A ban also uses the stored notice on the next join (the join gate reads
        /// it); a warn/kick relies on this push.
        /// </summary>
        [Server]
        public async Task<ModerationNotice> NotifyAsync(
            ModerationAction action,
            ModerationCategory category,
            long expiresAtTicks)
        {
            ModerationService service = Service;
            if (service == null || string.IsNullOrEmpty(_accountId))
                return null;

            ModerationNotice notice = await service.NotifyAsync(
                _accountId, action, category, expiresAtTicks, DateTimeOffset.UtcNow.UtcDateTime.Ticks);

            if (notice != null && IsClientStarted && Owner != null)
                TargetRpcNotice(Owner, JsonUtility.ToJson(notice));

            return notice;
        }

        [ServerRpc]
        private void RpcReport(string targetAccountId, int category, string note, NetworkConnection sender = null)
        {
            if (sender == null || !OwnerMatches(sender))
                return;

            _ = ReportAsync(targetAccountId, (ModerationCategory)category, note);
        }

        [ServerRpc]
        private void RpcRequestNotices(NetworkConnection sender = null)
        {
            if (sender == null || !OwnerMatches(sender))
                return;

            _ = DeliverNoticesAsync(sender);
        }

        private async Task ReportAsync(string targetAccountId, ModerationCategory category, string note)
        {
            ModerationService service = Service;
            if (service == null)
            {
                TargetRpcReportFailed(Owner, (int)ReportRule.BackendUnavailable);
                return;
            }

            ReportRule rule = await service.ReportAsync(
                _accountId,
                targetAccountId,
                category,
                note,
                DateTimeOffset.UtcNow.UtcDateTime.Ticks);

            if (rule != ReportRule.Ok && Owner != null)
                TargetRpcReportFailed(Owner, (int)rule);
        }

        private async Task DeliverNoticesAsync(NetworkConnection sender)
        {
            ModerationService service = Service;
            if (service == null || sender == null)
                return;

            List<ModerationNotice> notices = await service.LoadActiveNoticesAsync(
                _accountId, DateTimeOffset.UtcNow.UtcDateTime.Ticks);

            var payload = new NoticeList();
            payload.Notices.AddRange(notices);

            TargetRpcNotices(sender, JsonUtility.ToJson(payload));
        }

        [TargetRpc]
        private void TargetRpcNotice(NetworkConnection conn, string json)
        {
            ModerationNotice notice = DecodeNotice(json);
            if (notice != null)
                NoticeReceived?.Invoke(notice);
        }

        [TargetRpc]
        private void TargetRpcNotices(NetworkConnection conn, string json)
        {
            List<ModerationNotice> notices = DecodeNotices(json);
            for (int i = 0; i < notices.Count; i++)
            {
                if (notices[i] != null)
                    NoticeReceived?.Invoke(notices[i]);
            }
        }

        [TargetRpc]
        private void TargetRpcReportFailed(NetworkConnection conn, int rule)
            => ReportFailed?.Invoke((ReportRule)rule);

        private static ModerationNotice DecodeNotice(string json)
        {
            if (string.IsNullOrEmpty(json))
                return null;

            try
            {
                return JsonUtility.FromJson<ModerationNotice>(json);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        private static List<ModerationNotice> DecodeNotices(string json)
        {
            if (string.IsNullOrEmpty(json))
                return new List<ModerationNotice>();

            try
            {
                NoticeList payload = JsonUtility.FromJson<NoticeList>(json);
                return payload?.Notices ?? new List<ModerationNotice>();
            }
            catch (ArgumentException)
            {
                return new List<ModerationNotice>();
            }
        }

        private ModerationService Service
        {
            get
            {
                if (_service != null)
                    return _service;

                SocialHubService hub = SocialHubService.Instance;
                if (hub == null || hub.Rest == null)
                    return null;

                _service = new ModerationService(
                    new NakamaModerationRepository(hub.Rest),
                    () => Task.FromResult(hub.IsConnected));
                return _service;
            }
        }

        private bool OwnsSelf()
        {
            NetworkObject self = NetworkObject;
            return self != null && self.IsOwner;
        }

        private string ResolveAccountId()
        {
            PlayerGuild guild = GetComponent<PlayerGuild>();
            if (guild != null && !string.IsNullOrEmpty(guild.AccountId))
                return guild.AccountId;

            MMO.Persistence.PlayerPersistenceService service = MMO.Persistence.PlayerPersistenceService.Instance;
            int clientId = Owner != null ? Owner.ClientId : -1;
            if (service != null && service.TryGetJoin(clientId, out string accountId, out _))
                return accountId;

            return null;
        }
    }
}
