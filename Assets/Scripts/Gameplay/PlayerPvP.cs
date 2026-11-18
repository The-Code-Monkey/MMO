using System;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using MMO.Persistence;
using UnityEngine;

namespace MMO.Gameplay
{
    /// <summary>
    /// The networked shell of the PvP flag: the state the server replicates, the one door a
    /// client can knock on to change it, and the verdict every damage site asks before it lands.
    ///
    /// <para>
    /// The client decides nothing. It sends a request, and the server resolves it against the
    /// zone's tier, the current flag and the opt-out cooldown — all server-side values — then
    /// writes a <see cref="SyncVar{T}"/> FishNet refuses to let a client touch. The replicated
    /// cooldown is a countdown for the HUD, not the clock the server validates against.
    /// </para>
    ///
    /// <para>
    /// The arithmetic lives in <see cref="PvPRules"/> and touches no engine state; this class only
    /// feeds it the tier under the player, both flags and the last player-versus-player damage.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerPvP : NetworkBehaviour, IPvPSource
    {
        // Server-owned. Clients hold a replicated copy and are refused if they try to write.
        private readonly SyncVar<bool> _flagged = new();

        // Whole seconds left to the opt-out window, published once per second so a HUD can draw a
        // countdown without sharing the server's clock.
        private readonly SyncVar<float> _optOutCooldown = new();

        // Why the last toggle was refused, so the UI can say something better than "nothing".
        private readonly SyncVar<int> _lastResult = new();

        // Server-only: the last time this player dealt or took player-versus-player damage.
        private double _lastPlayerHarmAt = double.NegativeInfinity;
        private float _publishedCooldown = -1f;

        // The tier under the player is scanned from the scene, so it is cached for a second
        // instead of re-scanned every frame. The authoritative checks (TrySetFlag, TryEngage)
        // resolve it fresh; this is for the HUD and the cooldown pump.
        private ZoneRiskTier _cachedTier = ZoneRiskTier.Safe;
        private double _tierRefreshedAt = double.NegativeInfinity;

        /// <summary>True when this player has opted into PvP.</summary>
        public bool IsFlagged => _flagged.Value;

        /// <summary>Whole seconds left on the opt-out cooldown, as replicated.</summary>
        public float OptOutCooldown => _optOutCooldown.Value;

        /// <summary>The verdict on the most recent toggle request.</summary>
        public PvPRequestResult LastResult => NormalizeResult(_lastResult.Value);

        /// <summary>True when the zone under this player forces PvP.</summary>
        public bool IsForced => PvPRules.IsForced(ZoneTier);

        /// <summary>
        /// The tier under this player's feet, refreshed at most once a second: the lookup scans
        /// every zone in the scene, and a HUD or a cooldown pump has no reason to pay for that on
        /// every frame.
        /// </summary>
        public ZoneRiskTier ZoneTier
        {
            get
            {
                double now = Time.timeAsDouble;
                if (now - _tierRefreshedAt >= 1d || _tierRefreshedAt > now)
                {
                    _cachedTier = ZoneRiskConfig.ResolveTierAt(transform.position);
                    _tierRefreshedAt = now;
                }

                return _cachedTier;
            }
        }

        /// <summary>Raised on the server and every client whenever the replicated state changes.</summary>
        public event Action FlagChanged;

        public override void OnStartNetwork()
        {
            _flagged.OnChange += OnFlagReplicated;
            _optOutCooldown.OnChange += OnCooldownReplicated;
            _lastResult.OnChange += OnResultReplicated;
        }

        public override void OnStopNetwork()
        {
            _flagged.OnChange -= OnFlagReplicated;
            _optOutCooldown.OnChange -= OnCooldownReplicated;
            _lastResult.OnChange -= OnResultReplicated;
        }

        /// <summary>
        /// Owner-side intent: ask the server to set the flag. A remote copy of this player does
        /// nothing, so a client cannot toggle someone else's flag through this component.
        /// </summary>
        public void RequestFlag(bool flagged)
        {
            if (!IsOwner)
                return;

            RpcRequestFlag(flagged);
        }

        [ServerRpc]
        private void RpcRequestFlag(bool flagged, NetworkConnection sender = null)
        {
            if (sender == null || Owner == null || sender != Owner)
                return;

            TrySetFlag(flagged);
        }

        /// <summary>
        /// Server: resolves and applies a flag request. The write happens only when
        /// <see cref="PvPRules.ResolveFlagRequest"/> accepts it; every refusal is reported back
        /// through <see cref="LastResult"/> so the UI can explain it.
        /// </summary>
        [Server]
        public PvPRequestResult TrySetFlag(bool flagged)
        {
            // A toggle is rare, so this one reads the zone fresh rather than trusting the cache:
            // stepping into a forced zone must not leave a one-second window to opt out.
            ZoneRiskTier tier = ZoneRiskConfig.ResolveTierAt(transform.position);
            PvPRequestResult result = PvPRules.ResolveFlagRequest(
                tier, IsFlagged, flagged, Time.timeAsDouble, _lastPlayerHarmAt);

            _lastResult.Value = (int)result;
            if (result == PvPRequestResult.Accepted)
            {
                _flagged.Value = flagged;
                if (!flagged)
                    _optOutCooldown.Value = 0f;
            }

            return result;
        }

        /// <summary>
        /// Server: flags the player without asking — the auto-flag of an unflagged attacker who
        /// lands a hit on a flagged target in a yellow zone.
        /// </summary>
        [Server]
        public void ForceFlag()
        {
            if (!_flagged.Value)
                _flagged.Value = true;
        }

        /// <summary>
        /// Server: resets the opt-out cooldown. Called whenever player-versus-player damage is
        /// dealt or taken; NPC damage never calls it.
        /// </summary>
        [Server]
        public void NotifyPlayerHarm()
        {
            _lastPlayerHarmAt = Time.timeAsDouble;
        }

        /// <summary>
        /// Server: the whole "may this hit land" verdict for one target. True means the damage is
        /// allowed and both combat timers have been reset; a yellow-zone hit on a flagged target
        /// also flags an unflagged attacker, before the caller applies any damage.
        /// </summary>
        [Server]
        public bool TryEngage(PlayerPvP target, Vector3 targetPosition)
        {
            ZoneRiskTier tier = ZoneRiskConfig.ResolveTierAt(targetPosition);
            bool attackerFlagged = IsFlagged;
            bool targetFlagged = target != null && target.IsFlagged;

            if (!PvPRules.CanHarm(tier, attackerFlagged, targetFlagged))
                return false;

            if (PvPRules.AutoFlags(tier, attackerFlagged, targetFlagged))
                ForceFlag();

            NotifyPlayerHarm();
            if (target != null)
                target.NotifyPlayerHarm();

            return true;
        }

        /// <summary>Server: applies a loaded save to this player.</summary>
        [Server]
        public void InitializeFromSave(PlayerSaveRecord record)
        {
            _flagged.Value = record != null && record.PvPFlag;
            _lastPlayerHarmAt = double.NegativeInfinity;
        }

        /// <summary>Server: captures the flag into the record a save or a zone transfer carries.</summary>
        [Server]
        public void CaptureToSave(PlayerSaveRecord record)
        {
            if (record != null)
                record.PvPFlag = _flagged.Value;
        }

        private void Update()
        {
            if (!IsServer || !IsFlagged)
                return;

            float remaining = !IsForced
                ? (float)PvPRules.RemainingOptOut(Time.timeAsDouble, _lastPlayerHarmAt)
                : 0f;

            // Publish whole seconds only, and only when the number actually moves: the SyncVar
            // write is a network delta, not a frame counter.
            float published = Mathf.Ceil(remaining);
            if (Mathf.Approximately(published, _publishedCooldown))
                return;

            _publishedCooldown = published;
            _optOutCooldown.Value = published;
        }

        private void OnFlagReplicated(bool previous, bool current, bool asServer)
        {
            if (previous == current)
                return;

            FlagChanged?.Invoke();
        }

        private void OnCooldownReplicated(float previous, float current, bool asServer)
            => FlagChanged?.Invoke();

        private void OnResultReplicated(int previous, int current, bool asServer)
        {
            if (previous == current)
                return;

            FlagChanged?.Invoke();
        }

        private static PvPRequestResult NormalizeResult(int value)
        {
            return value >= (int)PvPRequestResult.Accepted && value <= (int)PvPRequestResult.ForcedZone
                ? (PvPRequestResult)value
                : PvPRequestResult.Accepted;
        }
    }
}
