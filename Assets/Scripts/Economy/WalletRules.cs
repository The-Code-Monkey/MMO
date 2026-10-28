using System;
using System.Collections.Generic;

namespace MMO.Economy
{
    /// <summary>The outcome of a wallet operation. <see cref="Ok"/> is the only success.</summary>
    public enum WalletError
    {
        Ok,
        NoAccount,
        UnknownCurrency,
        InvalidAmount,
        InsufficientFunds,
        Overflow,
    }

    /// <summary>
    /// Every wallet decision as a pure function: the server validates here before it writes, and
    /// no network, storage or Unity dependency is involved, so every branch is reachable from an
    /// EditMode test.
    ///
    /// <para>
    /// The invariant is simple and absolute: a balance is never negative and a credit never wraps.
    /// <see cref="Debit"/> and <see cref="Credit"/> mutate the list in place; <see cref="Transfer"/>
    /// compensates a failed credit by restoring the debit, so money can never vanish between two
    /// accounts.
    /// </para>
    /// </summary>
    public static class WalletRules
    {
        /// <summary>The amount held in <paramref name="currencyId"/>, or zero when it is unknown.</summary>
        public static long BalanceOf(List<CurrencyBalance> balances, string currencyId)
            => TryGet(balances, currencyId, out long amount) ? amount : 0;

        /// <summary>Looks up a currency balance; false when the wallet lacks that currency.</summary>
        public static bool TryGet(List<CurrencyBalance> balances, string currencyId, out long amount)
        {
            amount = 0;
            if (balances == null || string.IsNullOrEmpty(currencyId))
                return false;

            for (int i = 0; i < balances.Count; i++)
            {
                if (balances[i].CurrencyId == currencyId)
                {
                    amount = balances[i].Amount;
                    return true;
                }
            }

            return false;
        }

        /// <summary>Sets a currency to an exact non-negative amount, creating it if absent.</summary>
        public static WalletError Set(List<CurrencyBalance> balances, string currencyId, long amount)
        {
            if (balances == null)
                return WalletError.NoAccount;

            if (string.IsNullOrEmpty(currencyId))
                return WalletError.InvalidAmount;

            if (amount < 0)
                return WalletError.InvalidAmount;

            int index = IndexOf(balances, currencyId);
            if (index >= 0)
                balances[index] = new CurrencyBalance { CurrencyId = currencyId, Amount = amount };
            else
                balances.Add(new CurrencyBalance { CurrencyId = currencyId, Amount = amount });

            return WalletError.Ok;
        }

        /// <summary>
        /// Removes <paramref name="amount"/> of a currency. Fails with
        /// <see cref="WalletError.InsufficientFunds"/> when the balance is short, and with
        /// <see cref="WalletError.UnknownCurrency"/> when the wallet never held that currency.
        /// A zero debit is a no-op success.
        /// </summary>
        public static WalletError Debit(List<CurrencyBalance> balances, string currencyId, long amount)
        {
            if (balances == null)
                return WalletError.NoAccount;

            if (amount < 0)
                return WalletError.InvalidAmount;

            if (amount == 0)
                return WalletError.Ok;

            int index = IndexOf(balances, currencyId);
            if (index < 0)
                return WalletError.UnknownCurrency;

            long balance = balances[index].Amount;
            if (balance < amount)
                return WalletError.InsufficientFunds;

            balances[index] = new CurrencyBalance { CurrencyId = currencyId, Amount = balance - amount };
            return WalletError.Ok;
        }

        /// <summary>
        /// Adds <paramref name="amount"/> of a currency, creating it when absent. Fails with
        /// <see cref="WalletError.Overflow"/> rather than wrapping past <c>long.MaxValue</c>.
        /// A zero credit is a no-op success.
        /// </summary>
        public static WalletError Credit(List<CurrencyBalance> balances, string currencyId, long amount)
        {
            if (balances == null)
                return WalletError.NoAccount;

            if (string.IsNullOrEmpty(currencyId))
                return WalletError.InvalidAmount;

            if (amount < 0)
                return WalletError.InvalidAmount;

            if (amount == 0)
                return WalletError.Ok;

            int index = IndexOf(balances, currencyId);
            if (index < 0)
            {
                balances.Add(new CurrencyBalance { CurrencyId = currencyId, Amount = amount });
                return WalletError.Ok;
            }

            long balance = balances[index].Amount;
            if (balance > long.MaxValue - amount)
                return WalletError.Overflow;

            balances[index] = new CurrencyBalance { CurrencyId = currencyId, Amount = balance + amount };
            return WalletError.Ok;
        }

        /// <summary>
        /// Moves <paramref name="amount"/> of a currency from one wallet to another. The debit is
        /// applied first; if the credit then fails (it cannot, for a well-formed target, but the
        /// caller may pass a null or overflowed wallet) the debit is compensated so neither side
        /// loses money.
        /// </summary>
        public static WalletError Transfer(
            List<CurrencyBalance> from, List<CurrencyBalance> to, string currencyId, long amount)
        {
            if (from == null || to == null)
                return WalletError.NoAccount;

            if (amount < 0)
                return WalletError.InvalidAmount;

            if (amount == 0)
                return WalletError.Ok;

            WalletError debit = Debit(from, currencyId, amount);
            if (debit != WalletError.Ok)
                return debit;

            WalletError credit = Credit(to, currencyId, amount);
            if (credit != WalletError.Ok)
            {
                // Compensating rollback: the sender keeps what it tried to send. The credit failure
                // cannot have written a partial balance, so restoring the full amount is exact.
                Credit(from, currencyId, amount);
                return credit;
            }

            return WalletError.Ok;
        }

        /// <summary>
        /// Produces a clean copy of a balance list: drops entries with no currency id, clamps
        /// negative amounts to zero, and sums duplicate ids. Used when loading a wallet from a save
        /// or a replicated snapshot, so malformed data can never become a negative balance.
        /// </summary>
        public static List<CurrencyBalance> Sanitize(IReadOnlyList<CurrencyBalance> source)
        {
            var clean = new List<CurrencyBalance>();
            if (source == null)
                return clean;

            for (int i = 0; i < source.Count; i++)
            {
                CurrencyBalance entry = source[i];
                if (string.IsNullOrEmpty(entry.CurrencyId))
                    continue;

                long amount = Math.Max(0, entry.Amount);
                int index = IndexOf(clean, entry.CurrencyId);
                if (index < 0)
                {
                    clean.Add(new CurrencyBalance { CurrencyId = entry.CurrencyId, Amount = amount });
                    continue;
                }

                long existing = clean[index].Amount;
                long sum = existing > long.MaxValue - amount ? long.MaxValue : existing + amount;
                clean[index] = new CurrencyBalance { CurrencyId = entry.CurrencyId, Amount = sum };
            }

            return clean;
        }

        private static int IndexOf(List<CurrencyBalance> balances, string currencyId)
        {
            for (int i = 0; i < balances.Count; i++)
            {
                if (balances[i].CurrencyId == currencyId)
                    return i;
            }

            return -1;
        }
    }
}
