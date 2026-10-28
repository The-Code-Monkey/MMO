using MMO.Economy;
using MMO.Inventory;

namespace MMO.Auction
{
    /// <summary>Resolves a carried currency item into the currency id and amount it credits.</summary>
    internal static class CurrencyItemConverter
    {
        public static bool TryPlan(ItemInstance item, out string currencyId, out long credit)
        {
            currencyId = null;
            credit = 0;

            if (item.IsEmpty)
                return false;

            CurrencyData currency = CurrencyRegistry.ResolveByItem(item.ItemName);
            if (currency == null || string.IsNullOrEmpty(currency.CurrencyId))
                return false;

            int count = item.StackCount < 1 ? 1 : item.StackCount;
            credit = (long)currency.ItemValue * count;
            if (credit <= 0)
                return false;

            currencyId = currency.CurrencyId;
            return true;
        }
    }
}
