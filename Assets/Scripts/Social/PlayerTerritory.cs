using System;
using System.Threading.Tasks;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Transporting;
using MMO.Gameplay;
using MMO.Net;
using MMO.Persistence;
using UnityEngine;

namespace MMO.Social
{
    /// <summary>
    /// Territory capture intents for one player. The server finds the scene <see cref="ClaimFlag"/>
    /// containing the player's authoritative position, validates through
    /// <see cref="SocialHubService"/> and broadcasts the new owner; clients only paint it.
    /// </summary>
    public sealed class PlayerTerritory : NetworkBehaviour
    {
        private string _accountId;

        /// <summary>Raised on the acting client when the server refuses a capture.</summary>
        public event Action<TerritoryRule> RuleFailed;

        public override void OnStartNetwork()
        {
            if (!IsServer)
                return;

            _accountId = ResolveAccountId();
            SocialHubService hub = SocialHubService.Instance;
            if (hub != null)
                hub.TerritoryChanged += OnTerritoryChanged;

            _ = LoadAllAsync();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();

            if (ClientManager != null)
                ClientManager.RegisterBroadcast<TerritoryStateBroadcast>(OnTerritoryBroadcast);

            if (IsOwner)
                RpcRequestState();
        }

        public override void OnStopNetwork()
        {
            if (IsServer)
            {
                SocialHubService hub = SocialHubService.Instance;
                if (hub != null)
                    hub.TerritoryChanged -= OnTerritoryChanged;
            }
        }

        public override void OnStopClient()
        {
            if (ClientManager != null)
                ClientManager.UnregisterBroadcast<TerritoryStateBroadcast>(OnTerritoryBroadcast);

            base.OnStopClient();
        }

        /// <summary>Client intent: capture the flag the server says this player is standing in.</summary>
        public void RequestCapture()
        {
            if (OwnsSelf())
                RpcCapture();
        }

        [ServerRpc]
        private void RpcRequestState(NetworkConnection sender = null)
        {
            if (sender == null || !OwnerMatches(sender))
                return;

            _ = LoadAllAsync();
        }

        [ServerRpc]
        private void RpcCapture(NetworkConnection sender = null)
        {
            if (sender == null || !OwnerMatches(sender) || SocialHubService.Instance == null)
                return;

            PlayerMover mover = GetComponent<PlayerMover>();
            Vector3 position = mover != null ? mover.LastAuthoritativePosition : transform.position;
            ClaimFlag flag = FindFlagAt(position);
            if (flag != null)
                _ = CaptureAsync(flag);
        }

        private async Task CaptureAsync(ClaimFlag flag)
        {
            SocialHubService hub = SocialHubService.Instance;
            if (hub == null)
                return;

            TerritoryRule rule = await hub.CaptureTerritoryAsync(
                _accountId,
                flag.TerritoryId,
                flag.VulnerabilityWindow,
                DateTimeOffset.UtcNow);

            if (rule != TerritoryRule.Ok)
                TargetNotify(Owner, (int)rule);
        }

        private async Task LoadAllAsync()
        {
            SocialHubService hub = SocialHubService.Instance;
            if (hub == null)
                return;

            ClaimFlag[] flags = FindObjectsByType<ClaimFlag>(FindObjectsSortMode.None);
            for (int i = 0; i < flags.Length; i++)
            {
                ClaimFlag flag = flags[i];
                if (flag == null || string.IsNullOrEmpty(flag.TerritoryId))
                    continue;

                TerritoryRecord record = await hub.LoadTerritoryAsync(flag.TerritoryId);
                string owner = record?.OwnerGuildId;
                flag.SetOwner(owner);
                Broadcast(flag.TerritoryId, owner);
            }
        }

        private void OnTerritoryChanged(TerritoryRecord record)
        {
            if (!IsServer || record == null)
                return;

            ClaimFlag flag = FindFlag(record.TerritoryId);
            flag?.SetOwner(record.OwnerGuildId);
            Broadcast(record.TerritoryId, record.OwnerGuildId);
        }

        [Server]
        private void Broadcast(string territoryId, string ownerGuildId)
            => ServerManager.Broadcast(
                new TerritoryStateBroadcast
                {
                    TerritoryId = territoryId,
                    OwnerGuildId = ownerGuildId,
                },
                requireAuthenticated: true);

        private void OnTerritoryBroadcast(TerritoryStateBroadcast message, Channel channel)
            => FindFlag(message.TerritoryId)?.SetOwner(message.OwnerGuildId);

        [TargetRpc]
        private void TargetNotify(NetworkConnection conn, int rule)
            => RuleFailed?.Invoke((TerritoryRule)rule);

        private static ClaimFlag FindFlagAt(Vector3 position)
        {
            ClaimFlag[] flags = FindObjectsByType<ClaimFlag>(FindObjectsSortMode.None);
            for (int i = 0; i < flags.Length; i++)
            {
                if (flags[i] != null && flags[i].Contains(position))
                    return flags[i];
            }

            return null;
        }

        private static ClaimFlag FindFlag(string territoryId)
        {
            if (string.IsNullOrEmpty(territoryId))
                return null;

            ClaimFlag[] flags = FindObjectsByType<ClaimFlag>(FindObjectsSortMode.None);
            for (int i = 0; i < flags.Length; i++)
            {
                if (flags[i] != null && flags[i].TerritoryId == territoryId)
                    return flags[i];
            }

            return null;
        }

        private bool OwnsSelf()
        {
            NetworkObject self = NetworkObject;
            return self != null && self.IsOwner;
        }

        private string ResolveAccountId()
        {
            PlayerPersistenceService service = PlayerPersistenceService.Instance;
            int clientId = Owner != null ? Owner.ClientId : -1;
            if (service != null && service.TryGetJoin(clientId, out string accountId, out _))
                return accountId;

            return null;
        }
    }
}
