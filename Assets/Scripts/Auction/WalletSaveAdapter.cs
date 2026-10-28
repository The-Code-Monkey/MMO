using System.Collections.Generic;
using MMO.Economy;
using MMO.Persistence;

namespace MMO.Auction
{
    /// <summary>Maps the live wallet and bank vault to and from the player save record.</summary>
    internal static class WalletSaveAdapter
    {
        public static void Load(
            PlayerSaveRecord record,
            string accountId,
            out WalletAccount account,
            out List<CurrencyBalance> bankVault)
        {
            account = WalletAccount.Empty(accountId);
            bankVault = new List<CurrencyBalance>();

            if (record == null)
                return;

            account.Balances = WalletSaveMigration.WalletBalances(record);
            bankVault = WalletSaveMigration.BankBalances(record);
        }

        public static void Capture(
            PlayerSaveRecord record,
            WalletAccount account,
            IReadOnlyList<CurrencyBalance> bankVault,
            long gold)
        {
            if (record == null)
                return;

            record.Currencies = WalletRules.Sanitize(account.Balances);
            record.BankVault = WalletRules.Sanitize(bankVault);
            record.Gold = gold;
        }
    }
}
