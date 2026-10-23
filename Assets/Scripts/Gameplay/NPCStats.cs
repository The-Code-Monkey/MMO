using System;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using MMO.Inventory;
using UnityEngine;

namespace MMO.Gameplay
{
    /// <summary>
    /// An NPC's vitals: a health pool owned entirely by the server.
    ///
    /// Like <see cref="PlayerStats"/>, health is a <c>SyncVar&lt;int&gt;</c> rather than a FishNet
    /// 2.x-style <c>[SyncVar]</c> field. In FishNet 4.x the attribute still exists but is marked
    /// <c>[Obsolete("This no longer functions...")]</c> — a field carrying it silently replicates
    /// nothing. The object form is the supported API and, like the attribute, is server-write-only:
    /// a client that sets <c>Value</c> is refused by FishNet and the next delta overwrites it with
    /// the server's truth.
    ///
    /// Nothing here exposes a setter. Clients read <see cref="Health"/> for a health bar and, at
    /// most, *ask* for an effect through an RPC; only the <c>[Server]</c> mutators ever write the
    /// real number.
    ///
    /// <para>
    /// As an <see cref="ITargetSource"/> it is also what a target frame binds to: the frame
    /// repaints from <see cref="Changed"/>, which the <c>SyncVar.OnChange</c> callback below
    /// raises, so an untouched target costs nothing after the frame's first paint.
    /// </para>
    /// </summary>
    public sealed class NPCStats : NetworkBehaviour, ITargetSource
    {
        public const int MaxHealth = 50;

        // Server-owned. Clients hold a replicated copy and are refused if they try to write.
        private readonly SyncVar<int> _health = new();

        public int Health => _health.Value;
        public bool IsAlive => _health.Value > 0;

        /// <summary>What a target frame titles this target with: its GameObject name.</summary>
        public string TargetName => name;

        /// <summary>
        /// The player whose damage most recently landed, or null when the last change came from
        /// the environment. Server-only state: clients read null because they never author damage.
        /// </summary>
        public PlayerInventory LastAttacker { get; private set; }

        /// <summary>
        /// Raised once on the server when health crosses from above zero to zero. The killer is
        /// carried with the event so loot and quest handlers never have to guess from state that a
        /// later respawn will clear.
        /// </summary>
        public event Action<PlayerInventory> Died;

        /// <summary>
        /// Raised on the server for damage that actually moved the health pool, carrying the
        /// source and the amount dealt. <see cref="EnemyBrain"/> uses it to build threat.
        /// </summary>
        public event Action<PlayerInventory, int> Damaged;

        /// <summary>
        /// Raised whenever replicated health changes — the <c>SyncVar.OnChange</c> callback
        /// forwarded into a plain C# event, so a bound target frame repaints on the change
        /// itself rather than sampling this component every frame.
        /// </summary>
        public event Action Changed;

        int ITargetSource.MaxHealth => MaxHealth;

        public override void OnStartNetwork()
        {
            if (IsServer)
                _health.Value = MaxHealth;

            // Both sides subscribe: clients need the change callback to drive a health bar, and the
            // server needs it to broadcast death and regeneration events.
            _health.OnChange += OnHealthChanged;
        }

        public override void OnStopNetwork()
        {
            _health.OnChange -= OnHealthChanged;
        }

        /// <summary>
        /// Applies damage on behalf of the server. Safe to call from a server-owned hit scan, an
        /// ability, or another <c>[ServerRpc]</c> — never from client code, which FishNet refuses to
        /// run.
        /// </summary>
        [Server]
        public void TakeDamage(int amount)
        {
            // Unattributed damage must not inherit credit from an earlier attacker.
            LastAttacker = null;
            _health.Value = ComputeNewHealth(_health.Value, amount);
        }

        /// <summary>
        /// Server: apply damage and name the player who dealt it. This is the attributed path
        /// combat, abilities and damage-over-time use; <see cref="TakeDamage"/> remains the
        /// unattributed mutator for environmental or direct calls.
        /// </summary>
        [Server]
        public void ApplyDamage(int amount, PlayerInventory source)
        {
            int previous = _health.Value;
            int next = ComputeNewHealth(previous, amount);
            int dealt = previous - next;
            if (dealt <= 0)
                return;

            LastAttacker = source;
            Damaged?.Invoke(source, dealt);
            _health.Value = next;
        }

        /// <summary>
        /// Server: restore a defeated NPC for a spawn controller's respawn timer. The attacker is
        /// cleared so a stale kill cannot claim the next death.
        /// </summary>
        [Server]
        public void ResetHealth()
        {
            LastAttacker = null;
            _health.Value = MaxHealth;
        }

        [Server]
        public void Heal(int amount)
        {
            _health.Value = ComputeHealedHealth(_health.Value, amount);
        }

        /// <summary>True only on the crossing into zero, not on a repeated hit to a corpse.</summary>
        public static bool IsDeathTransition(int previous, int current)
        {
            return previous > 0 && current == 0;
        }

        public static int ComputeNewHealth(int current, int amount)
        {
            return Mathf.Clamp(current - Mathf.Max(0, amount), 0, MaxHealth);
        }

        public static int ComputeHealedHealth(int current, int amount)
        {
            return Mathf.Clamp(current + Mathf.Max(0, amount), 0, MaxHealth);
        }

        private void OnHealthChanged(int previous, int current, bool asServer)
        {
            Debug.Log($"[ClientId {OwnerId}] {name} health {previous} -> {current}");
            Changed?.Invoke();

            if (asServer && IsDeathTransition(previous, current))
                Died?.Invoke(LastAttacker);
        }
    }
}