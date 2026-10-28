using System;
using System.Collections.Generic;
using MMO.Inventory;

namespace MMO.Social
{
    /// <summary>Where a direct trade is in its two-lock handshake.</summary>
    public enum TradeState
    {
        Open,
        OneLocked,
        BothLocked,
        Executed,
        Cancelled,
    }

    /// <summary>The outcome of a trade intent. <see cref="Ok"/> is the only success.</summary>
    public enum TradeRule
    {
        Ok,
        NotYourTrade,
        NoOffer,
        DuplicateInstanceId,
        AlreadyFinished,
        OutOfRange,
        Busy,
        BagFull,
    }

    /// <summary>The two sides of a settled trade, already routed: <see cref="ToA"/> is B's offer.</summary>
    public readonly struct TradeExchange
    {
        public TradeExchange(IReadOnlyList<ItemInstance> toA, IReadOnlyList<ItemInstance> toB)
        {
            ToA = toA;
            ToB = toB;
        }

        /// <summary>The items that move to account A.</summary>
        public IReadOnlyList<ItemInstance> ToA { get; }

        /// <summary>The items that move to account B.</summary>
        public IReadOnlyList<ItemInstance> ToB { get; }
    }

    /// <summary>
    /// One player-to-player trade, pure and server-side. Items never leave their bags while the
    /// session is open: each side only publishes an offer and a lock, and the shell moves both
    /// sides in one tick after <see cref="TryBuildExchange"/> re-validates them against the live
    /// bags. Any offer change clears both locks, so neither side can be surprised by a swap.
    /// </summary>
    public sealed class TradeSession
    {
        private static readonly IReadOnlyList<ItemInstance> Empty = Array.Empty<ItemInstance>();

        private readonly string _accountA;
        private readonly string _accountB;
        private readonly List<ItemInstance> _offerA = new();
        private readonly List<ItemInstance> _offerB = new();

        private bool _lockedA;
        private bool _lockedB;
        private TradeState _finished;

        public TradeSession(string accountA, string accountB)
        {
            _accountA = accountA;
            _accountB = accountB;
        }

        /// <summary>The state derived from the two locks, or the terminal state once finished.</summary>
        public TradeState State
        {
            get
            {
                if (_finished != TradeState.Open)
                    return _finished;

                if (_lockedA && _lockedB)
                    return TradeState.BothLocked;

                return _lockedA || _lockedB ? TradeState.OneLocked : TradeState.Open;
            }
        }

        public bool IsFinished => State == TradeState.Executed || State == TradeState.Cancelled;

        /// <summary>The offer this account published, or an empty list when it is not a participant.</summary>
        public IReadOnlyList<ItemInstance> OfferOf(string accountId)
        {
            if (accountId == _accountA)
                return _offerA;

            if (accountId == _accountB)
                return _offerB;

            return Empty;
        }

        /// <summary>Whether this account has locked its side of the current offer.</summary>
        public bool IsLocked(string accountId)
        {
            if (accountId == _accountA)
                return _lockedA;

            if (accountId == _accountB)
                return _lockedB;

            return false;
        }

        /// <summary>
        /// Replaces one side's offer and clears both locks: a lock always describes the offer on
        /// screen, never one that has since changed.
        /// </summary>
        public TradeRule SetOffer(string accountId, IReadOnlyList<ItemInstance> items)
        {
            if (IsFinished)
                return TradeRule.AlreadyFinished;

            List<ItemInstance> target = OfferListOf(accountId);
            if (target == null)
                return TradeRule.NotYourTrade;

            target.Clear();
            if (items != null)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    if (!items[i].IsEmpty)
                        target.Add(items[i]);
                }
            }

            _lockedA = false;
            _lockedB = false;
            return TradeRule.Ok;
        }

        /// <summary>Locks one side. An empty offer cannot be locked.</summary>
        public TradeRule Lock(string accountId)
        {
            if (IsFinished)
                return TradeRule.AlreadyFinished;

            if (accountId != _accountA && accountId != _accountB)
                return TradeRule.NotYourTrade;

            if (OfferOf(accountId).Count == 0)
                return TradeRule.NoOffer;

            if (accountId == _accountA)
                _lockedA = true;
            else
                _lockedB = true;

            return TradeRule.Ok;
        }

        /// <summary>
        /// Builds the routed swap once both sides are locked. Does not mutate the session: the
        /// shell calls <see cref="Complete"/> only after the items actually moved.
        /// </summary>
        public TradeRule TryBuildExchange(out TradeExchange exchange)
        {
            exchange = default;

            if (IsFinished)
                return TradeRule.AlreadyFinished;

            if (State != TradeState.BothLocked || _offerA.Count == 0 || _offerB.Count == 0)
                return TradeRule.NoOffer;

            if (HasDuplicateInstanceId(_offerA, _offerB))
                return TradeRule.DuplicateInstanceId;

            exchange = new TradeExchange(
                new List<ItemInstance>(_offerB),
                new List<ItemInstance>(_offerA));
            return TradeRule.Ok;
        }

        /// <summary>Records the swap as durable. Called only after both bags moved.</summary>
        public void Complete() => _finished = TradeState.Executed;

        /// <summary>Ends the session without a swap.</summary>
        public void Cancel() => _finished = TradeState.Cancelled;

        private List<ItemInstance> OfferListOf(string accountId)
        {
            if (accountId == _accountA)
                return _offerA;

            if (accountId == _accountB)
                return _offerB;

            return null;
        }

        private static bool HasDuplicateInstanceId(
            IReadOnlyList<ItemInstance> first, IReadOnlyList<ItemInstance> second)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            return !TryAddIds(first, seen) || !TryAddIds(second, seen);
        }

        private static bool TryAddIds(IReadOnlyList<ItemInstance> items, HashSet<string> seen)
        {
            for (int i = 0; i < items.Count; i++)
            {
                string instanceId = items[i].InstanceId;
                if (string.IsNullOrEmpty(instanceId) || !seen.Add(instanceId))
                    return false;
            }

            return true;
        }
    }
}
