using System.Collections.Generic;
using MMO.Economy;
using NUnit.Framework;

namespace MMO.Tests
{
    /// <summary>
    /// The wallet rules, isolated: the multi-currency balance list, the non-negative invariant,
    /// overflow safety and the compensating transfer. No server, no storage, no socket — the same
    /// "test the server logic with mock state first" rule the rest of the suite follows.
    /// </summary>
    public sealed class WalletRulesTests
    {
        private static List<CurrencyBalance> Balances(params (string id, long amount)[] entries)
        {
            var balances = new List<CurrencyBalance>();
            foreach ((string id, long amount) entry in entries)
                balances.Add(new CurrencyBalance { CurrencyId = entry.id, Amount = entry.amount });
            return balances;
        }

        [Test]
        public void BalanceOf_KnownCurrency_ReturnsAmount()
            => Assert.AreEqual(10, WalletRules.BalanceOf(Balances((CurrencyIds.Gold, 10)), CurrencyIds.Gold));

        [Test]
        public void BalanceOf_UnknownCurrency_ReturnsZero()
            => Assert.AreEqual(0, WalletRules.BalanceOf(Balances((CurrencyIds.Gold, 10)), CurrencyIds.Silver));

        [Test]
        public void BalanceOf_NullList_ReturnsZero()
            => Assert.AreEqual(0, WalletRules.BalanceOf(null, CurrencyIds.Gold));

        [Test]
        public void Credit_CreatesMissingCurrency()
        {
            List<CurrencyBalance> balances = Balances();

            Assert.AreEqual(WalletError.Ok, WalletRules.Credit(balances, CurrencyIds.Silver, 7));

            Assert.AreEqual(7, WalletRules.BalanceOf(balances, CurrencyIds.Silver));
        }

        [Test]
        public void Credit_AddsToExistingCurrency()
        {
            List<CurrencyBalance> balances = Balances((CurrencyIds.Gold, 5));

            Assert.AreEqual(WalletError.Ok, WalletRules.Credit(balances, CurrencyIds.Gold, 3));

            Assert.AreEqual(8, WalletRules.BalanceOf(balances, CurrencyIds.Gold));
        }

        [Test]
        public void Credit_RejectsNegativeAmount()
            => Assert.AreEqual(WalletError.InvalidAmount, WalletRules.Credit(Balances(), CurrencyIds.Gold, -1));

        [Test]
        public void Credit_ZeroIsANoOp()
        {
            List<CurrencyBalance> balances = Balances((CurrencyIds.Gold, 5));

            Assert.AreEqual(WalletError.Ok, WalletRules.Credit(balances, CurrencyIds.Gold, 0));
            Assert.AreEqual(5, WalletRules.BalanceOf(balances, CurrencyIds.Gold));
            Assert.AreEqual(1, balances.Count);
        }

        [Test]
        public void Credit_Overflow_IsRefusedRatherThanWrapping()
        {
            List<CurrencyBalance> balances = Balances((CurrencyIds.Gold, long.MaxValue));

            Assert.AreEqual(WalletError.Overflow, WalletRules.Credit(balances, CurrencyIds.Gold, 1));
            Assert.AreEqual(long.MaxValue, WalletRules.BalanceOf(balances, CurrencyIds.Gold));
        }

        [Test]
        public void Debit_SubtractsAndNeverGoesNegative()
        {
            List<CurrencyBalance> balances = Balances((CurrencyIds.Gold, 10));

            Assert.AreEqual(WalletError.Ok, WalletRules.Debit(balances, CurrencyIds.Gold, 6));
            Assert.AreEqual(4, WalletRules.BalanceOf(balances, CurrencyIds.Gold));
        }

        [Test]
        public void Debit_AboveBalance_IsRefusedAndNothingMoves()
        {
            List<CurrencyBalance> balances = Balances((CurrencyIds.Gold, 10));

            Assert.AreEqual(WalletError.InsufficientFunds, WalletRules.Debit(balances, CurrencyIds.Gold, 11));
            Assert.AreEqual(10, WalletRules.BalanceOf(balances, CurrencyIds.Gold));
        }

