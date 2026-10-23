namespace MMO.Gameplay
{
    /// <summary>
    /// What kind of thing a resource node is — an ore vein, a tree, a herb bush. The generic
    /// node wears one of these the way the generic crafting station wears a
    /// <see cref="MMO.Crafting.CraftingStationType"/>: it is the label a player (and a future
    /// UI) reads off a node, and the thing content is sorted by.
    ///
    /// <para>
    /// The enum deliberately says nothing about tiers or yields — those live on the
    /// <see cref="ResourceDefinition"/> asset, so a new kind of resource is a new definition, not
    /// a new number in a table that has to be kept in step with the code.
    /// </para>
    /// </summary>
    public enum ResourceType
    {
        /// <summary>Not a resource. A node wearing this yields nothing and refuses every harvest.</summary>
        None = 0,

        /// <summary>A vein of metal, mined with a pickaxe.</summary>
        Ore = 1,

        /// <summary>A tree, felled with an axe.</summary>
        Lumber = 2,

        /// <summary>A herb bush, cut with a sickle.</summary>
        Herb = 3,

        /// <summary>A fishing spot, worked with a knife or a rod.</summary>
        Fish = 4,
    }
}
