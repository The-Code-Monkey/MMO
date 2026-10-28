using System;
using System.Collections.Generic;
using UnityEngine;

namespace MMO.Economy
{
    /// <summary>
    /// Resolves a <see cref="CurrencyData"/> by its currency id or by the item that converts into
    /// it. Both ends of the connection resolve locally from the same assets: the wire only ever
    /// carries an item name or a currency id, never the asset itself.
    ///
    /// <para>
    /// Assets under <c>Resources/Currencies</c> load on demand. A definition that is loaded
    /// registers itself from <see cref="CurrencyData.OnEnable"/>; tests and bootstraps that build
    /// definitions in code call <see cref="Register"/> after giving them a name. A duplicate
    /// currency id replaces the previous entry and says so.
    /// </para>
    /// </summary>
    public static class CurrencyRegistry
    {
        /// <summary>The <c>Resources</c> subfolder the currency assets live under.</summary>
        public const string CurrenciesFolder = "Currencies";

        private static readonly Dictionary<string, CurrencyData> ById = new();
        private static readonly Dictionary<string, CurrencyData> ByItem = new();

        /// <summary>How many currencies are currently registered. Exposed for diagnostics and tests.</summary>
        public static int Count => ById.Count;

        /// <summary>Registers a currency under its id and, when set, its item name.</summary>
        /// <exception cref="ArgumentNullException">The currency is null.</exception>
        /// <exception cref="ArgumentException">The currency has no id yet.</exception>
        public static void Register(CurrencyData currency)
        {
            if (currency == null)
                throw new ArgumentNullException(nameof(currency));

            if (string.IsNullOrEmpty(currency.CurrencyId))
                throw new ArgumentException(
                    $"{nameof(CurrencyRegistry)}: {nameof(currency)} has no {nameof(CurrencyData.CurrencyId)} to register under.",
                    nameof(currency));

            if (ById.TryGetValue(currency.CurrencyId, out CurrencyData existing) && existing != currency)
            {
                Debug.LogWarning(
                    $"{nameof(CurrencyRegistry)}: '{currency.CurrencyId}' is already registered as " +
                    $"'{(existing != null ? existing.name : "<destroyed>")}'; replacing it with '{currency.name}'.");
            }

            ById[currency.CurrencyId] = currency;

            string itemName = currency.CurrencyItemName;
            if (!string.IsNullOrEmpty(itemName))
                ByItem[itemName] = currency;
        }

        /// <summary>Removes a currency if it is still the one its keys point at.</summary>
        public static void Unregister(CurrencyData currency)
        {
            if (currency == null || string.IsNullOrEmpty(currency.CurrencyId))
                return;

            if (ById.TryGetValue(currency.CurrencyId, out CurrencyData existing) && existing == currency)
                ById.Remove(currency.CurrencyId);

            string itemName = currency.CurrencyItemName;
            if (!string.IsNullOrEmpty(itemName) && ByItem.TryGetValue(itemName, out CurrencyData byItem) && byItem == currency)
                ByItem.Remove(itemName);
        }

        /// <summary>The currency with this id, loading it from Resources on a miss, or null.</summary>
        public static CurrencyData Resolve(string currencyId)
        {
            if (string.IsNullOrEmpty(currencyId))
                return null;

            if (ById.TryGetValue(currencyId, out CurrencyData currency) && currency != null)
                return currency;

            foreach (CurrencyData candidate in Resources.LoadAll<CurrencyData>(CurrenciesFolder))
            {
                if (candidate != null && candidate.CurrencyId == currencyId)
                {
                    Register(candidate);
                    return candidate;
                }
            }

            return null;
        }

        /// <summary>The currency that converts from this item, or null when the item is not a currency item.</summary>
        public static CurrencyData ResolveByItem(string itemName)
        {
            if (string.IsNullOrEmpty(itemName))
                return null;

            if (ByItem.TryGetValue(itemName, out CurrencyData currency) && currency != null)
                return currency;

            foreach (CurrencyData candidate in Resources.LoadAll<CurrencyData>(CurrenciesFolder))
            {
                if (candidate != null && candidate.CurrencyItemName == itemName)
                {
                    Register(candidate);
                    return candidate;
                }
            }

            return null;
        }

        /// <summary>Loads every currency asset under <c>Resources/Currencies</c> and registers it.</summary>
        public static void LoadAllFromResources()
        {
            foreach (CurrencyData currency in Resources.LoadAll<CurrencyData>(CurrenciesFolder))
                Register(currency);
        }

        /// <summary>Forgets every registration. Resource assets still resolve through the fallback.</summary>
        public static void Clear()
        {
            ById.Clear();
            ByItem.Clear();
        }
    }
}
