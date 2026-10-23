using System;

namespace MMO.Gameplay
{
    /// <summary>
    /// The pure half of the mount system: a clock-free state machine that decides when a cast
    /// finishes, whether a takeoff is legal, and what speed multiplier applies.
    ///
    /// Time is never read here. Every method takes the caller's <c>now</c>, exactly like
    /// <see cref="AbilityCooldownTracker"/>, so an EditMode test can cast at <c>t = 100</c> and
    /// assert it completes at <c>t = 101.5</c> without sleeping. The networked shell
    /// (<see cref="PlayerMount"/>) holds the clock and passes <c>Time.timeAsDouble</c>.
    ///
    /// States run <c>Unmounted → Mounting → Mounted → Flying → Mounted</c>, and back to
    /// <c>Unmounted</c>. Landing and dismounting are separate on purpose: landing while flying
    /// returns to ground movement, while dismounting removes the mount entirely.
    /// </summary>
    public sealed class MountStateMachine
    {
        private readonly MountData _data;
        private double _mountedAt;
        private double _dismountedAt;

        /// <exception cref="ArgumentNullException">A mount without data has nothing to decide.</exception>
        public MountStateMachine(MountData data)
        {
            _data = data != null ? data : throw new ArgumentNullException(nameof(data));
        }

        public MountState State { get; private set; } = MountState.Unmounted;

        public MountData Data => _data;

        /// <summary>True on the ground or in the air, i.e. a mount is active.</summary>
        public bool IsMounted =>
            State == MountState.Mounted || State == MountState.Flying || State == MountState.Dismounting;

        public bool IsFlying => State == MountState.Flying;

        /// <summary>
        /// Begins the mount cast. Only legal from <see cref="MountState.Unmounted"/>; a second
        /// request while mounting or mounted is refused so a spam click cannot reset the cast.
        /// </summary>
        public bool RequestMount(double now)
        {
            if (State != MountState.Unmounted)
                return false;

            State = MountState.Mounting;
            _mountedAt = now + _data.MountCastSeconds;
            return true;
        }

        /// <summary>
        /// Advances the cast. A zero-length cast completes on the same tick that started it,
        /// because <c>now &gt;= _mountedAt</c> is already true. Both getting on and getting off
        /// use <see cref="MountData.MountCastSeconds"/>.
        /// </summary>
        public MountState Tick(double now)
        {
            if (State == MountState.Mounting && now >= _mountedAt)
                State = MountState.Mounted;

            if (State == MountState.Dismounting && now >= _dismountedAt)
                State = MountState.Unmounted;

            return State;
        }

        /// <summary>
        /// Takes off. Refused unless mounted, the mount can fly, and the zone allows flight —
        /// the zone flag is the PvP-balance hook and is passed in so this stays pure.
        /// </summary>
        public bool RequestTakeoff(bool zoneAllowsFlying)
        {
            if (State != MountState.Mounted || !_data.IsFlyingMount || !zoneAllowsFlying)
                return false;

            State = MountState.Flying;
            return true;
        }

        /// <summary>Returns a flying mount to ground movement. No-op when not flying.</summary>
        public bool RequestLand()
        {
            if (State != MountState.Flying)
                return false;

            State = MountState.Mounted;
            return true;
        }

        /// <summary>
        /// Begins the dismount cast. Only legal from <see cref="MountState.Mounted"/>; a flying
        /// mount must land first. The cast runs for <see cref="MountData.MountCastSeconds"/> and
        /// <see cref="Tick"/> finishes it.
        /// </summary>
        public bool RequestDismount(double now)
        {
            if (State != MountState.Mounted)
                return false;

            State = MountState.Dismounting;
            _dismountedAt = now + _data.MountCastSeconds;
            return true;
        }

        /// <summary>
        /// Removes the mount at once, bypassing the dismount cast. Refused mid-air so the player
        /// cannot be dropped through geometry; the shell uses this for forced dismounts (the mount
        /// item going away), while a player dons/doffs through <see cref="RequestDismount"/>.
        /// </summary>
        public bool Dismount()
        {
            if (State == MountState.Flying)
                return false;

            State = MountState.Unmounted;
            return true;
        }

        /// <summary>
        /// How far through the current cast the player is: 0 at the start, 1 when it completes and
        /// 0 when neither mounting nor dismounting. The shell replicates this so a client can draw
        /// a progress bar without sharing the server's clock.
        /// </summary>
        public float CastProgress(double now)
        {
            double end;
            if (State == MountState.Mounting)
                end = _mountedAt;
            else if (State == MountState.Dismounting)
                end = _dismountedAt;
            else
                return 0f;

            float duration = _data.MountCastSeconds;
            if (duration <= 0f)
                return 1f;

            double remaining = Math.Max(0d, end - now);
            return (float)Math.Min(1d, Math.Max(0d, 1d - remaining / duration));
        }

        /// <summary>The speed multiplier the mover should apply for the current state.</summary>
        public float SpeedMultiplier => State switch
        {
            MountState.Mounted or MountState.Dismounting => _data.GroundSpeedMultiplier,
            MountState.Flying => _data.FlightSpeedMultiplier,
            _ => 1f,
        };
    }
}
