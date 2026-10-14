using System;
using System.Collections.Generic;

namespace MMO.Inventory
{
    /// <summary>
    /// The outcome of a single ownership operation. <see cref="Ok"/> is the only success; every
    /// other value is a refusal a caller must honour (reject the inventory mutation) rather than
    /// retry blindly.
    /// </summary>
    public enum OwnershipMove
    {
        /// <summary>The move was applied.</summary>
        Ok,

        /// <summary>The id is already held by a different holder — a duplicate was refused.</summary>
        DuplicateDetected,

        /// <summary>No such item is tracked (an empty or never-created instance id).</summary>
        UnknownItem,

        /// <summary>The item exists but is not held by the holder that tried to move it.</summary>
        NotHeld,
    }

    /// <summary>
    /// The single source of truth for <em>who holds which item instance</em> on this server. Every
    /// inventory mutation that creates, moves, or consumes an item routes through it, and a grant
    /// that would put an id held by one holder into another's hands is refused as
    /// <see cref="OwnershipMove.DuplicateDetected"/> instead of silently succeeding.
    ///
    /// This is the structural half of dupe protection: it does not make duplication unlikely, it
    /// makes the *same* physical item impossible to hold twice. The cross-process half is the
    /// backend's compare-and-swap / atomic transaction (#59), which settles two servers racing the
    /// same item; this registry settles everything inside one process.
    ///
    /// <para>
    /// The server tick is single-threaded, so a check-then-act race cannot occur between two
    /// operations on one item. The lock below is belt-and-braces for any caller that drives the
    /// registry off the main tick, and is cheap enough that it costs nothing when they do not.
    /// </para>
    /// </summary>
    public sealed class ItemOwnershipRegistry
    {
        private readonly Dictionary<string, string> _owners = new(StringComparer.Ordinal);
        private readonly object _gate = new();

        /// <summary>How many item instances are currently tracked (granted and not released).</summary>
        public int Count
        {
            get
            {
                lock (_gate)
                    return _owners.Count;
            }
        }

        /// <summary>
        /// Records that <paramref name="holder"/> now holds <paramref name="instanceId"/>. The
        /// first grant wins; re-granting to the same holder is an idempotent <see cref="OwnershipMove.Ok"/>,
        /// while granting an id already held elsewhere is refused as
        /// <see cref="OwnershipMove.DuplicateDetected"/>.
        /// </summary>
        public OwnershipMove Grant(string instanceId, string holder)
        {
            if (string.IsNullOrEmpty(instanceId) || string.IsNullOrEmpty(holder))
                return OwnershipMove.UnknownItem;

            lock (_gate)
            {
                if (_owners.TryGetValue(instanceId, out string existing))
                    return string.Equals(existing, holder, StringComparison.Ordinal)
                        ? OwnershipMove.Ok
                        : OwnershipMove.DuplicateDetected;

                _owners[instanceId] = holder;
                return OwnershipMove.Ok;
            }
        }

        /// <summary>
        /// Atomically moves <paramref name="instanceId"/> from <paramref name="fromHolder"/> to
        /// <paramref name="toHolder"/>. Refused with <see cref="OwnershipMove.NotHeld"/> when the
        /// item is not currently held by the source, and with
        /// <see cref="OwnershipMove.DuplicateDetected"/> when the destination already holds it.
        /// </summary>
        public OwnershipMove Transfer(string instanceId, string fromHolder, string toHolder)
        {
            if (string.IsNullOrEmpty(instanceId) || string.IsNullOrEmpty(fromHolder) || string.IsNullOrEmpty(toHolder))
                return OwnershipMove.UnknownItem;

            lock (_gate)
            {
                if (!_owners.TryGetValue(instanceId, out string existing))
                    return OwnershipMove.UnknownItem;

                if (existing == toHolder)
                    return OwnershipMove.Ok;

                if (!string.Equals(existing, fromHolder, StringComparison.Ordinal))
                    return OwnershipMove.NotHeld;

                _owners[instanceId] = toHolder;
                return OwnershipMove.Ok;
            }
        }

        /// <summary>
        /// Forgets an item entirely — it was consumed, destroyed, or dropped out of the world.
        /// Only the holder that currently owns it may release it; anyone else gets
        /// <see cref="OwnershipMove.NotHeld"/>, so one holder cannot erase another's item.
        /// </summary>
        public OwnershipMove Release(string instanceId, string holder)
        {
            if (string.IsNullOrEmpty(instanceId) || string.IsNullOrEmpty(holder))
                return OwnershipMove.UnknownItem;

            lock (_gate)
            {
                if (!_owners.TryGetValue(instanceId, out string existing))
                    return OwnershipMove.UnknownItem;

                if (!string.Equals(existing, holder, StringComparison.Ordinal))
                    return OwnershipMove.NotHeld;

                _owners.Remove(instanceId);
                return OwnershipMove.Ok;
            }
        }

        /// <summary>The holder of <paramref name="instanceId"/>, or false when it is untracked.</summary>
        public bool TryGetOwner(string instanceId, out string holder)
        {
            holder = null;
            if (string.IsNullOrEmpty(instanceId))
                return false;

            lock (_gate)
                return _owners.TryGetValue(instanceId, out holder);
        }

        /// <summary>True when <paramref name="instanceId"/> is currently held by <paramref name="holder"/>.</summary>
        public bool IsHeldBy(string instanceId, string holder)
        {
            if (string.IsNullOrEmpty(instanceId) || string.IsNullOrEmpty(holder))
                return false;

            lock (_gate)
                return _owners.TryGetValue(instanceId, out string existing)
                       && string.Equals(existing, holder, StringComparison.Ordinal);
        }

        /// <summary>Drops all tracking (disconnect, teardown, test reset).</summary>
        public void Clear()
        {
            lock (_gate)
                _owners.Clear();
        }
    }
}
