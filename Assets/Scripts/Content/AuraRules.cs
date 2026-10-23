using System;
using System.Collections.Generic;

namespace MMO.Content
{
    /// <summary>
    /// Pure aura rules: application/refresh/stacking, periodic ticks, expiry and the CC gates.
    /// Callers pass <c>now</c>; nothing here reads a clock.
    /// </summary>
    public static class AuraRules
    {
        /// <summary>
        /// Applies or refreshes an aura. A max-stack of one refreshes duration; a higher cap adds
        /// one stack per reapplication, and the duration/tick clocks restart either way.
        /// </summary>
        public static AuraState Apply(AuraState existing, AuraData data, double now)
        {
            if (data == null)
                return existing;

            double nextTick = data.TickIntervalSeconds > 0d
                ? now + data.TickIntervalSeconds
                : double.PositiveInfinity;

            if (existing.Data == null || existing.Data != data)
            {
                return new AuraState
                {
                    Data = data,
                    ExpiresAt = now + data.DurationSeconds,
                    NextTickAt = nextTick,
                    Stacks = 1,
                };
            }

            int stacks = existing.Stacks > 0 && data.MaxStacks > 1
                ? Math.Min(data.MaxStacks, existing.Stacks + 1)
                : 1;

            return new AuraState
            {
                Data = data,
                ExpiresAt = now + data.DurationSeconds,
                NextTickAt = nextTick,
                Stacks = stacks,
            };
        }

        /// <summary>
        /// Produces at most one periodic tick. A caller that needs to catch up across a long frame
        /// should loop while the returned state's <see cref="AuraState.NextTickAt"/> is due.
        /// </summary>
        public static AuraTickResult Tick(AuraState state, double now)
        {
            if (!state.IsActive)
                return new AuraTickResult(false, 0, 0, true, state);

            bool periodic = state.Data.Kind == AuraKind.Dot || state.Data.Kind == AuraKind.Hot;
            double interval = state.Data.TickIntervalSeconds;
            bool ticked = false;
            int damage = 0;
            int heal = 0;

            if (periodic && interval > 0d && now >= state.NextTickAt)
            {
                int magnitude = Math.Max(0, state.Data.MagnitudePerTick) * state.Stacks;
                if (state.Data.Kind == AuraKind.Dot)
                    damage = magnitude;
                else
                    heal = magnitude;

                state.NextTickAt += interval;
                ticked = true;
            }

            return new AuraTickResult(ticked, damage, heal, now >= state.ExpiresAt, state);
        }

        /// <summary>A stun blocks abilities, gathering and attacks.</summary>
        public static bool BlocksActions(IReadOnlyList<AuraState> states)
        {
            if (states == null)
                return false;

            for (int i = 0; i < states.Count; i++)
            {
                AuraState state = states[i];
                if (state.IsActive && state.Data.Kind == AuraKind.Stun)
                    return true;
            }

            return false;
        }

        /// <summary>A stun or root blocks movement; a root alone does not block actions.</summary>
        public static bool BlocksMovement(IReadOnlyList<AuraState> states)
        {
            if (states == null)
                return false;

            for (int i = 0; i < states.Count; i++)
            {
                AuraState state = states[i];
                if (!state.IsActive)
                    continue;

                if (state.Data.Kind == AuraKind.Stun || state.Data.Kind == AuraKind.Root)
                    return true;
            }

            return false;
        }

        /// <summary>Sum of a stat's flat bonus across active auras, scaled by stacks.</summary>
        public static int TotalFlatBonus(IReadOnlyList<AuraState> states, AuraStatKind stat)
        {
            if (states == null || stat == AuraStatKind.None)
                return 0;

            int total = 0;
            for (int i = 0; i < states.Count; i++)
            {
                AuraState state = states[i];
                if (state.IsActive && state.Data.Stat == stat)
                    total += state.Data.FlatStatBonus * state.Stacks;
            }

            return total;
        }
    }
}
