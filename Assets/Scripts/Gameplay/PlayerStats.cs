using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace MMO.Gameplay
{
    /// <summary>
    /// A player's stats: vitals (health and mana), level, and the bonus caps equipment grants —
    /// all owned by the server.
    ///
    /// The values are <c>SyncVar&lt;int&gt;</c> fields rather than FishNet 2.x-style
    /// <c>[SyncVar]</c> fields. In FishNet 4.x the attribute still exists but is marked
    /// <c>[Obsolete("This no longer functions...")]</c> — a field carrying it silently replicates
    /// nothing, which is the worst possible failure for an authoritative stat. The object form is
    /// the supported API and, like the attribute, is server-write-only: a client that sets
    /// <c>Value</c> is refused by FishNet and the next delta overwrites it with the server's truth.
    ///
    /// Nothing here exposes a setter. Clients read <see cref="Health"/>, <see cref="Mana"/>,
    /// <see cref="Level"/> and the equipment caps for UI and, at most, *ask* for an effect
    /// through an RPC; only the mutators below, which are all <c>[Server]</c>, ever write the
    /// real numbers. Equipment bonuses are not written by this class either: it exposes them to
    /// <c>PlayerInventory</c> through <see cref="ApplyEquipmentBonuses"/>, which recalculates the
    /// caps from what is actually worn and clamps vitals down when a cap shrinks.
    /// </summary>
    public sealed class PlayerStats : NetworkBehaviour
    {
        public const int MaxHealth = 100;
        public const int MaxMana = 50;

        /// <summary>Level a player spawns at, until a progression system says otherwise.</summary>
        public const int DefaultLevel = 1;

        // Server-owned. Clients hold a replicated copy and are refused if they try to write.
        private readonly SyncVar<int> _health = new();
        private readonly SyncVar<int> _mana = new();
        private readonly SyncVar<int> _level = new();
        private readonly SyncVar<int> _maxHealthBonus = new();
        private readonly SyncVar<int> _maxManaBonus = new();

        public int Health => _health.Value;
        public int Mana => _mana.Value;
        public int Level => _level.Value;

        /// <summary>Maximum health currently granted by equipped items.</summary>
        public int MaxHealthBonus => _maxHealthBonus.Value;

        /// <summary>Maximum mana currently granted by equipped items.</summary>
        public int MaxManaBonus => _maxManaBonus.Value;

        /// <summary>Health cap in force: the base <see cref="MaxHealth"/> plus equipment bonuses.</summary>
        public int EffectiveMaxHealth => MaxHealth + _maxHealthBonus.Value;

        /// <summary>Mana cap in force: the base <see cref="MaxMana"/> plus equipment bonuses.</summary>
        public int EffectiveMaxMana => MaxMana + _maxManaBonus.Value;

        public override void OnStartNetwork()
        {
            if (IsServer)
            {
                // A SyncVar is default-initialised, so an unreset player would spawn at zero.
                _health.Value = MaxHealth;
                _mana.Value = MaxMana;
                _level.Value = DefaultLevel;
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
            _health.Value = ComputeNewHealth(_health.Value, amount, EffectiveMaxHealth);
        }

        [Server]
        public void Heal(int amount)
        {
            _health.Value = ComputeHealedHealth(_health.Value, amount, EffectiveMaxHealth);
        }

        [Server]
        public void SpendMana(int amount)
        {
            _mana.Value = ComputeNewMana(_mana.Value, amount, EffectiveMaxMana);
        }

        [Server]
        public void RestoreMana(int amount)
        {
            _mana.Value = ComputeRestoredMana(_mana.Value, amount, EffectiveMaxMana);
        }

        /// <summary>
        /// Raises or lowers the player's level on behalf of the server. Levels gate equipment
        /// through <c>InventoryValidator.CanEquip</c>, so a writable level is what lets a
        /// progression system eventually unlock higher-tier items.
        /// </summary>
        [Server]
        public void SetLevel(int level)
        {
            _level.Value = Mathf.Max(0, level);
        }

        /// <summary>
        /// Replaces the equipment-derived stat bonuses with newly summed ones. Called by
        /// <c>PlayerInventory</c> after every equip or unequip — the recalculation step of the
        /// container system — so the caps always describe what is actually worn, never what a
        /// client claims to be wearing.
        ///
        /// A cap that shrinks pulls the current value down with it: health or mana floating
        /// above its own maximum is state the server would then have to remember not to heal.
        /// </summary>
        [Server]
        public void ApplyEquipmentBonuses(int maxHealthBonus, int maxManaBonus)
        {
            _maxHealthBonus.Value = Mathf.Max(0, maxHealthBonus);
            _maxManaBonus.Value = Mathf.Max(0, maxManaBonus);

            _health.Value = ComputeNewHealth(_health.Value, 0, EffectiveMaxHealth);
            _mana.Value = ComputeNewMana(_mana.Value, 0, EffectiveMaxMana);
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

        // The optional cap defaults to the base maximum. Callers holding an equipment bonus pass
        // the effective cap instead, so one helper serves an uncapped player and a geared one
        // without a second method to test. A negative cap is treated as zero rather than handed
        // to Mathf.Clamp with its minimum above its maximum.
        public static int ComputeNewHealth(int current, int amount, int maxHealth = MaxHealth)
        {
            return Mathf.Clamp(current - Mathf.Max(0, amount), 0, Mathf.Max(0, maxHealth));
        }

        public static int ComputeHealedHealth(int current, int amount, int maxHealth = MaxHealth)
        {
            return Mathf.Clamp(current + Mathf.Max(0, amount), 0, Mathf.Max(0, maxHealth));
        }

        public static int ComputeNewMana(int current, int amount, int maxMana = MaxMana)
        {
            return Mathf.Clamp(current - Mathf.Max(0, amount), 0, Mathf.Max(0, maxMana));
        }

        public static int ComputeRestoredMana(int current, int amount, int maxMana = MaxMana)
        {
            return Mathf.Clamp(current + Mathf.Max(0, amount), 0, Mathf.Max(0, maxMana));
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
