using System;

namespace MMO.Economy
{
    /// <summary>
    /// One currency balance on a wallet: the currency id and how much of it the owner holds.
    /// A struct with public fields because it is both a Unity <c>JsonUtility</c> payload and
    /// (through <see cref="WalletAccount"/>) part of a replicated snapshot. JsonUtility does not
    /// serialize a <c>Dictionary</c>, so wallets carry a list of these instead of a map.
    /// </summary>
    [Serializable]
    public struct CurrencyBalance
    {
        /// <summary>The currency key, e.g. <see cref="CurrencyIds.Gold"/>.</summary>
        public string CurrencyId;

        /// <summary>How much of the currency the owner holds. Never negative.</summary>
        public long Amount;
    }
}
