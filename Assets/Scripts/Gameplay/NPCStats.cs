using System;
using FishNet.Object;
using FishNet.Object.Synchronizing;
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
            _health.Value = ComputeNewHealth(_health.Value, amount);
        }

        [Server]
        public void Heal(int amount)
        {
            _health.Value = ComputeHealedHealth(_health.Value, amount);
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
        }
    }
}