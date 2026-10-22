using MMO.Inventory;
using UnityEditor;
using UnityEngine;

namespace MMO.Tests
{
    /// <summary>
    /// Shared factory for code-built <see cref="ItemDefinition"/> fixtures. Writes every field
    /// through <see cref="SerializedObject"/> — the exact path the Inspector uses — so the
    /// properties are proven against Unity's own serialization instead of a hand-rolled field
    /// poke, and registers the result so server-side resolution can find it in the same tests
    /// that created it.
    /// </summary>
    public static class TestItemDefinitions
    {
        /// <summary>
        /// Writes <paramref name="definition"/> in place and returns it. Registration can be
        /// skipped for tests that only exercise the asset's data surface.
        /// </summary>
        public static ItemDefinition Write(
            ItemDefinition definition,
            string name,
            int width = 1,
            int height = 1,
            EquipmentSlot equipSlot = EquipmentSlot.MainHand,
            int requiredLevel = 0,
            int maxHealthBonus = 0,
            int maxManaBonus = 0,
            int maxDurability = 0,
            ToolKind toolKind = ToolKind.None,
            int toolTier = 0,
            bool register = true)
        {
            var inspector = new SerializedObject(definition);
            inspector.FindProperty("_itemName").stringValue = name;
            inspector.FindProperty("_equipSlot").intValue = (int)equipSlot;
            inspector.FindProperty("_requiredLevel").intValue = requiredLevel;
            inspector.FindProperty("_gridWidth").intValue = width;
            inspector.FindProperty("_gridHeight").intValue = height;
            inspector.FindProperty("_maxHealthBonus").intValue = maxHealthBonus;
            inspector.FindProperty("_maxManaBonus").intValue = maxManaBonus;
            inspector.FindProperty("_maxDurability").intValue = maxDurability;
            inspector.FindProperty("_toolKind").intValue = (int)toolKind;
            inspector.FindProperty("_toolTier").intValue = toolTier;
            inspector.ApplyModifiedPropertiesWithoutUndo();

            if (register)
                ItemDefinitionRegistry.Register(definition);

            return definition;
        }

        /// <summary>Creates a fresh definition, writes it, and registers it under its name.</summary>
        public static ItemDefinition Create(
            string name,
            int width = 1,
            int height = 1,
            EquipmentSlot equipSlot = EquipmentSlot.MainHand,
            int requiredLevel = 0,
            int maxHealthBonus = 0,
            int maxManaBonus = 0,
            int maxDurability = 0,
            ToolKind toolKind = ToolKind.None,
            int toolTier = 0)
        {
            return Write(
                ScriptableObject.CreateInstance<ItemDefinition>(),
                name,
                width,
                height,
                equipSlot,
                requiredLevel,
                maxHealthBonus,
                maxManaBonus,
                maxDurability,
                toolKind,
                toolTier);
        }
    }
}
