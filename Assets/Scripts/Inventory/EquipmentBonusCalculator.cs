using System.Collections.Generic;

namespace MMO.Inventory
{
    /// <summary>Resolves worn definitions and sums their stat bonuses.</summary>
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
    }
}
