using System;
using System.Collections.Generic;
using System.Text;

namespace MMO.Economy
{
    /// <summary>
    /// Formats a balance list for the bank's currency strip, e.g.
    /// <c>"100 Gold - 10 Silver - 2 Copper"</c>. Pure, so the exact wording is unit tested
    /// without a Canvas.
    ///
    /// The three coin denominations always appear, in gold/silver/copper order and zero when the
    /// bank has none, so the strip does not jump around as balances empty. Any other currency is
    /// appended after them when it has a non-zero balance.
    /// </summary>
    public static class CurrencyDisplay
    {
        private static readonly string[] CoinOrder =
        {
            CurrencyIds.Gold,
            CurrencyIds.Silver,
            CurrencyIds.Copper,
        };

        /// <summary>The strip text; "Empty" when there is nothing at all to show.</summary>
        public static string Format(IReadOnlyList<CurrencyBalance> balances)
        {
            if (balances == null || balances.Count == 0)
                return "Empty";

            var amounts = new Dictionary<string, long>(StringComparer.Ordinal);
            for (int i = 0; i < balances.Count; i++)
            {
                string id = balances[i].CurrencyId;
                if (string.IsNullOrEmpty(id))
                    continue;

                amounts[id] = Math.Max(0, balances[i].Amount);
            }

            var builder = new StringBuilder();
            for (int i = 0; i < CoinOrder.Length; i++)
                Append(builder, CoinOrder[i], amounts.TryGetValue(CoinOrder[i], out long amount) ? amount : 0);

            foreach (CurrencyBalance balance in balances)
            {
                if (string.IsNullOrEmpty(balance.CurrencyId) || IsCoin(balance.CurrencyId) || balance.Amount <= 0)
                    continue;

                Append(builder, balance.CurrencyId, balance.Amount);
            }

            return builder.Length == 0 ? "Empty" : builder.ToString();
        }

        /// <summary>A display name for a currency id, falling back to a humanised id.</summary>
        public static string NameOf(string currencyId)
        {
            if (string.IsNullOrEmpty(currencyId))
                return string.Empty;

            CurrencyData data = CurrencyRegistry.Resolve(currencyId);
            if (data != null && !string.IsNullOrEmpty(data.DisplayName))
                return data.DisplayName;

            string[] words = currencyId.Split('_', StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < words.Length; i++)
            {
                if (words[i].Length > 0)
                    words[i] = char.ToUpperInvariant(words[i][0]) + words[i].Substring(1);
            }

            return string.Join(" ", words);
        }

        private static void Append(StringBuilder builder, string currencyId, long amount)
        {
            if (builder.Length > 0)
                builder.Append(" - ");

            builder.Append(amount);
            builder.Append(' ');
            builder.Append(NameOf(currencyId));
        }

        private static bool IsCoin(string currencyId)
        {
            for (int i = 0; i < CoinOrder.Length; i++)
            {
                if (CoinOrder[i] == currencyId)
                    return true;
            }

            return false;
        }
    }
}
