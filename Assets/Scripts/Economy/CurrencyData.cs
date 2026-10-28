using MMO.Inventory;
using UnityEngine;

namespace MMO.Economy
{
    /// <summary>
    /// Pure data for one currency: its stable id, its display name, and the item that converts
    /// into it. A <see cref="CurrencyData"/> asset is the bridge between the item world (a stack in
    /// the bag) and the wallet world (a balance), mirroring how <see cref="ItemDefinition"/> is the
    /// data half of an item.
    ///
    /// The asset carries no state and nothing is replicated: both ends resolve a currency by its
    /// <see cref="CurrencyId"/> through <see cref="CurrencyRegistry"/>, and the conversion amount is
    /// always recomputed on the server from this asset, never read from a client.
    /// </summary>
    [CreateAssetMenu(fileName = "New Currency Data", menuName = "MMO/Currency Data")]
    public sealed class CurrencyData : ScriptableObject
    {
        [SerializeField] private string _currencyId = CurrencyIds.Gold;
        [SerializeField] private string _displayName = "Gold";

        /// <summary>The item that converts into this currency, or null when no item does.</summary>
        [SerializeField] private ItemDefinition _currencyItem;

        [SerializeField, Min(1)] private int _itemValue = 1;

        /// <summary>The stable currency key stored in wallets, e.g. <see cref="CurrencyIds.Gold"/>.</summary>
        public string CurrencyId => _currencyId;

        /// <summary>The name shown to players.</summary>
        public string DisplayName => _displayName;

        /// <summary>The item that converts into this currency, or null.</summary>
        public ItemDefinition CurrencyItem => _currencyItem;

        /// <summary>The item's <see cref="ItemDefinition.ItemName"/>, or empty when unset.</summary>
        public string CurrencyItemName => _currencyItem != null ? _currencyItem.ItemName : string.Empty;

        /// <summary>Wallet units granted per single unit of <see cref="CurrencyItem"/>.</summary>
        public int ItemValue => Mathf.Max(1, _itemValue);

        private void OnEnable()
        {
            if (!string.IsNullOrEmpty(_currencyId))
                CurrencyRegistry.Register(this);
        }

        private void OnDisable() => CurrencyRegistry.Unregister(this);
    }
}
