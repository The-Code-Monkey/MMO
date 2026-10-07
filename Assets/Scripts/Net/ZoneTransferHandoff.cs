using System;
using System.Threading;
using System.Threading.Tasks;

namespace MMO.Net
{
    /// <summary>
    /// The server-side story of one zone handoff, with no Unity or FishNet in it: flush the player's
    /// save, mint a token bound to this account and destination, and return the redirect the client
    /// should act on. The order is the whole point — a player may never be told to leave before
    /// their state is durable — so the save is awaited first and a failed save aborts the handoff
    /// (returns null) rather than risk the target loading a stale or fresh character.
    ///
    /// <para>
    /// Kept separate from <see cref="Gameplay.ZoneTransferTrigger"/> so the sequence, the abort and
    /// the token contents can be unit-tested without a physics scene or a live server;
    /// the trigger only supplies the player's account id and the persistence flush.
    /// </para>
    /// </summary>
    public sealed class ZoneTransferHandoff
    {
        /// <summary>Player-facing reason used when the trigger has none of its own.</summary>
        public const string DefaultReason = "Zone transition";

        private readonly IZoneTransferTokenStore _store;
        private readonly string _targetHost;
        private readonly ushort _targetPort;
        private readonly string _targetServerId;
        private readonly string _sourceServerId;
        private readonly Func<string, Task<bool>> _forceSave;

        /// <param name="store">Issuer of the transfer token.</param>
        /// <param name="targetHost">Destination address the client is told to connect to.</param>
        /// <param name="targetPort">Destination port.</param>
        /// <param name="targetServerId">Stable destination id, recorded in the token.</param>
        /// <param name="sourceServerId">Stable issuer id, recorded in the token.</param>
        /// <param name="forceSave">
        /// Flushes one account's save and reports whether it landed. The trigger passes
        /// <see cref="Persistence.PlayerPersistenceService.FlushAsync(string)"/>.
        /// </param>
        public ZoneTransferHandoff(
            IZoneTransferTokenStore store,
            string targetHost,
            ushort targetPort,
            string targetServerId,
            string sourceServerId,
            Func<string, Task<bool>> forceSave)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _targetHost = string.IsNullOrWhiteSpace(targetHost)
                ? throw new ArgumentException("A target host is required.", nameof(targetHost))
                : targetHost;
            _targetPort = targetPort;
            _targetServerId = targetServerId ?? string.Empty;
            _sourceServerId = sourceServerId ?? string.Empty;
            _forceSave = forceSave ?? throw new ArgumentNullException(nameof(forceSave));
        }

        /// <summary>
        /// Runs the handoff. Returns the redirect to send, or null when the save could not be
        /// completed (or the account id is unusable) — in which case the player stays put.
        /// </summary>
        public async Task<ZoneTransferBroadcast?> RunAsync(string accountId, string reason = null, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(accountId))
                return null;

            if (!await _forceSave(accountId))
                return null;

            var token = new ZoneTransferToken
            {
                AccountId = accountId,
                SourceServerId = _sourceServerId,
                TargetServerId = _targetServerId,
                TargetHost = _targetHost,
                TargetPort = _targetPort,
            };

            string tokenString = await _store.IssueAsync(token, ct);
            if (string.IsNullOrEmpty(tokenString))
                return null;

            return new ZoneTransferBroadcast
            {
                TargetHost = _targetHost,
                TargetPort = _targetPort,
                TargetServerId = _targetServerId,
                TransferToken = tokenString,
                Reason = string.IsNullOrWhiteSpace(reason) ? DefaultReason : reason,
            };
        }
    }
}