        [Test]
        public void Debit_UnknownCurrency_IsRefused()
            => Assert.AreEqual(WalletError.UnknownCurrency, WalletRules.Debit(Balances(), CurrencyIds.Gold, 1));

        [Test]
        public void Debit_NegativeAmount_IsRefused()
            => Assert.AreEqual(WalletError.InvalidAmount, WalletRules.Debit(Balances((CurrencyIds.Gold, 1)), CurrencyIds.Gold, -1));

        [Test]
        public void Debit_ZeroIsANoOp()
        {
            List<CurrencyBalance> balances = Balances((CurrencyIds.Gold, 1));

            Assert.AreEqual(WalletError.Ok, WalletRules.Debit(balances, CurrencyIds.Gold, 0));
            Assert.AreEqual(1, WalletRules.BalanceOf(balances, CurrencyIds.Gold));
        }

        [Test]
        public void Set_OverwritesAndCreates()
        {
            List<CurrencyBalance> balances = Balances((CurrencyIds.Gold, 3));

            Assert.AreEqual(WalletError.Ok, WalletRules.Set(balances, CurrencyIds.Gold, 9));
            Assert.AreEqual(WalletError.Ok, WalletRules.Set(balances, CurrencyIds.Silver, 2));

            Assert.AreEqual(9, WalletRules.BalanceOf(balances, CurrencyIds.Gold));
            Assert.AreEqual(2, WalletRules.BalanceOf(balances, CurrencyIds.Silver));
        }

        [Test]
        public void Set_NegativeAmount_IsRefused()
            => Assert.AreEqual(WalletError.InvalidAmount, WalletRules.Set(Balances(), CurrencyIds.Gold, -1));

        [Test]
        public void Transfer_MovesValueAtomically()
        {
            List<CurrencyBalance> from = Balances((CurrencyIds.Gold, 10));
            List<CurrencyBalance> to = Balances();

            Assert.AreEqual(WalletError.Ok, WalletRules.Transfer(from, to, CurrencyIds.Gold, 4));

            Assert.AreEqual(6, WalletRules.BalanceOf(from, CurrencyIds.Gold));
            Assert.AreEqual(4, WalletRules.BalanceOf(to, CurrencyIds.Gold));
        }

        [Test]
        public void Transfer_RefusedWhenSenderShort_LeavesBothSidesUntouched()
        {
            List<CurrencyBalance> from = Balances((CurrencyIds.Gold, 3));
            List<CurrencyBalance> to = Balances();

            Assert.AreEqual(WalletError.InsufficientFunds, WalletRules.Transfer(from, to, CurrencyIds.Gold, 4));

            Assert.AreEqual(3, WalletRules.BalanceOf(from, CurrencyIds.Gold));
            Assert.AreEqual(0, WalletRules.BalanceOf(to, CurrencyIds.Gold));
        }

        [Test]
        public void Transfer_NullDestination_FailsWithoutLosingSenderFunds()
        {
            List<CurrencyBalance> from = Balances((CurrencyIds.Gold, 5));

            Assert.AreEqual(WalletError.NoAccount, WalletRules.Transfer(from, null, CurrencyIds.Gold, 2));
            Assert.AreEqual(5, WalletRules.BalanceOf(from, CurrencyIds.Gold));
        }

        [Test]
        public void Sanitize_DropsEmptyIdsClampsNegativesAndSumsDuplicates()
        {
            var dirty = Balances(
                (CurrencyIds.Gold, -4),
                ("", 9),
                (CurrencyIds.Gold, 6),
                (CurrencyIds.Silver, 2));

            List<CurrencyBalance> clean = WalletRules.Sanitize(dirty);

            Assert.AreEqual(2, clean.Count);
            Assert.AreEqual(6, WalletRules.BalanceOf(clean, CurrencyIds.Gold));
            Assert.AreEqual(2, WalletRules.BalanceOf(clean, CurrencyIds.Silver));
        }

        [Test]
        public void Sanitize_Null_ReturnsEmptyList()
            => Assert.AreEqual(0, WalletRules.Sanitize(null).Count);
    }
}
