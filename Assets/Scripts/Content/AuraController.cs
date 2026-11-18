using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using MMO.Gameplay;
using MMO.Inventory;
using UnityEngine;

namespace MMO.Content
{
    /// <summary>
    /// The server-authoritative aura shell on a player or enemy. It owns the active
    /// <see cref="AuraState"/> list, applies periodic ticks through the target's vitals, gates
    /// actions and movement, and publishes the visible set for a buff bar.
    ///
    /// Clients never apply an aura themselves; they only receive <see cref="Visible"/> and read
    /// server-written vitals. The pure decisions live in <see cref="AuraRules"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AuraController : NetworkBehaviour
    {
        private readonly List<AuraState> _active = new();
        private readonly SyncList<AuraSnapshot> _visible = new();

        private PlayerStats _playerStats;
        private NPCStats _npcStats;
        private PlayerMover _mover;
        private bool _dead;

        /// <summary>The replicated buff-bar set; empty on a server with no active auras.</summary>
        public IReadOnlyList<AuraSnapshot> Visible => _visible;

        /// <summary>True while a stun is active. Server-side gate for abilities, gathering and attacks.</summary>
        public bool BlocksActions => AuraRules.BlocksActions(_active);

        /// <summary>True while a stun or root is active. Server-side gate for movement.</summary>
        public bool BlocksMovement => AuraRules.BlocksMovement(_active);

        public override void OnStartNetwork()
        {
            if (!IsServer)
                return;

            _playerStats = GetComponent<PlayerStats>();
            _npcStats = GetComponent<NPCStats>();
            _mover = GetComponent<PlayerMover>();

            if (_playerStats != null)
                _playerStats.Died += OnPlayerDied;

            if (_npcStats != null)
                _npcStats.Died += OnNpcDied;
        }

        public override void OnStopNetwork()
        {
            if (_playerStats != null)
                _playerStats.Died -= OnPlayerDied;

            if (_npcStats != null)
                _npcStats.Died -= OnNpcDied;
        }

        /// <summary>
        /// Server: apply or refresh an aura. A max-stack of one refreshes; a higher cap stacks.
        /// The source is kept so a damage-over-time kill can credit the player who applied it.
        /// </summary>
        [Server]
        public void Apply(AuraData data, PlayerInventory source = null)
        {
            if (data == null)
                return;

            double now = Time.timeAsDouble;
            int index = Find(data);

            if (index >= 0)
            {
                AuraState refreshed = AuraRules.Apply(_active[index], data, now);
                refreshed.Source = source != null ? source : _active[index].Source;
                _active[index] = refreshed;
            }
            else
            {
                AuraState state = AuraRules.Apply(default, data, now);
                state.Source = source;
                _active.Add(state);
            }

            RecalculateBonuses();
            PublishVisible();
        }

        /// <summary>Server: remove every aura, used when the target dies.</summary>
        [Server]
        public void ClearAll()
        {
            if (_active.Count == 0)
                return;

            _active.Clear();
            RecalculateBonuses();
            PublishVisible();
        }

        private void Update()
        {
            if (!IsServer)
                return;

            if (_dead)
            {
                _dead = false;
                ClearAll();
                return;
            }

            if (_active.Count == 0)
                return;

            double now = Time.timeAsDouble;
            bool structureChanged = false;

            for (int i = _active.Count - 1; i >= 0; i--)
            {
                AuraState state = _active[i];
                AuraTickResult result;

                do
                {
                    result = AuraRules.Tick(state, now);
                    state = result.State;

                    if (result.Ticked)
                        ApplyTick(result);

                    if (_dead || result.Expired)
                        break;
                }
                while (result.Ticked && state.NextTickAt <= now);

                if (_dead)
                    break;

                if (result.Expired)
                {
                    _active.RemoveAt(i);
                    structureChanged = true;
                }
                else
                {
                    _active[i] = state;
                }
            }

            if (_dead)
            {
                _dead = false;
                ClearAll();
                return;
            }

            if (structureChanged)
            {
                RecalculateBonuses();
                PublishVisible();
            }
        }

        private void ApplyTick(AuraTickResult tick)
        {
            if (tick.Damage > 0)
            {
                // The attributed NPC path keeps poison kill credit on the applier.
                if (_npcStats != null)
                    _npcStats.ApplyDamage(tick.Damage, tick.State.Source);
                else if (_playerStats != null)
                {
                    // A damage-over-time tick is still player-versus-player damage when a player
                    // applied it: the same tier-and-flag verdict gates every tick, so an aura
                    // cannot outlive the target's consent.
                    if (!CanHarmFromSource(tick.State.Source))
                        return;

                    _playerStats.TakeDamage(tick.Damage);
                }

                return;
            }

            if (tick.Heal > 0)
            {
                if (_npcStats != null)
                    _npcStats.Heal(tick.Heal);
                else if (_playerStats != null)
                    _playerStats.Heal(tick.Heal);
            }
        }

        /// <summary>
        /// True when the applier of this aura may still damage the wearer. An NPC applier, or a
        /// wearer with no <see cref="PlayerPvP"/> (a bare test object), is not PvP and keeps the
        /// pre-PvP behaviour.
        /// </summary>
        private bool CanHarmFromSource(PlayerInventory source)
        {
            PlayerPvP attacker = source != null ? source.GetComponent<PlayerPvP>() : null;
            PlayerPvP target = GetComponent<PlayerPvP>();
            if (attacker == null || target == null)
                return true;

            return attacker.TryEngage(target, transform.position);
        }

        private void RecalculateBonuses()
        {
            if (_playerStats != null)
            {
                _playerStats.ApplyAuraBonuses(
                    AuraRules.TotalFlatBonus(_active, AuraStatKind.MaxHealth),
                    AuraRules.TotalFlatBonus(_active, AuraStatKind.MaxMana));
            }

            if (_mover != null)
            {
                float multiplier =
                    1f + AuraRules.TotalFlatBonus(_active, AuraStatKind.MoveSpeedPercent) / 100f;
                if (AuraRules.BlocksMovement(_active))
                    multiplier = 0f;

                _mover.SetAuraSpeedMultiplier(multiplier);
            }
        }

        private void PublishVisible()
        {
            _visible.Clear();

            for (int i = 0; i < _active.Count; i++)
            {
                AuraState state = _active[i];
                if (state.IsActive)
                    _visible.Add(new AuraSnapshot(state.Data.AuraId, state.Data.Kind, state.Stacks));
            }
        }

        private int Find(AuraData data)
        {
            for (int i = 0; i < _active.Count; i++)
            {
                if (_active[i].Data == data)
                    return i;
            }

            return -1;
        }

        private void OnPlayerDied() => _dead = true;

        private void OnNpcDied(PlayerInventory killer) => _dead = true;
    }
}
