using System;
using System.Collections.Generic;
using MMO.Economy;

namespace MMO.Persistence
{
    /// <summary>
    /// The pure half of loading a wallet from a save record. Kept out of the networked
    /// <see cref="Auction.PlayerWallet"/> so the legacy-gold migration is reachable from an EditMode
    /// test without a running server.
    ///
    /// <para>
    /// A save written before the multi-currency wallet carries only <see cref="PlayerSaveRecord.Gold"/>.
    /// When the wallet list is empty that scalar is the whole balance, so it seeds a single gold
    /// entry; once the wallet has been saved it carries <see cref="PlayerSaveRecord.Currencies"/> and
    /// the scalar is only kept for older readers.
    /// </para>
    /// </summary>
    public static class WalletSaveMigration
    {
        /// <summary>The carried wallet, migrating legacy scalar gold when the list is empty.</summary>
        public static List<CurrencyBalance> WalletBalances(PlayerSaveRecord record)
        {
            List<CurrencyBalance> balances = WalletRules.Sanitize(record?.Currencies);
            if (balances.Count == 0 && record != null && record.Gold != 0)
            {
                balances.Add(new CurrencyBalance
                {
                    CurrencyId = CurrencyIds.Gold,
                    Amount = Math.Max(0, record.Gold),
                });
            }

            return balances;
        }

        /// <summary>The personal bank vault balances, sanitized.</summary>
        public static List<CurrencyBalance> BankBalances(PlayerSaveRecord record)
            => WalletRules.Sanitize(record?.BankVault);
    }
}
