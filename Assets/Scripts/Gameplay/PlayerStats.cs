using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace MMO.Gameplay
{
    public sealed class PlayerStats : NetworkBehaviour
    {
        public const int MaxHealth = 100;

        // Clients own a copy; the server owns the authoritative value and
        // replicates deltas automatically. Never write .Value off-server.
        private readonly NetworkVariable<int> _health = new(MaxHealth);

        public int Health => _health.Value;

        public override void OnNetworkSpawn()
        {
            if (!IsServer)
                return;

            _health.OnValueChange += OnHealthChanged;
        }

        public override void OnNetworkDespawn()
        {
            if (!IsServer)
                return;

            _health.OnValueChange -= OnHealthChanged;
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
            if (sender == null || !sender.IsOwner)
                return;

            _health.Value = ComputeNewHealth(_health.Value, amount);
        }

        public static int ComputeNewHealth(int current, int amount)
        {
            return Mathf.Clamp(current - Mathf.Max(0, amount), 0, MaxHealth);
        }

        private void OnHealthChanged(int previous, int current)
        {
            Debug.Log($"[ClientId {OwnerClientId}] health {previous} -> {current}");
        }
    }
}
