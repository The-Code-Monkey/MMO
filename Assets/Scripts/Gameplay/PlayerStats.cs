using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace MMO.Gameplay
{
    public sealed class PlayerStats : NetworkBehaviour
    {
        private const int MaxHealth = 100;

        // Server owns the authoritative value and replicates; clients never write.
        private readonly SyncVar<int> _health = new(MaxHealth);

        public int Health => _health.Value;

        public override void OnNetworkSpawn()
        {
            if (!IsServer)
                return;

            _health.OnChange += OnHealthChanged;
        }

        public override void OnNetworkDespawn()
        {
            if (!IsServer)
                return;

            _health.OnChange -= OnHealthChanged;
        }

        /// Called from local input only. The client never changes health itself.
        public void RequestDamage(int amount)
        {
            if (!IsOwner)
                return;

            RpcRequestDamage(amount);
        }

        [ServerRpc]
        private void RpcRequestDamage(int amount, NetworkConnection sender = null)
        {
            // Server is the only writer. Validate here, not on the client.
            if (sender == null || sender != Owner)
                return;

            _health.Value = Mathf.Clamp(_health.Value - Mathf.Max(0, amount), 0, MaxHealth);
        }

        private void OnHealthChanged(int previous, int current, bool asServer)
        {
            Debug.Log($"[ClientId {OwnerId}] health {previous} -> {current}");
        }
    }
}
