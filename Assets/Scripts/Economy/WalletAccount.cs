using System;
using System.Collections.Generic;

namespace MMO.Economy
{
    /// <summary>
    /// An owner's multi-currency wallet. The same shape serves a player wallet (owner = the
    /// player's account id) and, when replicated or saved, the personal bank vault, so one set of
    /// rules covers both.
    ///
    /// <para>
    /// Balances are a list, not a dictionary: <c>JsonUtility</c> cannot serialize a
    /// <c>Dictionary</c>, and this record is replicated and saved as JSON. Look every currency up
    /// through <see cref="WalletRules"/> so the list is never indexed by hand.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class WalletAccount
    {
        /// <summary>The owner key: a player account id or a guild id.</summary>
        public string OwnerId;

        /// <summary>Currency id -> amount. Never negative; use <see cref="WalletRules"/> to mutate.</summary>
        public List<CurrencyBalance> Balances = new List<CurrencyBalance>();

        /// <summary>A wallet with no balances yet, owned by <paramref name="ownerId"/>.</summary>
        public static WalletAccount Empty(string ownerId)
            => new WalletAccount { OwnerId = ownerId };
    }
}
