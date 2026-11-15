using System.Collections.Generic;

namespace MMO.Inventory
{
    /// <summary>
    /// Resolves worn definitions and sums their stat bonuses. The definition-only sums describe a
    /// fully repaired set; the instance overloads below scale each contribution by the item's
    /// actual condition, which is what the live inventory and combat read.
    /// </summary>
    public static class EquipmentBonusCalculator
    {
        public static void Collect(
            IReadOnlyList<ItemInstance> equipment,
            List<ItemDefinition> target)
        {
            target.Clear();
            if (equipment == null)
                return;

            for (int i = 0; i < equipment.Count; i++)
            {
                ItemInstance worn = equipment[i];
                if (worn.IsEmpty)
                    continue;

                ItemDefinition definition = ItemDefinitionRegistry.Resolve(worn.ItemName);
                if (definition != null)
                    target.Add(definition);
            }
        }

        public static int MaxHealthBonus(IReadOnlyList<ItemDefinition> definitions)
        {
            if (definitions == null)
                return 0;

            int total = 0;
            for (int i = 0; i < definitions.Count; i++)
            {
                if (definitions[i] != null)
                    total += definitions[i].MaxHealthBonus;
            }

            return total;
        }

        public static int MaxManaBonus(IReadOnlyList<ItemDefinition> definitions)
        {
            if (definitions == null)
                return 0;

            int total = 0;
            for (int i = 0; i < definitions.Count; i++)
            {
                if (definitions[i] != null)
                    total += definitions[i].MaxManaBonus;
            }

            return total;
        }

        /// <summary>
        /// Total health granted by worn instances, each scaled by its own condition
        /// (<see cref="DegradationRules"/>). The live inventory uses this overload so a tired
        /// chestplate grants less; an indestructible item still grants its full bonus.
        /// </summary>
        public static int MaxHealthBonus(IReadOnlyList<ItemInstance> equipment)
        {
            if (equipment == null)
                return 0;

            int total = 0;
            for (int i = 0; i < equipment.Count; i++)
            {
                ItemInstance worn = equipment[i];
                if (worn.IsEmpty)
                    continue;

                ItemDefinition definition = ItemDefinitionRegistry.Resolve(worn.ItemName);
                if (definition != null)
                    total += DegradationRules.Effective(
                        definition.MaxHealthBonus, worn.Durability, definition.MaxDurability);
            }

            return total;
        }

        /// <summary>Total mana granted by worn instances, each scaled by its own condition. See the health overload.</summary>
        public static int MaxManaBonus(IReadOnlyList<ItemInstance> equipment)
        {
            if (equipment == null)
                return 0;

            int total = 0;
            for (int i = 0; i < equipment.Count; i++)
            {
                ItemInstance worn = equipment[i];
                if (worn.IsEmpty)
                    continue;

                ItemDefinition definition = ItemDefinitionRegistry.Resolve(worn.ItemName);
                if (definition != null)
                    total += DegradationRules.Effective(
                        definition.MaxManaBonus, worn.Durability, definition.MaxDurability);
            }

            return total;
        }
    }
}
