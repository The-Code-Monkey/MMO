using System;
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
    /// <see cref="PlayerInventory"/> through <see cref="ApplyEquipmentBonuses"/>, which recalculates the
    /// caps from what is actually worn and clamps vitals down when a cap shrinks.
    ///
    /// <para>
    /// The four values a HUD shows are published as <see cref="VitalsChanged"/>, raised from the
    /// <c>SyncVar.OnChange</c> callbacks below rather than sampled from an <c>Update</c>. A bound
    /// health bar therefore repaints exactly when the server's numbers arrive — once per real
    /// change, and not at all in the frames between them.
    /// </para>
    /// </summary>
    public sealed class PlayerStats : NetworkBehaviour, IVitalsSource
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

        /// <summary>
        /// Raised whenever replicated vitals change: health or mana moving, or a cap shifting
        /// because gear was equipped. This is the whole UI update path — a subscriber repaints
        /// from here, the FishNet <c>OnChange</c> callbacks below forward into it, and nothing
        /// anywhere samples these values on a frame timer. Fires on the server as well as on
        /// clients, so a host's own HUD and the server's bookkeeping share one hook.
        /// </summary>
        public event Action VitalsChanged;

        /// <summary>
        /// Raised on the server when the level actually changes (a delta, not a repeat), the hook
        /// the persistence layer binds to: "levelled up → push the new state to the backend."
        /// </summary>
        public event Action LevelChanged;

        /// <summary>
        /// Raised on the server the moment health first reaches zero — the transition, not a
        /// repeated zero, so a death runs exactly once. <see cref="PlayerDeath"/> is the
        /// subscriber that turns this event into a zone-tier consequence. A health of zero
        /// arriving through <see cref="ApplySave"/> does not fire it: loading a dead-but-saved
        /// player is a restore, not a death.
        /// </summary>
        public event Action Died;

        // Set while ApplySave writes the loaded value, so a restored zero health cannot be
        // mistaken for a fresh death and run the death rules on spawn.
        private bool _applyingSave;

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
            _level.OnChange += OnLevelChanged;

            // The caps are HUD state too: equipment raising or lowering a maximum has to repaint
            // the bar's fill even when the current value itself did not move.
            _maxHealthBonus.OnChange += OnMaxChanged;
            _maxManaBonus.OnChange += OnMaxChanged;
        }

        public override void OnStopNetwork()
        {
            _health.OnChange -= OnHealthChanged;
            _mana.OnChange -= OnManaChanged;
            _level.OnChange -= OnLevelChanged;
            _maxHealthBonus.OnChange -= OnMaxChanged;
            _maxManaBonus.OnChange -= OnMaxChanged;
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
        /// Server: overwrite vitals and level from a loaded save record, clamped to the caps in
        /// force. Equipment bonuses are applied afterwards by <c>PlayerInventory.Restore</c>, so a
        /// later recalculation may tighten these again — never loosen them.
        /// </summary>
        [Server]
        public void ApplySave(int health, int mana, int level)
        {
            _applyingSave = true;
            try
            {
                _level.Value = Mathf.Max(0, level);
                _health.Value = Mathf.Clamp(health, 0, EffectiveMaxHealth);
                _mana.Value = Mathf.Clamp(mana, 0, EffectiveMaxMana);
            }
            finally
            {
                _applyingSave = false;
            }
        }

        /// <summary>
        /// Server: return the player to full health and mana. The minimal respawn the death
        /// rules ask for — no position reset and no spawn-point selection yet, because no
        /// movement or spawn-point system exists to consult. A later respawn system can replace
        /// this call without touching the death rules.
        /// </summary>
        [Server]
        public void Respawn()
        {
            _health.Value = EffectiveMaxHealth;
            _mana.Value = EffectiveMaxMana;
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
            VitalsChanged?.Invoke();

            // Only the server resolves death, and only on the crossing into zero: a restore that
            // starts at zero, or a further hit on an already-dead player, must not re-run it.
            if (asServer && !_applyingSave && previous > 0 && current == 0)
                Died?.Invoke();
        }

        private void OnManaChanged(int previous, int current, bool asServer)
        {
            Debug.Log($"[ClientId {OwnerId}] mana {previous} -> {current}");
            VitalsChanged?.Invoke();
        }

        private void OnLevelChanged(int previous, int current, bool asServer)
        {
            if (!asServer || previous == current)
                return;

            LevelChanged?.Invoke();
        }

        private void OnMaxChanged(int previous, int current, bool asServer)
        {
            VitalsChanged?.Invoke();
        }
    }
}
