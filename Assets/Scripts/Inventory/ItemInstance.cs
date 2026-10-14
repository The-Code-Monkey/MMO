using System;
using UnityEngine;

namespace MMO.Inventory
{
    /// <summary>
    /// One item as it exists inside a container: which definition it is, where it sits in the
    /// inventory grid, and how many cells it covers. This is the element type of the
    /// <c>SyncList&lt;ItemInstance&gt;</c> that <c>PlayerInventory</c> replicates, so its shape
    /// is a wire contract.
    ///
    /// <para>
    /// FishNet auto-generates serializers from <b>public fields only</b> — private
    /// <c>[SerializeField]</c> fields, which are the project convention everywhere else, are
    /// silently skipped and would replicate an all-defaults struct. That is why this payload
    /// type follows the same shape as the project's broadcast structs instead: public fields,
    /// no setters hiding behind properties, and no FishNet types of its own (it travels through
    /// a serializer; it is not a <c>NetworkBehaviour</c>).
    /// </para>
    ///
    /// <para>
    /// The definition itself does not travel: a <c>ScriptableObject</c> reference cannot cross
    /// the wire, so only <see cref="ItemName"/> does and each end resolves it locally.
    /// </para>
    /// </summary>
    [Serializable]
    public struct ItemInstance
    {
        /// <summary>The <see cref="ItemDefinition.ItemName"/> key of the definition. Null or empty means no item.</summary>
        public string ItemName;

        /// <summary>
        /// The server-assigned identity of this instance, unique across the whole game and never
        /// supplied by a client. It is what the single-writer <see cref="ItemOwnershipRegistry"/>
        /// tracks, so the same item can never be granted to two holders — the structural half of
        /// dupe protection. Null or empty means "no item" (an empty cell, or a legacy save entry
        /// that has not been assigned an id on load yet).
        /// </summary>
        public string InstanceId;

        /// <summary>Left edge of the item's footprint in grid columns.</summary>
        public int GridX;

        /// <summary>Top edge of the item's footprint in grid rows.</summary>
        public int GridY;

        /// <summary>Footprint width in cells, copied from the definition when the item is created.</summary>
        public int Width;

        /// <summary>Footprint height in cells, copied from the definition when the item is created.</summary>
        public int Height;

        public int StackCount;

        /// <summary>
        /// Remaining durability of this instance. Zero for an empty instance and for an
        /// indestructible item (its definition reports <see cref="ItemDefinition.MaxDurability"/> 0);
        /// otherwise it starts at the definition's maximum and is worn down by
        /// <see cref="MMO.Gameplay.RiskRules.WearDurability"/>.
        /// </summary>
        public int Durability;

        /// <summary>True when this instance stands for an empty cell or an empty equipment slot.</summary>
        public bool IsEmpty => string.IsNullOrEmpty(ItemName);

        /// <summary>The value an empty cell or vacant equipment slot holds.</summary>
        public static ItemInstance Empty => default;

        /// <summary>
        /// Places a fresh instance of <paramref name="definition"/> at a grid position, copying
        /// the definition's footprint so grid math never has to resolve an asset.
        /// </summary>
        /// <exception cref="ArgumentNullException">The definition is null.</exception>
        public static ItemInstance Create(ItemDefinition definition, int gridX, int gridY)
        {
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));

            return new ItemInstance
            {
                ItemName = definition.ItemName,
                InstanceId = NewInstanceId(),
                GridX = gridX,
                GridY = gridY,
                Width = definition.GridWidth,
                Height = definition.GridHeight,
                StackCount = definition.IsStackable ? 1 : 1,
                Durability = definition.MaxDurability,
            };
        }

        public static ItemInstance Create(ItemDefinition definition, int gridX, int gridY, int stackCount)
        {
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));

            int count = stackCount < 1 ? 1 : stackCount;
            if (definition.IsStackable && definition.MaxStackSize >= 1 && count > definition.MaxStackSize)
                count = definition.MaxStackSize;

            return new ItemInstance
            {
                ItemName = definition.ItemName,
                InstanceId = NewInstanceId(),
                GridX = gridX,
                GridY = gridY,
                Width = definition.GridWidth,
                Height = definition.GridHeight,
                StackCount = count,
                Durability = definition.MaxDurability,
            };
        }

        /// <summary>
        /// Generates a fresh, server-side instance id. Only server code creates items, so a client
        /// can never inject one; the id is a plain wire field purely so the server can recognise
        /// the same physical item across containers and saves.
        /// </summary>
        public static string NewInstanceId()
        {
            return Guid.NewGuid().ToString("N");
        }

        /// <summary>
        /// Returns this instance with a guaranteed identity: an empty instance is untouched, an
        /// instance that already has an id keeps it, and a legacy instance with no id is assigned a
        /// fresh one. Called when loading a save written before instance ids existed.
        /// </summary>
        public ItemInstance WithIdentity()
        {
            if (!IsEmpty && string.IsNullOrEmpty(InstanceId))
                InstanceId = NewInstanceId();

            return this;
        }

        public long GetTotalValue(ItemDefinition definition)
        {
            if (IsEmpty || definition == null)
                return 0;

            int count = StackCount < 1 ? 1 : StackCount;
            return definition.Value * count;
        }

        public long GetTotalValue()
        {
            if (IsEmpty)
                return 0;

            ItemDefinition definition = ItemDefinitionRegistry.Resolve(ItemName);
            if (definition == null)
                return 0;

            int count = StackCount < 1 ? 1 : StackCount;
            return definition.Value * count;
        }
    }
}
