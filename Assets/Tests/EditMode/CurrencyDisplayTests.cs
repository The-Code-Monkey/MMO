using System.Collections.Generic;
using MMO.Economy;
using NUnit.Framework;

namespace MMO.Tests
{
    /// <summary>
    /// The bank currency strip's wording, isolated: gold/silver/copper always in order, zeros
    /// included, other currencies appended. No Canvas, no registry assets.
    /// </summary>
    public sealed class CurrencyDisplayTests
    {
        private static List<CurrencyBalance> Balances(params (string id, long amount)[] entries)
        {
            var balances = new List<CurrencyBalance>();
            foreach ((string id, long amount) entry in entries)
                balances.Add(new CurrencyBalance { CurrencyId = entry.id, Amount = entry.amount });
            return balances;
        }

        [Test]
        public void Format_ShowsGoldSilverCopperInOrder()
        {
            List<CurrencyBalance> balances = Balances(
                (CurrencyIds.Copper, 2),
                (CurrencyIds.Gold, 100),
                (CurrencyIds.Silver, 10));

            Assert.AreEqual("100 Gold - 10 Silver - 2 Copper", CurrencyDisplay.Format(balances));
        }

        [Test]
        public void Format_MissingCoins_ShowZero()
        {
            List<CurrencyBalance> balances = Balances((CurrencyIds.Gold, 5));

            Assert.AreEqual("5 Gold - 0 Silver - 0 Copper", CurrencyDisplay.Format(balances));
        }

        [Test]
        public void Format_AppendsOtherCurrencies()
        {
            List<CurrencyBalance> balances = Balances(
                (CurrencyIds.Gold, 1),
                (CurrencyIds.FactionMarks, 7));

            Assert.AreEqual("1 Gold - 0 Silver - 0 Copper - 7 Faction Marks", CurrencyDisplay.Format(balances));
        }

        [Test]
        public void Format_Null_IsEmpty()
            => Assert.AreEqual("Empty", CurrencyDisplay.Format(null));

        [Test]
        public void Format_EmptyList_IsEmpty()
            => Assert.AreEqual("Empty", CurrencyDisplay.Format(new List<CurrencyBalance>()));
    }
}
