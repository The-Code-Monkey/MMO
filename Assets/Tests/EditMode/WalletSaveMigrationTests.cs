using System.Collections.Generic;
using MMO.Economy;
using MMO.Persistence;
using NUnit.Framework;

namespace MMO.Tests
{
    /// <summary>
    /// The save-to-wallet bridge, isolated: a legacy save carries only scalar gold, a current save
    /// carries a currency list, and the bank vault travels separately. No server, no storage.
    /// </summary>
    public sealed class WalletSaveMigrationTests
    {
        [Test]
        public void WalletBalances_LegacyGold_SeedsGold()
        {
            var record = new PlayerSaveRecord { Gold = 42 };

            List<CurrencyBalance> balances = WalletSaveMigration.WalletBalances(record);

            Assert.AreEqual(1, balances.Count);
            Assert.AreEqual(42, WalletRules.BalanceOf(balances, CurrencyIds.Gold));
        }

        [Test]
        public void WalletBalances_EmptyRecord_IsEmpty()
        {
            List<CurrencyBalance> balances = WalletSaveMigration.WalletBalances(new PlayerSaveRecord());

            Assert.AreEqual(0, balances.Count);
        }

        [Test]
        public void WalletBalances_NullRecord_IsEmpty()
            => Assert.AreEqual(0, WalletSaveMigration.WalletBalances(null).Count);

        [Test]
        public void WalletBalances_CurrencyList_WinsOverLegacyGold()
        {
            var record = new PlayerSaveRecord { Gold = 42 };
            record.Currencies.Add(new CurrencyBalance { CurrencyId = CurrencyIds.Silver, Amount = 7 });

            List<CurrencyBalance> balances = WalletSaveMigration.WalletBalances(record);

            Assert.AreEqual(1, balances.Count);
            Assert.AreEqual(7, WalletRules.BalanceOf(balances, CurrencyIds.Silver));
            Assert.AreEqual(0, WalletRules.BalanceOf(balances, CurrencyIds.Gold));
        }

        [Test]
        public void BankBalances_ReturnsTheVaultSanitized()
        {
            var record = new PlayerSaveRecord();
            record.BankVault.Add(new CurrencyBalance { CurrencyId = CurrencyIds.Gold, Amount = -5 });
            record.BankVault.Add(new CurrencyBalance { CurrencyId = CurrencyIds.Silver, Amount = 3 });

            List<CurrencyBalance> balances = WalletSaveMigration.BankBalances(record);

            Assert.AreEqual(2, balances.Count);
            Assert.AreEqual(0, WalletRules.BalanceOf(balances, CurrencyIds.Gold));
            Assert.AreEqual(3, WalletRules.BalanceOf(balances, CurrencyIds.Silver));
        }

        [Test]
        public void BankBalances_NullRecord_IsEmpty()
            => Assert.AreEqual(0, WalletSaveMigration.BankBalances(null).Count);
    }
}
