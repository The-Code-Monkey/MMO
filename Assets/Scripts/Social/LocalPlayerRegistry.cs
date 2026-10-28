using System;
using System.Collections.Generic;
using MMO.Auction;

namespace MMO.Social
{
    /// <summary>
    /// The zone's local-player roster and their attached FishNet view handles. Owns registration,
    /// chat/friend delivery callbacks, guild views and wallets so the hub does not hold five
    /// unrelated dictionaries.
    /// </summary>
    internal sealed class LocalPlayerRegistry : ILocalRoster
    {
        private readonly Dictionary<string, LocalPlayerHandle> _players = new Dictionary<string, LocalPlayerHandle>();
        private readonly Dictionary<string, PlayerGuild> _guilds = new Dictionary<string, PlayerGuild>();
        private readonly Dictionary<string, PlayerWallet> _wallets = new Dictionary<string, PlayerWallet>();
        private readonly Func<string> _zoneId;

        public LocalPlayerRegistry(Func<string> zoneId)
        {
            _zoneId = zoneId;
        }

        public event Action<LocalPlayerHandle> Registered;
        public event Action<string> Unregistered;

        public int Count => _players.Count;

        public string LocalZoneId => _zoneId != null ? _zoneId() : null;

        public IReadOnlyList<LocalPlayerHandle> Local
        {
            get
            {
                var list = new List<LocalPlayerHandle>(_players.Count);
                foreach (LocalPlayerHandle handle in _players.Values)
                    list.Add(handle);

                return list;
            }
        }

        public List<string> AccountIds() => new List<string>(_players.Keys);

        public bool Contains(string accountId)
            => !string.IsNullOrEmpty(accountId) && _players.ContainsKey(accountId);

        public bool TryGet(string accountId, out LocalPlayerHandle handle)
            => _players.TryGetValue(accountId ?? string.Empty, out handle);

        public void Register(string accountId, string playerName)
        {
            if (string.IsNullOrEmpty(accountId))
                return;

            var local = new LocalPlayerHandle
            {
                AccountId = accountId,
                PlayerName = string.IsNullOrEmpty(playerName) ? accountId : playerName,
            };

            _players[accountId] = local;
            Registered?.Invoke(local);
        }

        public void Unregister(string accountId)
        {
            if (string.IsNullOrEmpty(accountId) || !_players.Remove(accountId))
                return;

            Unregistered?.Invoke(accountId);
        }

        public string PartyOf(string accountId)
            => _players.TryGetValue(accountId ?? string.Empty, out LocalPlayerHandle handle) ? handle.PartyId : null;

        public string NameOf(string accountId)
            => _players.TryGetValue(accountId ?? string.Empty, out LocalPlayerHandle handle)
                ? handle.PlayerName
                : accountId;

        public string GetPlayerName(string accountId)
            => _players.TryGetValue(accountId ?? string.Empty, out LocalPlayerHandle local)
                ? local.PlayerName
                : accountId;

        public void AttachChat(string accountId, Action<string> deliverChat)
        {
            if (_players.TryGetValue(accountId ?? string.Empty, out LocalPlayerHandle handle))
                handle.DeliverChat = deliverChat;
        }

        public void DetachChat(string accountId)
        {
            if (_players.TryGetValue(accountId ?? string.Empty, out LocalPlayerHandle handle))
                handle.DeliverChat = null;
        }

        public void AttachFriends(string accountId, Action<string> deliverSnapshot, Action<int> notifyEvent)
        {
            if (_players.TryGetValue(accountId ?? string.Empty, out LocalPlayerHandle handle))
            {
                handle.DeliverFriends = deliverSnapshot;
                handle.NotifyFriendEvent = notifyEvent;
            }
        }

        public void DetachFriends(string accountId)
        {
            if (_players.TryGetValue(accountId ?? string.Empty, out LocalPlayerHandle handle))
            {
                handle.DeliverFriends = null;
                handle.NotifyFriendEvent = null;
            }
        }

        public void AttachGuild(string accountId, PlayerGuild guild)
        {
            if (!string.IsNullOrEmpty(accountId) && guild != null)
                _guilds[accountId] = guild;
        }

        public void DetachGuild(string accountId)
        {
            if (!string.IsNullOrEmpty(accountId))
                _guilds.Remove(accountId);
        }

        public bool TryGetGuild(string accountId, out PlayerGuild guild)
            => _guilds.TryGetValue(accountId ?? string.Empty, out guild);

        public void AttachWallet(string accountId, PlayerWallet wallet)
        {
            if (!string.IsNullOrEmpty(accountId) && wallet != null)
                _wallets[accountId] = wallet;
        }

        public void DetachWallet(string accountId)
        {
            if (!string.IsNullOrEmpty(accountId))
                _wallets.Remove(accountId);
        }

        public bool TryGetWallet(string accountId, out PlayerWallet wallet)
            => _wallets.TryGetValue(accountId ?? string.Empty, out wallet);
    }
}
