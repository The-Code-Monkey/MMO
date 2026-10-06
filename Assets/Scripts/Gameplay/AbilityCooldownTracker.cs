using System;

namespace MMO.Gameplay
{
    /// <summary>
    /// The server's stopwatch bookkeeping for one player: when each slot may fire again, when
    /// the Global Cooldown lifts, and until when a cast with a cast time is still channelling.
    ///
    /// Time is never read here. Every method takes the caller's <c>now</c>, which is what makes
    /// the whole cooldown system testable without sleeping: an EditMode test can cast at
    /// <c>t = 100</c> and assert the slot unlocks at <c>t = 106</c> in microseconds. The server
    /// passes <c>Time.timeAsDouble</c>; a test passes whatever number proves the boundary.
    ///
    /// A tracker belongs to one player and is only ever written by the server — a client that
    /// could edit it would have an opinion about its own cooldowns, which is exactly what the
    /// issue forbids.
    /// </summary>
    public sealed class AbilityCooldownTracker
    {
        /// <summary>
        /// Seconds every ability shares as a lockout after any cast. One constant, not an asset
        /// field: the GCD is a rule of the combat system, not a property of a single skill.
        /// </summary>
        public const float GlobalCooldown = 1.5f;

        private readonly double[] _readyAt;
        private double _globalReadyAt;
        private double _castingUntil;

        public AbilityCooldownTracker(int slotCount = AbilityLoadout.MaxSlots)
        {
            if (slotCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(slotCount), "A tracker needs at least one slot.");

            _readyAt = new double[slotCount];
        }

        public int SlotCount => _readyAt.Length;

        /// <summary>
        /// True when the slot may be cast at <paramref name="now"/>. An out-of-range slot answers
        /// false rather than throwing: there is nothing there to be ready for.
        /// </summary>
        public bool IsReady(int slot, double now)
        {
            return slot >= 0 && slot < _readyAt.Length && now >= _readyAt[slot];
        }

        /// <summary>
        /// Seconds until the slot unlocks, zero once it has. Out-of-range slots wait for nothing.
        /// </summary>
        public float SecondsUntilReady(int slot, double now)
        {
            if (slot < 0 || slot >= _readyAt.Length)
                return 0f;

            return (float)Math.Max(0d, _readyAt[slot] - now);
        }

        /// <summary>True when the Global Cooldown has lifted at <paramref name="now"/>.</summary>
        public bool IsGlobalCooldownReady(double now)
        {
            return now >= _globalReadyAt;
        }

        public double SecondsUntilGlobalCooldownReady(double now)
        {
            return Math.Max(0d, _globalReadyAt - now);
        }

        /// <summary>
        /// True while an ability with a cast time is still resolving. Zero-length casts set a
        /// window that is already over, so they never report as channelling.
        /// </summary>
        public bool IsCasting(double now)
        {
            return now < _castingUntil;
        }

        public double SecondsUntilCastCompletes(double now)
        {
            return Math.Max(0d, _castingUntil - now);
        }

        /// <summary>
        /// Locks the slot from <paramref name="now"/> for <paramref name="cooldown"/> seconds.
        /// A longer lock already in place is never shortened — that would be a way to erase a
        /// penalty by casting something else first.
        /// </summary>
        public void StartCooldown(int slot, float cooldown, double now)
        {
            if (slot < 0 || slot >= _readyAt.Length)
                throw new ArgumentOutOfRangeException(nameof(slot), $"Tracker only owns {SlotCount} slots.");

            double readyAt = now + Math.Max(0d, cooldown);
            if (readyAt > _readyAt[slot])
                _readyAt[slot] = readyAt;
        }

        /// <summary>Starts the shared Global Cooldown at <paramref name="now"/>.</summary>
        public void StartGlobalCooldown(double now)
        {
            double readyAt = now + GlobalCooldown;
            if (readyAt > _globalReadyAt)
                _globalReadyAt = readyAt;
        }

        /// <summary>
        /// Marks the caster as channelling until <paramref name="until"/>. The furthest end wins,
        /// so an overlapping window can never truncate one already running.
        /// </summary>
        public void BeginCastUntil(double until)
        {
            if (until > _castingUntil)
                _castingUntil = until;
        }
    }
}
