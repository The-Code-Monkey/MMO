using UnityEngine;

namespace MMO.Inventory
{
    /// <summary>
    /// Pure data for one kind of item: what it is called, which <see cref="EquipmentSlot"/>
    /// designates it as worn, how strong the wearer must be to use it, how much room it takes
    /// up in the inventory grid, the player stats it grants while worn, and — for the tools —
    /// what kind of tool it is and what tier it reaches. This is the "you are what you wear"
    /// data half — an asset you right-click into existence from the Project window, never a
    /// scene object.
    ///
    /// Deliberately carries no networking. Balance numbers are not state: the server reads the
    /// same asset the client reads, so there is nothing to replicate and nothing for a client
    /// to lie about. What crosses the wire is only the item's <see cref="ItemName"/>, which both
    /// ends resolve back to their local asset through <see cref="ItemDefinitionRegistry"/> —
    /// FishNet cannot serialize a <c>ScriptableObject</c> reference, and shipping a tweak means
    /// shipping an edited .asset, which diffs cleanly and cannot be patched from the wire.
    ///
    /// The fields are private <c>[SerializeField]</c> with read-only properties, matching the
    /// rest of the project: the Inspector (and the asset YAML) is the only writer, everything
    /// else is a reader.
    /// </summary>
    [CreateAssetMenu(fileName = "New Item Definition", menuName = "MMO/Item Definition")]
    public sealed class ItemDefinition : ScriptableObject
    {
        [SerializeField] private string _itemName = string.Empty;
        [SerializeField] private Sprite _icon;
        [SerializeField] private EquipmentSlot _equipSlot = EquipmentSlot.None;
        [SerializeField, Min(0)] private int _requiredLevel;
        [SerializeField, Min(1)] private int _gridWidth = 1;
        [SerializeField, Min(1)] private int _gridHeight = 1;
        [SerializeField, Min(0)] private int _maxHealthBonus;
        [SerializeField, Min(0)] private int _maxManaBonus;
        [SerializeField, Min(0)] private int _maxDurability;
        [SerializeField] private bool _isStackable;
        [SerializeField, Min(1)] private int _maxStackSize = 1;
        [SerializeField, Min(0)] private long _value;
        [SerializeField] private ToolKind _toolKind = ToolKind.None;
        [SerializeField, Min(0)] private int _toolTier;

        /// <summary>
        /// The stable key both ends of the connection use to identify this item. It is what
        /// <see cref="ItemInstance"/> carries over the wire and what
        /// <see cref="ItemDefinitionRegistry"/> resolves, so it must match the asset's file name
        /// for the <c>Resources.Load</c> fallback to find it.
        /// </summary>
        public string ItemName => _itemName;

        /// <summary>
        /// The optional UI sprite for this item. Null falls back to the item's name, so an
        /// unillustrated item still reads correctly.
        /// </summary>
        public Sprite Icon => _icon;

        /// <summary>The slot this item is designated to when equipped.</summary>
        public EquipmentSlot EquipSlot => _equipSlot;

        /// <summary>The player level required before the server will equip this item.</summary>
        public int RequiredLevel => _requiredLevel;

        /// <summary>
        /// Width of the item's footprint in inventory grid cells, clamped to at least one cell:
        /// a zero-wide item would occupy no cells, which the grid would read as "free" and let
        /// two items stack invisibly.
        /// </summary>
        public int GridWidth => Mathf.Max(1, _gridWidth);

        /// <summary>Height of the item's footprint in inventory grid cells. See <see cref="GridWidth"/>.</summary>
        public int GridHeight => Mathf.Max(1, _gridHeight);

        /// <summary>Maximum health granted while this item is equipped.</summary>
        public int MaxHealthBonus => _maxHealthBonus;

        /// <summary>Maximum mana granted while this item is equipped.</summary>
        public int MaxManaBonus => _maxManaBonus;

        /// <summary>
        /// Durability a fresh instance of this item starts with, and the ceiling a damaged one is
        /// clamped against. Zero means the item is indestructible: it never takes wear and is
        /// never dropped by a safe-zone death.
        /// </summary>
        public int MaxDurability => Mathf.Max(0, _maxDurability);

        public bool IsStackable => _isStackable;

        public int MaxStackSize => _maxStackSize < 1 ? 1 : _maxStackSize;

        /// <summary>The monetary/value of a single item unit (in game currency units).</summary>
        public long Value => _value;

        /// <summary>
        /// What kind of tool this item is, or <see cref="ToolKind.None"/> when it is not a tool
        /// at all. A resource node asks for a kind, and the player has to hold one of it.
        /// </summary>
        public ToolKind ToolKind => _toolKind;

        /// <summary>
        /// The tier this tool reaches. Zero is an unranked tool; a resource that asks for a tier
        /// wants a tool at least that high, so a T1 pickaxe never opens a T2 vein.
        /// </summary>
        public int ToolTier => Mathf.Max(0, _toolTier);

        // A definition deserialized from disk already carries its name here, so loading an asset
        // registers it; one created in code has no name yet and registers after the Inspector
        // (or a test) gives it one, which is why registration is also a public call.
        private void OnEnable()
        {
            if (!string.IsNullOrEmpty(_itemName))
                ItemDefinitionRegistry.Register(this);
        }

        private void OnDisable() => ItemDefinitionRegistry.Unregister(this);
    }
}
