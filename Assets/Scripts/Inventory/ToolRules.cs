using System.Collections.Generic;

namespace MMO.Inventory
{
    /// <summary>
    /// The tool half of the gathering gate: what kind of tool an item is, what tier it reaches,
    /// and whether the tools a player carries satisfy what a resource asks for.
    ///
    /// <para>
    /// A tool's identity lives on its <see cref="ItemDefinition"/> — a <see cref="ToolKind"/>
    /// and a <see cref="ItemDefinition.ToolTier"/> — and the wire only ever carries the item's
    /// name, so this class resolves each held item back to its definition through
    /// <see cref="ItemDefinitionRegistry"/>. An item nobody can resolve is simply not a tool:
    /// it contributes no kind and no tier, and is never an exception. A missing definition is
    /// data the server has to refuse, not a crash.
    /// </para>
    ///
    /// <para>
    /// The rules themselves are static, side-effect free and take the containers as arguments,
    /// so an EditMode test can lay out a bag and ask whether a T1 pickaxe opens a T2 vein
    /// without a server, a scene or a frame.
    /// </para>
    /// </summary>
    public static class ToolRules
    {
        /// <summary>
        /// The tier of "no tool at all". Distinct from tier zero, which is a real tool that
        /// simply has not been ranked up.
        /// </summary>
        public const int NoTool = -1;

        /// <summary>
        /// The kind of tool <paramref name="itemName"/> is, or <see cref="ToolKind.None"/> when
        /// the item is unknown or is not a tool.
        /// </summary>
        public static ToolKind KindOf(string itemName)
        {
            ItemDefinition definition = ItemDefinitionRegistry.Resolve(itemName);
            return definition != null ? definition.ToolKind : ToolKind.None;
        }

        /// <summary>
        /// The tier <paramref name="itemName"/> reaches, or zero when the item is unknown or is
        /// not a tool.
        /// </summary>
        public static int TierOf(string itemName)
        {
            ItemDefinition definition = ItemDefinitionRegistry.Resolve(itemName);
            return definition != null ? definition.ToolTier : 0;
        }

        /// <summary>
        /// The best tier the player reaches with a tool of <paramref name="kind"/>, looking in
        /// the bag and in the equipment, or <see cref="NoTool"/> when they hold none. A worn
        /// tool counts exactly as much as a carried one: the server does not care which.
        /// </summary>
        public static int BestTier(
            IReadOnlyList<ItemInstance> bag,
            IReadOnlyList<ItemInstance> equipment,
            ToolKind kind)
        {
            int best = BestTierIn(bag, kind);
            int worn = BestTierIn(equipment, kind);
            return worn > best ? worn : best;
        }

        /// <summary>
        /// The best tier the player reaches with any tool at all, or <see cref="NoTool"/> when
        /// they hold none. This is the reading a resource with no named tool uses: "bring
        /// something of at least this tier", whichever kind it happens to be.
        /// </summary>
        public static int BestToolTier(IReadOnlyList<ItemInstance> bag, IReadOnlyList<ItemInstance> equipment)
        {
            int best = BestToolTierIn(bag);
            int worn = BestToolTierIn(equipment);
            return worn > best ? worn : best;
        }

        /// <summary>
        /// True when the tools the player carries satisfy a resource's requirement.
        ///
        /// <para>
        /// Two conditions, and both have to hold. The <em>kind</em> condition applies when the
        /// resource names a tool: the player must hold one of that kind, in the bag or worn.
        /// The <em>tier</em> condition applies when the resource asks for a tier: the tool that
        /// counts must reach at least that tier — which is what refuses a T1 pickaxe on a T2
        /// vein. A resource that names no tool and asks for no tier is harvestable by hand.
        /// </para>
        ///
        /// <para>
        /// A resource that names no tool but does ask for a tier still needs a tool: bare hands
        /// reach no tier, so the gatherer has to bring something of the required tier, of any
        /// kind. There is no way to write "T2 or above" and have bare hands slip through.
        /// </para>
        /// </summary>
        public static bool Satisfies(
            IReadOnlyList<ItemInstance> bag,
            IReadOnlyList<ItemInstance> equipment,
            ToolKind requiredTool,
            int requiredTier)
        {
            int tier = requiredTier < 0 ? 0 : requiredTier;

            if (requiredTool == ToolKind.None)
            {
                // No tool named: hands will do, unless the resource asks for a tier — then any
                // tool of at least that tier will do.
                return tier == 0 || BestToolTier(bag, equipment) >= tier;
            }

            int best = BestTier(bag, equipment, requiredTool);
            return best >= 0 && best >= tier;
        }

        private static int BestTierIn(IReadOnlyList<ItemInstance> items, ToolKind kind)
        {
            if (items == null || kind == ToolKind.None)
                return NoTool;

            int best = NoTool;
            for (int i = 0; i < items.Count; i++)
            {
                ItemInstance instance = items[i];
                if (instance.IsEmpty)
                    continue;

                ItemDefinition definition = ItemDefinitionRegistry.Resolve(instance.ItemName);
                if (definition == null || definition.ToolKind != kind)
                    continue;

                int tier = definition.ToolTier;
                if (tier > best)
                    best = tier;
            }

            return best;
        }

        private static int BestToolTierIn(IReadOnlyList<ItemInstance> items)
        {
            if (items == null)
                return NoTool;

            int best = NoTool;
            for (int i = 0; i < items.Count; i++)
            {
                ItemInstance instance = items[i];
                if (instance.IsEmpty)
                    continue;

                ItemDefinition definition = ItemDefinitionRegistry.Resolve(instance.ItemName);
                if (definition == null || definition.ToolKind == ToolKind.None)
                    continue;

                int tier = definition.ToolTier;
                if (tier > best)
                    best = tier;
            }

            return best;
        }
    }
}
