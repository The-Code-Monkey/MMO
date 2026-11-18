using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace MMO.Gameplay
{
    /// <summary>
    /// A server-authoritative punching bag. Health is a <c>SyncVar&lt;int&gt;</c> owned by the
    /// server — clients read it for a health bar and are refused if they try to write — so the
    /// dummy is driven exactly like <see cref="PlayerStats"/> and demonstrates the same pattern to
    /// anyone authoring the next damageable.
    ///
    /// The health bar is the point of the whole exercise: a player clicks this cube, and if the
    /// server validates the attack the <see cref="SyncVar{T}"/> replicated the drop to every
    /// client's UI. Non-<see cref="PlayerCombat"/> damage still lands here, too, since
    /// <see cref="TakeDamage"/> only accepts the fact that a hit was valid.
    /// </summary>
    public sealed class TargetDummy : NetworkBehaviour
    {
        public const int MaxHealth = 100;

        // Server-owned. Clients hold a replicated copy and are refused if they try to write.
        private readonly SyncVar<int> _health = new();

        public int Health => _health.Value;

        public override void OnStartNetwork()
        {
            if (IsServer)
            {
                // A SyncVar is default-initialised, so an unreset dummy would spawn at zero.
                _health.Value = MaxHealth;
            }

            // Both sides subscribe: clients need the change callback to drive a health bar.
            _health.OnChange += OnHealthChanged;
        }

        public override void OnStopNetwork()
        {
            _health.OnChange -= OnHealthChanged;
        }

        /// <summary>
        /// Applies damage on behalf of the server. Safe to call from a server-owned hit scan,
        /// an ability, or another <c>[ServerRpc]</c> — never from client code, which FishNet
        /// refuses to run.
        /// </summary>
        [Server]
        public void TakeDamage(int amount)
        {
            _health.Value = ComputeNewHealth(_health.Value, amount);
        }

        /// <summary>
        /// The click's local effect: resolve the local player's combat handler and ask it to attack
        /// this dummy. Does nothing until a local player actually exists — e.g. on a dedicated
        /// server, or before spawn — which is exactly how a click on a scene-placed dummy behaves.
        /// </summary>
        /// <remarks>
        /// The <see cref="NetworkObject"/> access is deliberately null-guarded: it is null until
        /// FishNet has initialized this behaviour (at registration or spawn), and an uninitialized
        /// dummy cannot hold a reference to a manager yet.
        /// </remarks>
        public void RequestAttackFromLocalPlayer()
        {
            NetworkObject self = NetworkObject;
            if (self == null || self.NetworkManager == null)
                return;

            PlayerCombat combat = PlayerCombat.FindLocal(self.NetworkManager);
            if (combat == null)
                return;

            combat.RequestAttack(self);
        }

        public static int ComputeNewHealth(int current, int amount)
        {
            return Mathf.Clamp(current - Mathf.Max(0, amount), 0, MaxHealth);
        }

        private void OnHealthChanged(int previous, int current, bool asServer)
        {
            Debug.Log($"[TargetDummy {name}] health {previous} -> {current}");
        }
    }
}