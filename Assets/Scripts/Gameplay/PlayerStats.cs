using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace MMO.Gameplay
{
    /// <summary>
    /// A player's vitals: health and mana, both owned by the server.
    ///
    /// The two values are <c>SyncVar&lt;int&gt;</c> fields rather than FishNet 2.x-style
    /// <c>[SyncVar]</c> fields. In FishNet 4.x the attribute still exists but is marked
    /// <c>[Obsolete("This no longer functions...")]</c> — a field carrying it silently replicates
    /// nothing, which is the worst possible failure for an authoritative stat. The object form is
    /// the supported API and, like the attribute, is server-write-only: a client that sets
    /// <c>Value</c> is refused by FishNet and the next delta overwrites it with the server's truth.
    ///
    /// Nothing here exposes a setter. Clients read <see cref="Health"/> and <see cref="Mana"/> for
    /// UI and, at most, *ask* for an effect through an RPC; only the mutators below, which are all
    /// <c>[Server]</c>, ever write the real numbers.
    /// </summary>
    public sealed class PlayerStats : NetworkBehaviour
    {
        public const int MaxHealth = 100;
        public const int MaxMana = 50;

        // Server-owned. Clients hold a replicated copy and are refused if they try to write.
        private readonly SyncVar<int> _health = new();
        private readonly SyncVar<int> _mana = new();

        public int Health => _health.Value;
        public int Mana => _mana.Value;

        public override void OnStartNetwork()
        {
            if (IsServer)
            {
                // A SyncVar is default-initialised, so an unreset player would spawn at zero.
                _health.Value = MaxHealth;
                _mana.Value = MaxMana;
            }

            // Both sides subscribe: clients need the change callback to drive a health bar, and the
            // server needs it to broadcast death and regeneration events.
            _health.OnChange += OnHealthChanged;
            _mana.OnChange += OnManaChanged;
        }

        public override void OnStopNetwork()
        {
            _health.OnChange -= OnHealthChanged;
            _mana.OnChange -= OnManaChanged;
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

        [Server]
        public void Heal(int amount)
        {
            _health.Value = ComputeHealedHealth(_health.Value, amount);
        }

        [Server]
        public void SpendMana(int amount)
        {
            _mana.Value = ComputeNewMana(_mana.Value, amount);
        }

        [Server]
        public void RestoreMana(int amount)
        {
            _mana.Value = ComputeRestoredMana(_mana.Value, amount);
        }

        /// <summary>
        /// Local input path for a player hurting itself. This sends an intent; it does not decide
        /// anything. The server re-checks ownership before applying anything.
        /// </summary>
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
            // NetworkConnection has no IsOwner in FishNet 4.x; compare against our owner.
            if (sender == null || !OwnerMatches(sender))
                return;

            TakeDamage(amount);
        }

        public static int ComputeNewHealth(int current, int amount)
        {
            return Mathf.Clamp(current - Mathf.Max(0, amount), 0, MaxHealth);
        }

        public static int ComputeHealedHealth(int current, int amount)
        {
            return Mathf.Clamp(current + Mathf.Max(0, amount), 0, MaxHealth);
        }

        public static int ComputeNewMana(int current, int amount)
        {
            return Mathf.Clamp(current - Mathf.Max(0, amount), 0, MaxMana);
        }

        public static int ComputeRestoredMana(int current, int amount)
        {
            return Mathf.Clamp(current + Mathf.Max(0, amount), 0, MaxMana);
        }

        private void OnHealthChanged(int previous, int current, bool asServer)
        {
            Debug.Log($"[ClientId {OwnerId}] health {previous} -> {current}");
        }

        private void OnManaChanged(int previous, int current, bool asServer)
        {
            Debug.Log($"[ClientId {OwnerId}] mana {previous} -> {current}");
        }
    }
}
