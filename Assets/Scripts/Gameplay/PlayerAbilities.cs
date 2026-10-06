using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace MMO.Gameplay
{
    /// <summary>
    /// The networked half of the equipment-injected ability framework: the player's active
    /// ability slots, the server-tracked cooldowns behind them, and the single door a client is
    /// allowed to knock on when it wants to cast.
    ///
    /// <para>
    /// The flow mirrors <see cref="PlayerStats"/> exactly: gear is equipped by the server
    /// (<see cref="EquipGear"/>), which fills a plain <see cref="AbilityCaster"/> and publishes
    /// the resulting slot names to the owning client; the client sends an intent
    /// (<see cref="RequestCast"/>); the server validates mana, distance, the per-slot cooldown
    /// and the Global Cooldown, spends the mana, books the cooldowns and only then resolves the
    /// effect into the world. A client never holds a decision it could second-guess — the local
    /// <c>AbilityCaster</c> on a pure client stays empty, and the replicated slot names exist so
    /// an action bar can be drawn, not so it can be trusted.
    /// </para>
    ///
    /// <para>
    /// Cast time is honoured by deferring the effect: an accepted cast is queued and lands the
    /// moment its cast time elapses (see <see cref="ApplyDueCasts"/>), while the channel window
    /// refuses any second cast in the meantime. Zero-cast-time skills resolve in the same call
    /// that accepted them.
    /// </para>
    /// </summary>
    public sealed class PlayerAbilities : NetworkBehaviour
    {
        private readonly AbilityCaster _caster = new();

        // One replicated name per slot: six plain string SyncVars rather than a container type,
        // so replication rides the exact pattern PlayerStats already proves works. A string is
        // the display identity of a skill — the asset behind it stays server-side, where the
        // validation happens.
        private readonly SyncVar<string> _slot0Name = new();
        private readonly SyncVar<string> _slot1Name = new();
        private readonly SyncVar<string> _slot2Name = new();
        private readonly SyncVar<string> _slot3Name = new();
        private readonly SyncVar<string> _slot4Name = new();
        private readonly SyncVar<string> _slot5Name = new();

        private PlayerStats _stats;

        /// <summary>The bar. Authoritative on the server only; a pure client's copy is empty.</summary>
        public IReadOnlyList<AbilityData> Slots => _caster.Slots;

        /// <summary>Server-side cooldown state. Read by server code, never written off-server.</summary>
        public AbilityCooldownTracker Cooldowns => _caster.Cooldowns;

        /// <summary>
        /// The ability name replicated into <paramref name="slot"/>, or an empty string when the
        /// slot is empty, out of range, or this side has not received the bar yet.
        /// </summary>
        public string GetSlotName(int slot)
        {
            string name = slot switch
            {
                0 => _slot0Name.Value,
                1 => _slot1Name.Value,
                2 => _slot2Name.Value,
                3 => _slot3Name.Value,
                4 => _slot4Name.Value,
                5 => _slot5Name.Value,
                _ => null,
            };

            // A SyncVar<string> defaults to null before the server ever writes it.
            return name ?? string.Empty;
        }

        public override void OnStartNetwork()
        {
            // Same shape as PlayerStats: the server seeds the replicated state at spawn so a
            // client that joins later still receives the bar it is entitled to see.
            if (IsServer)
                PublishSlots();
        }

        /// <summary>
        /// Server-only entry point for the inventory system: rebuilds the active slots from the
        /// gear the player just put on and republishes them to the owner.
        /// </summary>
        [Server]
        public void EquipGear(WeaponData weapon, ArmorData armor)
        {
            _caster.EquipGear(weapon, armor);
            PublishSlots();
        }

        /// <summary>
        /// Owner-side request to cast whatever sits in <paramref name="slot"/> at the aimed point.
        /// This sends intent only: the local machine decides nothing and is told nothing — the
        /// server's answer is the state change itself.
        /// </summary>
        public void RequestCast(int slot, Vector3 targetPosition)
        {
            if (!IsOwner)
                return;

            RpcRequestCast(slot, targetPosition);
        }

        /// <summary>
        /// The one wire a client has into the ability system. Ownership is re-checked here
        /// against the connection that sent the packet, because a caller's word about who it is
        /// is not evidence.
        /// </summary>
        [ServerRpc]
        private void RpcRequestCast(int slot, Vector3 targetPosition, NetworkConnection sender = null)
        {
            if (sender == null || !OwnerMatches(sender))
                return;

            PlayerStats stats = GetStats();
            if (stats == null)
                return;

            AbilityCastResult result = _caster.TryCast(
                slot, stats.Mana, transform.position, targetPosition, Time.timeAsDouble, out int manaCost);

            if (result != AbilityCastResult.Success)
                return;

            // Validated above, so this can only ever take what the ability costs.
            stats.SpendMana(manaCost);
            ApplyDueCasts();
        }

        /// <summary>
        /// The server's clock-driven half of cast times: whatever has finished channelling gets
        /// resolved into the world. A pure client runs this too and does nothing, because its
        /// queue is never filled.
        /// </summary>
        private void Update()
        {
            if (!IsServer || _caster.PendingCastCount == 0)
                return;

            ApplyDueCasts();
        }

        private void ApplyDueCasts()
        {
            List<AbilityCastEffect> due = _caster.TakeDueEffects(Time.timeAsDouble);

            for (int i = 0; i < due.Count; i++)
                ApplyEffect(due[i]);
        }

        /// <summary>
        /// The only impure step: turns an accepted cast into vitals changes on the players it
        /// touches. Everything deciding *who* and *how hard* is pure and tested; this loop just
        /// carries the verdict out.
        /// </summary>
        private void ApplyEffect(AbilityCastEffect cast)
        {
            AbilityData ability = cast.Ability;
            if (ability == null)
                return;

            PlayerStats caster = GetStats();

            if (ability.Logic == AbilityLogic.SelfHeal)
            {
                if (caster != null)
                    caster.Heal(AbilityEffects.ComputePower(ability));
                return;
            }

            Vector3 origin = AbilityEffects.ResolveOrigin(ability.Logic, transform.position, cast.TargetPosition);
            List<PlayerStats> victims = AbilityEffects.SelectVictims(
                origin, ability.EffectRadius, caster, FindObjectsByType<PlayerStats>(FindObjectsSortMode.None));

            int power = AbilityEffects.ComputePower(ability);
            for (int i = 0; i < victims.Count; i++)
                victims[i].TakeDamage(power);
        }

        private PlayerStats GetStats()
        {
            if (_stats == null)
                _stats = GetComponent<PlayerStats>();

            return _stats;
        }

        /// <summary>Pushes the current bar into the replicated slot names, server-side only.</summary>
        private void PublishSlots()
        {
            IReadOnlyList<AbilityData> slots = _caster.Slots;

            _slot0Name.Value = NameAt(slots, 0);
            _slot1Name.Value = NameAt(slots, 1);
            _slot2Name.Value = NameAt(slots, 2);
            _slot3Name.Value = NameAt(slots, 3);
            _slot4Name.Value = NameAt(slots, 4);
            _slot5Name.Value = NameAt(slots, 5);
        }

        private static string NameAt(IReadOnlyList<AbilityData> slots, int slot)
        {
            AbilityData ability = slot >= 0 && slot < slots.Count ? slots[slot] : null;
            return ability == null ? string.Empty : ability.AbilityName;
        }
    }
}
