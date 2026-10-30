using System;
using System.Collections.Generic;

namespace MMO.Client
{
    /// <summary>What rebinding one action to one control should do to the rest of the map.</summary>
    public sealed class InputBindingPlan
    {
        public InputBindingPlan(
            string action, string controlPath, bool isReserved, IReadOnlyList<string> clearedActions)
        {
            Action = action;
            ControlPath = controlPath;
            IsReserved = isReserved;
            ClearedActions = clearedActions;
        }

        /// <summary>Action being rebound.</summary>
        public string Action { get; }

        /// <summary>Control path the player chose, e.g. <c>&lt;Keyboard&gt;/q</c>.</summary>
        public string ControlPath { get; }

        /// <summary>True when a system key refused the binding.</summary>
        public bool IsReserved { get; }

        /// <summary>Other actions whose binding of the same control must be removed.</summary>
        public IReadOnlyList<string> ClearedActions { get; }
    }

    /// <summary>
    /// Pure conflict resolution for key rebinding: one control belongs to one action, and a few
    /// controls belong to the system instead of the player.
    ///
    /// No Input System dependency — the caller passes the current action→control map as strings,
    /// so duplicate-key and reserved-key behaviour is tested without a device.
    /// </summary>
    public static class InputBindingRules
    {
        /// <summary>Controls the player may never bind.</summary>
        public static readonly string[] ReservedControls =
        {
            "<Keyboard>/escape",
            "<Keyboard>/f1",
        };

        /// <summary>True when <paramref name="controlPath"/> is a system key.</summary>
        public static bool IsReserved(string controlPath)
        {
            if (string.IsNullOrWhiteSpace(controlPath))
                return true;

            for (int i = 0; i < ReservedControls.Length; i++)
            {
                if (string.Equals(ReservedControls[i], controlPath.Trim(), StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Resolves rebinding <paramref name="action"/> to <paramref name="controlPath"/>:
        /// clears every other action currently using the same control, unless the control is
        /// reserved. Unknown/empty action names are refused as reserved.
        /// </summary>
        public static InputBindingPlan Resolve(
            string action,
            string controlPath,
            IReadOnlyDictionary<string, string> current)
        {
            string normalized = controlPath == null ? string.Empty : controlPath.Trim();
            bool reserved = string.IsNullOrWhiteSpace(action) || IsReserved(normalized);

            var cleared = new List<string>();
            if (!reserved && current != null)
            {
                foreach (KeyValuePair<string, string> pair in current)
                {
                    if (string.Equals(pair.Key, action, StringComparison.Ordinal))
                        continue;

                    if (PathsMatch(pair.Value, normalized))
                        cleared.Add(pair.Key);
                }

                // Dictionary order is not guaranteed; make the plan deterministic for tests and logs.
                cleared.Sort(StringComparer.Ordinal);
            }

            return new InputBindingPlan(action, normalized, reserved, cleared);
        }

        private static bool PathsMatch(string left, string right)
        {
            return string.Equals(
                left == null ? string.Empty : left.Trim(),
                right == null ? string.Empty : right.Trim(),
                StringComparison.OrdinalIgnoreCase);
        }
    }
}
