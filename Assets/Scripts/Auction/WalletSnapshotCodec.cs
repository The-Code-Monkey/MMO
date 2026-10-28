using System.Collections.Generic;
using MMO.Economy;
using UnityEngine;

namespace MMO.Auction
{
    /// <summary>JSON codec for the replicated wallet and bank-vault snapshots.</summary>
    internal static class WalletSnapshotCodec
    {
        public static string EncodeAccount(WalletAccount account)
            => JsonUtility.ToJson(account);

        public static WalletAccount DecodeAccount(string json)
        {
            if (string.IsNullOrEmpty(json))
                return null;

            return JsonUtility.FromJson<WalletAccount>(json);
        }

        public static string EncodeBank(IReadOnlyList<CurrencyBalance> balances)
            => JsonUtility.ToJson(new WalletAccount { Balances = balances as List<CurrencyBalance> ?? new List<CurrencyBalance>(balances ?? new List<CurrencyBalance>()) });

        public static List<CurrencyBalance> DecodeBank(string json)
        {
            WalletAccount parsed = DecodeAccount(json);
            return parsed?.Balances ?? new List<CurrencyBalance>();
        }
    }
}
